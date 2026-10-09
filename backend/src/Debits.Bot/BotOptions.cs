namespace Debits.Bot;

public sealed class BotOptions
{
    public const string Section = "Bot";

    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>"polling" (dev) or "webhook" (prod).</summary>
    public string Mode { get; set; } = "polling";
    public string WebhookSecret { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);
    public bool UseWebhook => string.Equals(Mode, "webhook", StringComparison.OrdinalIgnoreCase);
    public const string WebhookPath = "/tg/webhook";
}

public sealed class MiniAppOptions
{
    public const string Section = "MiniApp";

    public string Url { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>Bot identity learned from <c>getMe</c> at startup; falls back to configuration.</summary>
public sealed class BotIdentity
{
    private string _username = string.Empty;

    public string Username
    {
        get => _username;
        set => _username = value.TrimStart('@');
    }

    public string DeepLink(string payload) => $"https://t.me/{Username}?start={payload}";
}
