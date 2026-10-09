using Debits.Bot.Localization;
using Debits.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Debits.Bot.Hosting;

/// <summary>On startup: getMe, bot commands in three languages, Mini App menu button, webhook or its removal.</summary>
public sealed class BotSetupHostedService(
    IServiceProvider services,
    BotIdentity identity,
    BotStrings strings,
    IOptions<BotOptions> options,
    IOptions<MiniAppOptions> miniApp,
    ILogger<BotSetupHostedService> logger) : IHostedService
{
    public static readonly UpdateType[] AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery];

    private static readonly string[] Commands = ["start", "menu", "shops", "invite", "language", "help", "cancel"];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (!o.IsConfigured)
        {
            logger.LogWarning("Bot token is not configured; Telegram bot is disabled");
            return;
        }

        var bot = services.GetRequiredService<ITelegramBotClient>();
        var me = await bot.GetMe(cancellationToken);
        identity.Username = me.Username ?? o.Username;
        logger.LogInformation("Telegram bot @{Username} ({Id}), mode {Mode}", identity.Username, me.Id, o.UseWebhook ? "webhook" : "polling");

        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            var commands = Commands.Select(c => new BotCommand { Command = c, Description = strings.Get(language, "cmd." + c) });
            // Telegram has no uz-Cyrl code: uz-Latn is the default for everyone, ru for Russian clients.
            var code = language switch { AppLanguage.Ru => "ru", AppLanguage.UzLatn => "uz", _ => null };
            if (language == AppLanguage.UzCyrl)
            {
                continue;
            }

            await bot.SetMyCommands(commands, languageCode: code, cancellationToken: cancellationToken);
            if (language == AppLanguage.UzLatn)
            {
                await bot.SetMyCommands(commands, cancellationToken: cancellationToken);
            }
        }

        if (miniApp.Value.IsConfigured)
        {
            await bot.SetChatMenuButton(menuButton: new MenuButtonWebApp
            {
                Text = strings.Get(AppLanguage.UzLatn, "menu.button"),
                WebApp = new WebAppInfo { Url = miniApp.Value.Url },
            }, cancellationToken: cancellationToken);
        }

        if (o.UseWebhook)
        {
            var url = o.PublicUrl.TrimEnd('/') + BotOptions.WebhookPath;
            await bot.SetWebhook(url, secretToken: o.WebhookSecret, allowedUpdates: AllowedUpdates, cancellationToken: cancellationToken);
            logger.LogInformation("Webhook set to {Url}", url);
        }
        else
        {
            await bot.DeleteWebhook(cancellationToken: cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
