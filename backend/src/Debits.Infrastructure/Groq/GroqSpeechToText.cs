using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Debits.Application.Abstractions;
using Debits.Domain.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Debits.Infrastructure.Groq;

/// <summary>
/// Groq OpenAI-compatible <c>audio/transcriptions</c> (Whisper) for shops that speak Uzbek, Russian or both.
/// Measured on real shop recordings: auto-detection takes Uzbek for Kazakh / Turkish and garbles it; forcing Uzbek with
/// an Uzbek example-phrase prompt reads it correctly; Russian forced through the Uzbek decoder is garbage, while
/// auto-detection reads Russian well. Neither setting works for both languages, so every recording is transcribed twice
/// in parallel (auto with a Russian prompt, and forced Uzbek with an Uzbek prompt + the shop's customer names) and the
/// result is chosen by content: Uzbek number / debt words in the Uzbek pass mean the speech was Uzbek.
/// </summary>
public sealed class GroqSpeechToText(IHttpClientFactory httpFactory, IOptions<GroqOptions> options, ILogger<GroqSpeechToText> logger) : ISpeechToText
{
    /// <summary>Example phrases steer spelling far better than a word list ("besh yuz ellik ming" instead of "бейшус элик минсом").</summary>
    public const string UzbekPrompt =
        "Do'kon nasiya daftari. Besh yuz ellik ming so'm qarz. Bir million so'm qaytardi. Ikki yuz ming so'm to'ladi, bir haftaga. " +
        "Uch yuz ming nasiya oldi, oy oxirigacha.";

    /// <summary>No customer names here: a name list in the Russian prompt makes Whisper drop the name the phrase starts with.</summary>
    public const string RussianPrompt =
        "Долговая тетрадь магазина. Пятьсот пятьдесят тысяч сум, долг. Вернул миллион сум. Оплатил двести тысяч. " +
        "Взял в долг триста тысяч на неделю, до конца месяца.";

    /// <summary>Whisper keeps only the last 224 prompt tokens, so the name list is capped.</summary>
    public const int MaxNamesChars = 220;

    private static readonly HashSet<string> SlavicLookalikes = new(StringComparer.OrdinalIgnoreCase) { "ukrainian", "belarusian", "bulgarian", "serbian", "macedonian" };

    /// <summary>Words that only Uzbek speech produces in the Uzbek pass (Russian forced through it gives "Nader 550 tysik sum. Dolg.").</summary>
    private static readonly HashSet<string> UzbekWords =
    [
        "ikki", "tort", "besh", "olti", "yetti", "sakkiz", "toqqiz", "yigirma", "ottiz", "qirq", "ellik", "oltmish", "yetmish", "sakson", "toqson", "yuz", "ming", "yarim",
        "berdi", "oldi", "uzdi", "haftaga", "oyga", "kunga", "oxirigacha", "ertaga", "indinga",
    ];

    private static readonly string[] UzbekStems = ["qarz", "nasiya", "tola", "tolov", "qaytar"];

    private readonly GroqOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<Transcript> TranscribeAsync(Stream audio, string fileName, IReadOnlyList<string>? knownNames = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var uzbekPrompt = WithNames(UzbekPrompt, knownNames ?? []);

        // A language fixed in configuration switches the two-pass logic off.
        if (!string.IsNullOrWhiteSpace(_options.SttLanguage))
        {
            var language = _options.SttLanguage.Trim().ToLowerInvariant();
            return await CallAsync(bytes, fileName, language, language == "ru" ? RussianPrompt : uzbekPrompt, cancellationToken);
        }

        var autoTask = TryCallAsync(bytes, fileName, null, RussianPrompt, cancellationToken);
        var uzbekTask = TryCallAsync(bytes, fileName, "uz", uzbekPrompt, cancellationToken);
        var (auto, autoError) = await autoTask;
        var (uzbek, uzbekError) = await uzbekTask;

        if (uzbek is not null && SoundsUzbek(uzbek.Text))
        {
            return uzbek;
        }

        if (auto is not null && IsRussian(auto))
        {
            return auto;
        }

        // Russian speech that auto-detection took for Ukrainian / Bulgarian: one more pass with the language fixed.
        if (auto is not null && SlavicLookalikes.Contains(auto.DetectedLanguage ?? string.Empty))
        {
            var (russian, _) = await TryCallAsync(bytes, fileName, "ru", RussianPrompt, cancellationToken);
            if (russian is { Text.Length: > 0 })
            {
                return russian;
            }
        }

        return uzbek ?? auto ?? throw (uzbekError ?? autoError)!;
    }

