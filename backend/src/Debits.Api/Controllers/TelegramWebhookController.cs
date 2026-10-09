using System.Text.Json;
using Debits.Bot;
using Debits.Bot.Dispatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Debits.Api.Controllers;

[ApiController]
public sealed class TelegramWebhookController(UpdateDispatcher dispatcher, IOptions<BotOptions> options, ILogger<TelegramWebhookController> logger) : ControllerBase
{
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    [HttpPost("/tg/webhook")]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (!o.IsConfigured || !o.UseWebhook)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(o.WebhookSecret) || Request.Headers[SecretHeader] != o.WebhookSecret)
        {
            return Unauthorized();
        }

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(Request.Body, JsonBotAPI.Options, cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Malformed webhook payload");
            return BadRequest();
        }

        if (update is null)
        {
            return BadRequest();
        }

        await dispatcher.DispatchAsync(update, cancellationToken);
        return Ok();
    }
}
