using Debits.Application.Abstractions;
using Debits.Application.Entries;
using Debits.Domain.Entities;
using Debits.Infrastructure.DependencyInjection;
using Debits.Infrastructure.Groq;
using Microsoft.Extensions.DependencyInjection;

namespace Debits.Integration.Tests.Groq;

/// <summary>Runs only when the GROQ_API_KEY environment variable is set (the .env file is deliberately not read): real network, real quota.</summary>
public sealed class GroqTheoryAttribute : TheoryAttribute
{
    public GroqTheoryAttribute() => Skip = GroqLive.SkipReason;
}

public sealed class GroqFactAttribute : FactAttribute
{
    public GroqFactAttribute() => Skip = GroqLive.SkipReason;
}

internal static class GroqLive
{
    public static string? SkipReason =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "GROQ_API_KEY is not set" : null;
}

public class GroqLiveTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<GroqOptions>(o => o.ApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? string.Empty);
        services.AddDebitsInfrastructure("Host=unused");
        return services.BuildServiceProvider();
    }

    [GroqTheory]
    [InlineData("Ahmad akaga besh yuz ellik ming qarz", "debt", "Ahmad aka", 550_000L, null)]
    [InlineData("Зулфия опага икки юз эллик минг сўм насия, бир ҳафтага", "debt", "Зулфия опа", 250_000L, "2026-10-16")]
    [InlineData("Karim stomatolog bir yarim million to'ladi", "payment", "Karim stomatolog", 1_500_000L, null)]
    [InlineData("Ахмад ака взял в долг пятьсот тысяч до конца месяца", "debt", "Ахмад ака", 500_000L, "2026-10-31")]
    [InlineData("Nodir uka qarzini berdi 120 ming", "payment", "Nodir uka", 120_000L, null)]
    [InlineData("запиши Рустама на 2 миллиона 300 тысяч на две недели", "debt", "Рустам", 2_300_000L, "2026-10-23")]
    [InlineData("Dilnoza opa 80 ming qarz oldi 3 kunga non bilan sut", "debt", "Dilnoza opa", 80_000L, "2026-10-12")]
    public async Task Llm_ReadsThePhrase(string text, string intent, string customer, long amount, string? due)
    {
        await using var sp = Services();
        var parser = sp.GetServices<ILlmEntryParser>().Single();

        var result = await parser.ParseAsync(text, Today, AppLanguage.UzLatn);

        (result.Intent == TransactionType.Debt ? "debt" : result.Intent == TransactionType.Payment ? "payment" : null).Should().Be(intent);
        result.CustomerName.Should().Be(customer);
        result.AmountUzs.Should().Be(amount);
        result.DueDate?.ToString("yyyy-MM-dd").Should().Be(due);
    }

    /// <summary>Contract check of the transcription endpoint with one second of a synthetic tone: no speech, but the request must be accepted and parsed.</summary>
    [GroqFact]
    public async Task Stt_AcceptsAudio_AndReturnsDuration()
    {
        await using var sp = Services();
        var stt = sp.GetRequiredService<ISpeechToText>();

        var transcript = await stt.TranscribeAsync(new MemoryStream(ToneWav(seconds: 1)), "tone.wav");

        stt.IsConfigured.Should().BeTrue();
        transcript.Duration.TotalSeconds.Should().BeApproximately(1, 0.3);
    }

    /// <summary>
    /// Tuning aid, not an assertion: transcribes every recording in DEBITS_VOICE_DIR and prints text → regex reading.
    /// DEBITS_VOICE_NAMES is an optional comma-separated customer list for the spelling hint.
    /// </summary>
    [GroqFact]
    public async Task Stt_Samples()
    {
        var dir = Environment.GetEnvironmentVariable("DEBITS_VOICE_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        var names = (Environment.GetEnvironmentVariable("DEBITS_VOICE_NAMES") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await using var sp = Services();
        var stt = sp.GetRequiredService<ISpeechToText>();
        foreach (var file in Directory.GetFiles(dir).Where(f => f.EndsWith(".ogg") || f.EndsWith(".oga") || f.EndsWith(".mp3") || f.EndsWith(".wav")).OrderBy(f => f))
        {
            await using var stream = File.OpenRead(file);
            var t = await stt.TranscribeAsync(stream, Path.GetFileName(file), names);
            var p = RegexEntryParser.Parse(t.Text, Today);
            output.WriteLine($"{Path.GetFileName(file),-8} [{t.DetectedLanguage}] «{t.Text}» → {p.Intent?.ToString() ?? "?"} | {p.CustomerName ?? "?"} | {p.AmountUzs?.ToString() ?? "?"}");
        }
    }

    private static byte[] ToneWav(int seconds)
    {
        const int rate = 16000;
        var samples = rate * seconds;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
        }

        w.Flush();
        return ms.ToArray();
    }
}
