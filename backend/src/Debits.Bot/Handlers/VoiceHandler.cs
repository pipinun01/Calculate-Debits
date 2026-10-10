using Debits.Application.Abstractions;
using Debits.Application.Entries;
using Debits.Bot.Dispatch;
using Debits.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Debits.Bot.Handlers;

/// <summary>Voice message → download → speech-to-text → the same entry flow as typed text.</summary>
public sealed class VoiceHandler(ISpeechToText stt, EntryHandler entries, EntryDraftService drafts, IOptions<BotOptions> options, ILogger<VoiceHandler> logger)
{
    public async Task HandleAsync(BotContext ctx, string fileId, int durationSeconds, string fileName, CancellationToken ct)
    {
        if (!stt.IsConfigured)
        {
            await ctx.ReplyAsync(ctx.T("voice.unavailable"), ct: ct);
            return;
        }

        var max = options.Value.MaxVoiceSeconds;
        if (durationSeconds > max)
        {
            await ctx.ReplyAsync(ctx.T("voice.too_long", max), ct: ct);
            return;
        }

        string text;
        try
        {
            await ctx.Bot.SendChatAction(ctx.ChatId, ChatAction.Typing, cancellationToken: ct);
            using var audio = new MemoryStream();
            await ctx.Bot.GetInfoAndDownloadFile(fileId, audio, ct);
            audio.Position = 0;
            var names = await drafts.RecentCustomerNamesAsync(ct: ct);
            var transcript = await stt.TranscribeAsync(audio, fileName, names, ct);
            text = transcript.Text.Trim();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Voice message of user {UserId} could not be transcribed", ctx.User.Id);
            await ctx.ReplyAsync(ctx.T("voice.failed"), ct: ct);
            return;
        }

        if (text.Length == 0)
        {
            await ctx.ReplyAsync(ctx.T("voice.empty"), ct: ct);
            return;
        }

        if (!await entries.StartAsync(ctx, text, EntrySource.Voice, fileId, ct))
        {
            // Heard something, but neither an amount nor a known customer: show what was heard so the user can retry.
            await ctx.ReplyAsync(ctx.T("entry.heard", BotContext.Esc(text)) + "\n\n" + ctx.T("fallback.text"), ct: ct);
        }
    }
}
