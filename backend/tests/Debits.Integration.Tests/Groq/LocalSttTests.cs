using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Debits.Infrastructure.Groq;
using Debits.Infrastructure.LocalStt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Debits.Integration.Tests.Groq;

/// <summary>The optional self-hosted Uzbek model takes the Uzbek pass; Groq keeps Russian and stays the fallback (stub HTTP, no network).</summary>
public class LocalSttTests
{
    private sealed class Handler(Func<Uri, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add(request.RequestUri!);
            }

            return respond(request.RequestUri!, body);
        }

        public int Local => Requests.Count(IsLocal);
        public int Groq => Requests.Count(u => !IsLocal(u));
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://groq.test/openai/v1/") };
    }

    private static bool IsLocal(Uri uri) => uri.Host == "stt";

    private static bool IsUzbekPass(string body) => System.Text.RegularExpressions.Regex.IsMatch(body, "name=language\\s+uz\\s");

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Groq(string text, string language) => Json(HttpStatusCode.OK, new { text, language, duration = 3.4 });

    private static (GroqSpeechToText Sut, FakeTimeProvider Clock) Sut(Handler handler, string url = "http://stt:8000")
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        var factory = new Factory(handler);
        var local = new LocalSttClient(factory, Microsoft.Extensions.Options.Options.Create(new LocalSttOptions { Url = url }), clock, NullLogger<LocalSttClient>.Instance);
        var sut = new GroqSpeechToText(factory, Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test-key" }), NullLogger<GroqSpeechToText>.Instance, local);
        return (sut, clock);
    }

    [Fact]
    public async Task UzbekSpeech_ComesFromTheLocalModel_AndGroqIsNotAskedForTheUzbekPass()
    {
        var handler = new Handler((uri, body) => IsLocal(uri)
            ? Json(HttpStatusCode.OK, new { text = " Ahmad aka besh yuz ellik ming qarz ", language = "uz", duration = 4.2 })
            : IsUzbekPass(body) ? throw new InvalidOperationException("Groq must not get the Uzbek pass") : Groq("Ахмат Ака, 500 елік мін қарсы.", "Kazakh"));

        var transcript = await Sut(handler).Sut.TranscribeAsync(new MemoryStream([1, 2, 3]), "voice.ogg", ["Ahmad aka", "Зулфия опа"]);

        transcript.Text.Should().Be("Ahmad aka besh yuz ellik ming qarz");
        transcript.Duration.Should().Be(TimeSpan.FromSeconds(4.2));
        (handler.Local, handler.Groq).Should().Be((1, 1), "one local Uzbek pass and one Groq auto pass for Russian");

        var call = handler.Requests.Single(IsLocal);
        call.AbsolutePath.Should().Be("/transcribe");
        var query = HttpUtility.ParseQueryString(call.Query);
        query["language"].Should().Be("uz");
        query["name"].Should().Be("voice.ogg");
        query["prompt"].Should().StartWith("Mijozlar: Ahmad aka, Zulfiya Opa. Do'kon nasiya daftari", "the shop's customer names steer the local model too");
    }

    [Fact]
    public async Task RussianSpeech_StillTakesTheGroqAutoPass()
    {
        // The Uzbek-only model writes Russian speech as Latin gibberish without Uzbek words.
        var handler = new Handler((uri, _) => IsLocal(uri)
            ? Json(HttpStatusCode.OK, new { text = "nadir pyatsot pyatdesyat tisich sum dolg", language = "uz", duration = 3.0 })
            : Groq("Надыр. Пятьсот пятьдесят тысяч сум, долг.", "Russian"));

        (await Sut(handler).Sut.TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Надыр. Пятьсот пятьдесят тысяч сум, долг.");
    }

    [Fact]
    public async Task LocalModelDown_GroqTakesTheUzbekPass_AndTheServiceIsLeftAloneForAMinute()
    {
        var handler = new Handler((uri, body) => IsLocal(uri)
            ? Json(HttpStatusCode.ServiceUnavailable, new { error = "model is loading" })
            : IsUzbekPass(body) ? Groq("Ahmad aka, besh yuz ellik ming qarz.", "Uzbek") : Groq("Ахмат Ака", "Kazakh"));
        var (sut, clock) = Sut(handler);

        (await sut.TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Ahmad aka, besh yuz ellik ming qarz.");
        (handler.Local, handler.Groq).Should().Be((1, 2));

        // Next message within the cooldown: the broken service is not even tried.
        await sut.TranscribeAsync(new MemoryStream([1]), "voice.ogg");
        (handler.Local, handler.Groq).Should().Be((1, 4));

        clock.Advance(TimeSpan.FromSeconds(61));
        await sut.TranscribeAsync(new MemoryStream([1]), "voice.ogg");
        handler.Local.Should().Be(2, "after the cooldown it gets another chance");
    }

    [Fact]
    public async Task NotConfigured_BehavesExactlyLikeGroqAlone()
    {
        var handler = new Handler((uri, body) => IsLocal(uri)
            ? throw new InvalidOperationException("local STT is switched off")
            : IsUzbekPass(body) ? Groq("Olim ikki yuz ming to'ladi", "Uzbek") : Groq("Олим", "Kazakh"));

        (await Sut(handler, url: "").Sut.TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Olim ikki yuz ming to'ladi");
        (handler.Local, handler.Groq).Should().Be((0, 2));
    }
}
