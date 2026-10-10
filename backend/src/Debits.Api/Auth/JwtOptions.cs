namespace Debits.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public int LifetimeHours { get; set; } = 12;
    public string Issuer { get; set; } = "debits";
    public string Audience { get; set; } = "debits-miniapp";
}
