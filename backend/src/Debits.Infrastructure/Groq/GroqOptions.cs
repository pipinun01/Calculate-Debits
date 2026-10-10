namespace Debits.Infrastructure.Groq;

public sealed class GroqOptions
{
    public const string Section = "Groq";
    public const string HttpClientName = "groq";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/";
    public string SttModel { get; set; } = "whisper-large-v3";
    public string LlmModel { get; set; } = "openai/gpt-oss-120b";
    public string LlmFallbackModel { get; set; } = "openai/gpt-oss-20b";

    /// <summary>Forces one Whisper language ("uz" / "ru"). Empty = two passes: auto-detect, then Uzbek unless the result is clean Russian.</summary>
    public string SttLanguage { get; set; } = string.Empty;

    /// <summary>"low" keeps reasoning models fast; empty omits the parameter for models that reject it.</summary>
    public string ReasoningEffort { get; set; } = "low";

    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