    /// <summary>Detected as Russian and written only with Russian letters.</summary>
    public static bool IsRussian(Transcript transcript)
    {
        if (transcript.Text.Length == 0 || !string.Equals(transcript.DetectedLanguage, "russian", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var ch in transcript.Text)
        {
            if (char.IsLetter(ch) && !(char.ToLowerInvariant(ch) is (>= 'а' and <= 'я') or 'ё'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool SoundsUzbek(string text) => SearchKey.NormalizeText(text)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Any(w => UzbekWords.Contains(w) || UzbekStems.Any(s => w.StartsWith(s, StringComparison.Ordinal)));

    /// <summary>Names go first so the prompt still ends with a phrase; Cyrillic names are transliterated (customer search is script-insensitive).</summary>
    private static string WithNames(string prompt, IReadOnlyList<string> names)
    {
        var sb = new StringBuilder();
        foreach (var name in names.Select(LatinName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (sb.Length + name.Length + 2 > MaxNamesChars)
            {
                break;
            }

            sb.Append(sb.Length == 0 ? string.Empty : ", ").Append(name);
        }

        return sb.Length == 0 ? prompt : $"Mijozlar: {sb}. {prompt}";
    }

    private static string LatinName(string name)
    {
        name = name.Trim();
        if (!name.Any(c => c is >= '\u0400' and <= '\u04FF'))
        {
            return name;
        }

        return string.Join(' ', UzTranslit.ToLatin(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private async Task<(Transcript? Result, Exception? Error)> TryCallAsync(byte[] bytes, string fileName, string? language, string prompt, CancellationToken ct)
    {
        try
        {
            return (await CallAsync(bytes, fileName, language, prompt, ct), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Groq STT pass ({Requested}) failed", language ?? "auto");
            return (null, ex);
        }
    }

    private async Task<Transcript> CallAsync(byte[] bytes, string fileName, string? language, string prompt, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
            form.Add(new StringContent(_options.SttModel), "model");
            form.Add(new StringContent("verbose_json"), "response_format");
            form.Add(new StringContent("0"), "temperature");
            form.Add(new StringContent(prompt), "prompt");
            if (language is not null)
            {
                form.Add(new StringContent(language), "language");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = form };
            using var response = await httpFactory.CreateClient(GroqOptions.HttpClientName).SendAsync(request, ct);
            if (attempt == 1 && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
            {
                logger.LogWarning("Groq STT returned {Status}; retrying once", (int)response.StatusCode);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Groq STT failed with {(int)response.StatusCode}: {Truncate(body)}", null, response.StatusCode);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var t) ? (t.GetString() ?? string.Empty).Trim() : string.Empty;
            var detected = root.TryGetProperty("language", out var l) ? l.GetString() : null;
            var duration = root.TryGetProperty("duration", out var d) && d.TryGetDouble(out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

            logger.LogInformation("Groq STT {Model} ({Requested}): {Bytes} bytes, audio {AudioSeconds:0.0}s, language {Language}, took {ElapsedMs} ms, text «{Text}»",
                _options.SttModel, language ?? "auto", bytes.Length, duration.TotalSeconds, detected, sw.ElapsedMilliseconds, text);
            return new Transcript(text, detected, duration);
        }
    }

    internal static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
