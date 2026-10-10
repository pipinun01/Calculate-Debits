using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Debits.Application.Abstractions;
using Debits.Application.Entries;
using Debits.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Debits.Infrastructure.Groq;

/// <summary>
/// Groq chat completion in JSON mode: phrase → {intent, customer, amount_uzs, due_in_days, due_date, comment, confidence}.
/// The primary model is tried first; on any failure (429, 5xx, bad JSON) the fallback model gets one attempt.
/// </summary>
public sealed class GroqEntryParser(IHttpClientFactory httpFactory, IOptions<GroqOptions> options, ILogger<GroqEntryParser> logger) : ILlmEntryParser
{
    public const string ParserName = "groq";

    private readonly GroqOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Kept short on purpose: the free tier allows 8000 tokens per minute per model, and every request pays for the whole prompt.</summary>
    public const string SystemPrompt = """
        You read one entry of a shop's debt notebook in Uzbekistan (Uzbek Latin/Cyrillic, Russian or mixed; may contain speech-recognition mistakes) and answer with JSON only:
        {"intent":"debt"|"payment"|null,"customer":string|null,"amount_uzs":integer|null,"due_in_days":integer|null,"due_date":"YYYY-MM-DD"|null,"comment":string|null,"confidence":number}
        - intent: "debt" = took on credit (qarz, nasiya, oldi, долг, взял). "payment" = paid back (to'ladi, berdi, qaytardi, uzdi, оплатил, вернул, отдал). A payment word wins: "qarzini berdi" is a payment.
        - customer: the name as said, same script, with honorific (aka, opa, uka, xola, ака, опа) and any profession/place said with it. Drop case endings: "Ahmad akaga" → "Ahmad aka", "opadan" → "opa"; Russian oblique cases → nominative: "запиши Рустама" → "Рустам", "у Ахмада" → "Ахмад".
        - amount_uzs: full integer in so'm: "550 ming" = 550000, "bir yarim million" = 1500000, "пятьсот тысяч" = 500000. A bare number without ming/тысяч/million ("ellik", "550") is returned as is, never multiplied. Numbers of a term ("3 kunga", "на 10 дней") are not the amount.
        - due_in_days for relative terms ("3 kunga" = 3, "bir haftaga" = 7, "bir oyga" = 30, "ertaga" = 1). due_date only for calendar terms, from the given today ("oy oxirigacha" / "до конца месяца" = last day of this month; "25-chi kuni" = that day, next month if passed). Payments have no term.
        - comment: goods mentioned (un, shakar, мясо …) in the original words, else null.
        - confidence: 0..1 for customer and amount.
        """;

    private static readonly (string User, string Assistant)[] FewShots =
    [
        ("today: 2026-10-09\ntext: Зулфия опага икки юз эллик минг сўм насия, бир ҳафтага, ун билан шакар олди",
            """{"intent":"debt","customer":"Зулфия опа","amount_uzs":250000,"due_in_days":7,"due_date":null,"comment":"ун, шакар","confidence":0.95}"""),
        ("today: 2026-10-09\ntext: Karim stomatolog bir yarim million to'ladi",
            """{"intent":"payment","customer":"Karim stomatolog","amount_uzs":1500000,"due_in_days":null,"due_date":null,"comment":null,"confidence":0.9}"""),
        ("today: 2026-10-09\ntext: запиши Олима соседа 2 миллиона 300 тысяч до конца месяца, за мясо",
            """{"intent":"debt","customer":"Олим сосед","amount_uzs":2300000,"due_in_days":null,"due_date":"2026-10-31","comment":"мясо","confidence":0.9}"""),
        ("today: 2026-10-09\ntext: Sardor ellik sigaret oldi 3 kunga",
            """{"intent":"debt","customer":"Sardor","amount_uzs":50,"due_in_days":3,"due_date":null,"comment":"sigaret","confidence":0.5}"""),
    ];

    public async Task<ParsedEntry> ParseAsync(string text, DateOnly today, AppLanguage language, CancellationToken cancellationToken = default)
    {
        try
        {
            return await CallAsync(_options.LlmModel, text, today, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(_options.LlmFallbackModel) && _options.LlmFallbackModel != _options.LlmModel)
        {
            logger.LogWarning(ex, "Groq {Model} failed; trying {Fallback}", _options.LlmModel, _options.LlmFallbackModel);
            return await CallAsync(_options.LlmFallbackModel, text, today, cancellationToken);
        }
    }

    private async Task<ParsedEntry> CallAsync(string model, string text, DateOnly today, CancellationToken ct)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt } };
        foreach (var (user, assistant) in FewShots)
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = user });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = assistant });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = $"today: {today:yyyy-MM-dd}\ntext: {text}" });

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = 0,
            ["max_completion_tokens"] = 1024,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };
        if (!string.IsNullOrWhiteSpace(_options.ReasoningEffort) && model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
        {
            payload["reasoning_effort"] = _options.ReasoningEffort;
        }

        using var response = await httpFactory.CreateClient(GroqOptions.HttpClientName)
            .PostAsync("chat/completions", JsonContent.Create(payload), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Groq {model} failed with {(int)response.StatusCode}: {GroqSpeechToText.Truncate(body)}", null, response.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new JsonException("Groq returned an empty message");
        return Map(content, today);
    }

    /// <summary>Model JSON → <see cref="ParsedEntry"/>. Tolerant to numbers sent as strings and to out-of-range values.</summary>
    public static ParsedEntry Map(string json, DateOnly today)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected a JSON object");
        }

        TransactionType? intent = Str(root, "intent")?.ToLowerInvariant() switch
        {
            "debt" => TransactionType.Debt,
            "payment" => TransactionType.Payment,
            _ => null,
        };

        var amount = Long(root, "amount_uzs");
        if (amount is <= 0 or > 1_000_000_000_000)
        {
            amount = null;
        }

        DateOnly? due = null;
        if (intent != TransactionType.Payment)
        {
            if (Str(root, "due_date") is { } s && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                due = d;
            }
            else if (Long(root, "due_in_days") is > 0 and <= 3650 and var days)
            {
                due = today.AddDays((int)days);
            }
        }

        var confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out var cv) ? Math.Clamp(cv, 0, 1) : 0.5;
        return new ParsedEntry(intent, Str(root, "customer"), amount, due, Str(root, "comment"), confidence, ParserName, []);
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) && v.GetString() != "null"
            ? v.GetString()!.Trim()
            : null;

    private static long? Long(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var l) => l,
            JsonValueKind.Number when v.TryGetDouble(out var d) => (long)Math.Round(d),
            JsonValueKind.String when long.TryParse(v.GetString()?.Replace(" ", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            _ => null,
        };
    }
}
