using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Debits.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Debits.Infrastructure.LocalStt;

public sealed class LocalSttOptions
{
    public const string Section = "LocalStt";
    public const string HttpClientName = "local-stt";

    /// <summary>Base address of the <c>stt</c> container (<c>deploy/stt</c>), e.g. http://stt:8000. Empty = not used.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>CPU inference of a medium model takes a few seconds per voice message.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>After a failed call the service is left alone this long, so a stopped container costs one timeout, not one per message.</summary>
    public int CooldownSeconds { get; set; } = 60;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>
/// Client of the optional self-hosted Uzbek speech model. It replaces only the Uzbek pass of <see cref="Groq.GroqSpeechToText"/>:
/// the Uzbek fine-tunes do not understand Russian, so Russian speech keeps going to Whisper large-v3.
/// Every failure returns null and the caller falls back to Groq.
/// </summary>
public sealed class LocalSttClient(IHttpClientFactory httpFactory, IOptions<LocalSttOptions> options, TimeProvider clock, ILogger<LocalSttClient> logger)
{
    private readonly LocalSttOptions _options = options.Value;
    private long _retryAfterTicks;

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Uzbek transcript, or null when the service is not configured, cooling down after a failure, or failed just now.</summary>
    public async Task<Transcript?> TryTranscribeUzbekAsync(byte[] audio, string fileName, string prompt, CancellationToken ct)
    {
        if (!IsConfigured || clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _retryAfterTicks))
        {
            return null;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var url = $"{_options.Url.TrimEnd('/')}/transcribe?language=uz&name={Uri.EscapeDataString(fileName)}&prompt={Uri.EscapeDataString(prompt)}";
            using var content = new ByteArrayContent(audio);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await httpFactory.CreateClient(LocalSttOptions.HttpClientName).PostAsync(url, content, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Local STT failed with {(int)response.StatusCode}: {(body.Length <= 300 ? body : body[..300])}", null, response.StatusCode);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var text = (root.GetProperty("text").GetString() ?? string.Empty).Trim();
            var duration = root.TryGetProperty("duration", out var d) && d.TryGetDouble(out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
            logger.LogInformation("Local STT (uz): {Bytes} bytes, audio {AudioSeconds:0.0}s, took {ElapsedMs} ms, text «{Text}»", audio.Length, duration.TotalSeconds, sw.ElapsedMilliseconds, text);
            return new Transcript(text, "uzbek", duration);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _retryAfterTicks, clock.GetUtcNow().AddSeconds(_options.CooldownSeconds).UtcTicks);
            logger.LogWarning(ex, "Local STT is unavailable; Groq takes the Uzbek pass for the next {Seconds} s", _options.CooldownSeconds);
            return null;
        }
    }
}
