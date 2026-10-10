using System.Net;
using System.Text;
using System.Text.Json;
using Debits.Application.Abstractions;
using Debits.Domain.Entities;
using Debits.Infrastructure.Groq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Debits.Integration.Tests.Groq;

/// <summary>Request shape and response handling of the Groq clients against a stub HTTP handler (no network).</summary>
public class GroqClientTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, string Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body, request.Headers.Authorization?.ToString()));
            return respond(request, body);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://groq.test/openai/v1/") };
            http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");
            return http;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Chat(string content) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    private static IOptions<GroqOptions> Options() => Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test-key", LlmModel = "big", LlmFallbackModel = "small" });

    [Fact]
    public async Task EntryParser_SendsJsonModeRequest_AndMapsTheAnswer()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, Chat("""{"intent":"debt","customer":"Ahmad aka","amount_uzs":550000,"due_in_days":7,"due_date":null,"comment":"un","confidence":0.9}""")));
        var sut = new GroqEntryParser(new Factory(handler), Options(), NullLogger<GroqEntryParser>.Instance);

        var result = await sut.ParseAsync("Ahmad aka 550 ming qarz bir haftaga un", Today, AppLanguage.UzLatn);

        result.Should().BeEquivalentTo(new ParsedEntry(TransactionType.Debt, "Ahmad aka", 550_000, new DateOnly(2026, 10, 16), "un", 0.9, "groq", []));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Path.Should().EndWith("/chat/completions");
        request.Auth.Should().Be("Bearer test-key");
        using var doc = JsonDocument.Parse(request.Body);
        doc.RootElement.GetProperty("model").GetString().Should().Be("big");
        doc.RootElement.GetProperty("temperature").GetDouble().Should().Be(0);
        doc.RootElement.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        var messages = doc.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[messages.GetArrayLength() - 1].GetProperty("content").GetString().Should().Be("today: 2026-10-09\ntext: Ahmad aka 550 ming qarz bir haftaga un");
    }

    [Fact]
    public async Task EntryParser_FallsBackToTheSmallModel_On429()
    {
        var handler = new StubHandler((_, body) => body.Contains("\"model\":\"big\"")
            ? Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limit"}}""")
            : Json(HttpStatusCode.OK, Chat("""{"intent":"payment","customer":"Olim","amount_uzs":"200000","confidence":0.8}""")));
        var sut = new GroqEntryParser(new Factory(handler), Options(), NullLogger<GroqEntryParser>.Instance);

        var result = await sut.ParseAsync("Olim 200 ming to'ladi", Today, AppLanguage.UzLatn);

        handler.Requests.Should().HaveCount(2);
        (result.Intent, result.CustomerName, result.AmountUzs).Should().Be((TransactionType.Payment, "Olim", 200_000L));
    }

    [Fact]
    public async Task EntryParser_Throws_WhenBothModelsFail()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, Chat("not json at all")));
        var sut = new GroqEntryParser(new Factory(handler), Options(), NullLogger<GroqEntryParser>.Instance);

        var act = () => sut.ParseAsync("x", Today, AppLanguage.UzLatn);

        await act.Should().ThrowAsync<JsonException>();
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("""{"intent":"payment","customer":"A","amount_uzs":1000,"due_in_days":7}""", null)]
    [InlineData("""{"intent":"debt","customer":"A","amount_uzs":1000,"due_date":"2026-10-31","due_in_days":7}""", "2026-10-31")]
    [InlineData("""{"intent":"debt","customer":"A","amount_uzs":1000,"due_date":"31.10.2026"}""", null)]
    [InlineData("""{"intent":"debt","customer":"A","amount_uzs":1000,"due_in_days":99999}""", null)]
    public void Map_DueDate(string json, string? expected)
    {
        GroqEntryParser.Map(json, Today).DueDate?.ToString("yyyy-MM-dd").Should().Be(expected);
    }

    [Fact]
    public void Map_IsTolerant_ToNullsStringsAndNonsense()
    {
        var r = GroqEntryParser.Map("""{"intent":"unknown","customer":"null","amount_uzs":-5,"comment":"  ","confidence":7}""", Today);

        r.Should().BeEquivalentTo(new ParsedEntry(null, null, null, null, null, 1, "groq", []));
        GroqEntryParser.Map("""{"amount_uzs":1500000.0}""", Today).AmountUzs.Should().Be(1_500_000);
        var act = () => GroqEntryParser.Map("[1,2]", Today);
        act.Should().Throw<JsonException>();
    }

    private static string Stt(string text, string language) => JsonSerializer.Serialize(new { text, language, duration = 3.4 });

    private static bool IsUzbekPass(string body) => System.Text.RegularExpressions.Regex.IsMatch(body, "name=language\\s+uz\\s");

    private static bool IsRussianPass(string body) => System.Text.RegularExpressions.Regex.IsMatch(body, "name=language\\s+ru\\s");

    private static GroqSpeechToText Sut(StubHandler handler, string sttLanguage = "") => new(
        new Factory(handler),
        Microsoft.Extensions.Options.Options.Create(new GroqOptions { ApiKey = "test-key", SttLanguage = sttLanguage }),
        NullLogger<GroqSpeechToText>.Instance);

    [Fact]
    public async Task SpeechToText_RussianSpeech_TakesTheAutoPass()
    {
        // what the two passes really returned for a Russian recording
        var handler = new StubHandler((_, body) => IsUzbekPass(body)
            ? Json(HttpStatusCode.OK, Stt("Nader 550 tysik sum. Dolg.", "Uzbek"))
            : Json(HttpStatusCode.OK, Stt(" Надыр. Пятьсот пятьдесят тысяч сум, долг. ", "Russian")));

        var transcript = await Sut(handler).TranscribeAsync(new MemoryStream([1, 2, 3]), "voice.ogg", ["Ahmad aka", "Надар", "ahmad AKA"]);

        transcript.Should().Be(new Transcript("Надыр. Пятьсот пятьдесят тысяч сум, долг.", "Russian", TimeSpan.FromSeconds(3.4)));
        handler.Requests.Should().HaveCount(2).And.OnlyContain(r => r.Path.EndsWith("/audio/transcriptions") && r.Auth == "Bearer test-key");
        var uzbek = handler.Requests.Single(r => IsUzbekPass(r.Body)).Body;
        uzbek.Should().Contain("whisper-large-v3").And.Contain("verbose_json").And.Contain("filename=voice.ogg")
            .And.Contain("Mijozlar: Ahmad aka, Nadar. Do'kon nasiya daftari", "names first, Cyrillic ones transliterated, duplicates dropped");
        var auto = handler.Requests.Single(r => !IsUzbekPass(r.Body)).Body;
        auto.Should().Contain("Долговая тетрадь").And.NotContain("name=language").And.NotContain("Ahmad aka", "a name list makes the Russian pass drop the leading name");
    }

    [Theory]
    [InlineData("Ахмат Ака, 500 елік мін қарсы.", "Kazakh")]
    [InlineData("Ахмат-ака, beşyus ellik, min, so'm, qarz.", "Russian")]
    [InlineData("Ахмат Аке. Пятьсот пятьдесят тысяч сум, долг.", "Russian")]
    public async Task SpeechToText_UzbekSpeech_TakesTheUzbekPass_WhateverAutoDetectionSays(string autoText, string autoLanguage)
    {
        var handler = new StubHandler((_, body) => IsUzbekPass(body)
            ? Json(HttpStatusCode.OK, Stt("Ahmad aka, beş yuz ellik ming qarz.", "Uzbek"))
            : Json(HttpStatusCode.OK, Stt(autoText, autoLanguage)));

        var transcript = await Sut(handler).TranscribeAsync(new MemoryStream([1]), "voice.ogg");

        transcript.Text.Should().Be("Ahmad aka, beş yuz ellik ming qarz.");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SpeechToText_NameOnly_FallsToTheUzbekPass()
    {
        var handler = new StubHandler((_, body) => Json(HttpStatusCode.OK, IsUzbekPass(body) ? Stt("Nadar", "Uzbek") : Stt("NADAR", "Polish")));

        (await Sut(handler).TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Nadar");
    }

    [Fact]
    public async Task SpeechToText_OnePassFailing_UsesTheOther_BothFailing_Throws()
    {
        var uzbekDown = new StubHandler((_, body) => IsUzbekPass(body) ? Json(HttpStatusCode.BadRequest, "{}") : Json(HttpStatusCode.OK, Stt("Ахмат Ака, 500 елік мін қарсы.", "Kazakh")));
        (await Sut(uzbekDown).TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Ахмат Ака, 500 елік мін қарсы.");

        var autoDown = new StubHandler((_, body) => IsUzbekPass(body) ? Json(HttpStatusCode.OK, Stt("Nader 550 tysik sum.", "Uzbek")) : Json(HttpStatusCode.BadRequest, "{}"));
        (await Sut(autoDown).TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Nader 550 tysik sum.");

        var allDown = new StubHandler((_, _) => Json(HttpStatusCode.BadRequest, """{"error":{"message":"bad audio"}}"""));
        var act = () => Sut(allDown).TranscribeAsync(new MemoryStream([1]), "voice.ogg");
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        allDown.Requests.Should().HaveCount(2, "4xx is not retried");
    }

    [Fact]
    public async Task SpeechToText_RussianTakenForUkrainian_GetsAThirdPassInRussian()
    {
        var handler = new StubHandler((_, body) => Json(HttpStatusCode.OK,
            IsRussianPass(body) ? Stt("Надыр, пятьсот тысяч, долг.", "Russian")
            : IsUzbekPass(body) ? Stt("Nader 500 tysik dolg.", "Uzbek")
            : Stt("Надир п'ятсот тисяч борг", "Ukrainian")));

        (await Sut(handler).TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Надыр, пятьсот тысяч, долг.");
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task SpeechToText_ConfiguredLanguage_IsOnePass_WithOneRetryOn5xx()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => ++calls == 1 ? Json(HttpStatusCode.ServiceUnavailable, "{}") : Json(HttpStatusCode.OK, Stt("Ahmad aka qarz", "Uzbek")));

        (await Sut(handler, sttLanguage: "uz").TranscribeAsync(new MemoryStream([1]), "voice.ogg")).Text.Should().Be("Ahmad aka qarz");

        handler.Requests.Should().HaveCount(2).And.OnlyContain(r => IsUzbekPass(r.Body));
    }

    [Theory]
    [InlineData("Ahmad aka, beş yuz ellik ming qarz.", true)]
    [InlineData("Nodir, 5 million so'm qaytardi.", true)]
    [InlineData("Зулфия опа 120 минг тўлади", true)]
    [InlineData("Nader 550 tysik sum. Dolg.", false)]
    [InlineData("Nader, 500 sum, addal.", false)]
    [InlineData("Nadar", false)]
    public void SoundsUzbek_SeparatesUzbekFromRussianForcedThroughTheUzbekDecoder(string text, bool expected) =>
        GroqSpeechToText.SoundsUzbek(text).Should().Be(expected);
}
