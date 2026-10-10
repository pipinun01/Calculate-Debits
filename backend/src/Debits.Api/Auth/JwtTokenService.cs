using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Debits.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Debits.Api.Auth;

/// <summary>HS256 bearer tokens for the Mini App. <c>sub</c> = user id, <c>tg</c> = Telegram user id.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public const string TelegramIdClaim = "tg";

    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public static SymmetricSecurityKey Key(string secret) => new(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddHours(_options.LifetimeHours);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(TelegramIdClaim, user.TelegramUserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.FirstName),
            ]),
            SigningCredentials = new SigningCredentials(Key(_options.Secret), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
}
