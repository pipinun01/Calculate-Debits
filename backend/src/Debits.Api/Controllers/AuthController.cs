using Debits.Api.Auth;
using Debits.Api.Contracts;
using Debits.Application.Shops;
using Debits.Application.Users;
using Debits.Bot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Debits.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserService users,
    ShopService shops,
    JwtTokenService jwt,
    IOptions<BotOptions> bot,
    TimeProvider clock,
    IHostEnvironment environment,
    ILogger<AuthController> logger) : ControllerBase
{
    public const string DevTelegramIdHeader = "X-Dev-Telegram-Id";
    public const string DevFirstNameHeader = "X-Dev-First-Name";

    /// <summary>Exchanges Telegram Mini App initData for a JWT. In Development, <c>X-Dev-Telegram-Id</c> bypasses the HMAC check for browser work.</summary>
    [HttpPost("telegram")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Telegram([FromBody] TelegramAuthRequest? request, CancellationToken ct)
    {
        TelegramIdentity identity;
        if (environment.IsDevelopment() && Request.Headers.TryGetValue(DevTelegramIdHeader, out var dev) && long.TryParse(dev.ToString(), out var devId))
        {
            var name = Request.Headers.TryGetValue(DevFirstNameHeader, out var n) && !string.IsNullOrWhiteSpace(n) ? n.ToString() : "Dev";
            identity = new TelegramIdentity(devId, name, null, null);
            logger.LogWarning("Dev auth bypass for Telegram id {TelegramId}", devId);
        }
        else if (TelegramInitDataValidator.TryValidate(request?.InitData ?? string.Empty, bot.Value.Token, clock.GetUtcNow(), out var data, out var error))
        {
            identity = data!.User;
        }
        else
        {
            return Unauthorized(ApiProblems.Create(StatusCodes.Status401Unauthorized, error, "Telegram init data is invalid or expired"));
        }

        var user = await users.UpsertAsync(identity, ct);
        var (token, expiresAt) = jwt.Issue(user);
        var memberships = await shops.ListForUserAsync(user.Id, ct);
        return new AuthResponse(token, expiresAt, MeDto.From(user, memberships));
    }
}
