using System.Globalization;
using System.Text;
using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Application.Export;
using Debits.Application.Reports;
using Debits.Application.Tenancy;
using Debits.Bot.Dispatch;
using Debits.Bot.Localization;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Debits.Bot.Reports;

public sealed record ExportFile(byte[] Content, string FileName, string Caption)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>
/// Reports on request from the Mini App: Excel files (for download or as a document in the bot chat)
/// and the period summary as a chat message. Everything is in the language of the acting user.
/// </summary>
public sealed class ReportSender(
    IServiceProvider services,
    IOptions<BotOptions> options,
    BotStrings strings,
    ExportService export,
    ReportService reports,
    IExcelExporter excel,
    IDebitsDbContext db,
    ITenantContext tenant,
    ILogger<ReportSender> logger)
{
    private const string DateFormat = "dd.MM.yyyy";

    public async Task<ExportFile> CustomerLedgerFileAsync(Guid customerId, CancellationToken ct = default)
    {
        var data = await export.CustomerLedgerAsync(customerId, ct);
        return new ExportFile(
            excel.CustomerLedger(data, T),
            FileName(data.CustomerName, data.GeneratedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            T("export.customer.caption", BotContext.Esc(data.CustomerName), Money.Format(data.Balance)));
    }

    public async Task<ExportFile> ShopFileAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var data = await export.ShopAsync(from, to, ct);
        var (f, t) = (data.Summary.From, data.Summary.To);
        return new ExportFile(
            excel.Shop(data, T),
            FileName(data.ShopName, $"{f:yyyy-MM-dd}_{t:yyyy-MM-dd}"),
            T("export.shop.caption", BotContext.Esc(data.ShopName), f.ToString(DateFormat, CultureInfo.InvariantCulture), t.ToString(DateFormat, CultureInfo.InvariantCulture)));
    }

    public async Task SendCustomerLedgerAsync(Guid customerId, CancellationToken ct = default)
    {
        var chatId = await ChatAsync(ct);
        await SendFileAsync(chatId, await CustomerLedgerFileAsync(customerId, ct), ct);
    }

    public async Task SendShopFileAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var chatId = await ChatAsync(ct);
        await SendFileAsync(chatId, await ShopFileAsync(from, to, ct), ct);
    }

    public async Task SendSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var chatId = await ChatAsync(ct);
        var shopName = await db.Shops.Where(s => s.Id == tenant.ShopId).Select(s => s.Name).SingleAsync(ct);
        var text = SummaryText(shopName, await reports.SummaryAsync(from, to, ct));
        await DeliverAsync(bot => bot.SendMessage(chatId, text, parseMode: ParseMode.Html, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct));
    }

    public string SummaryText(string shopName, ReportSummary s)
    {
        var sb = new StringBuilder();
        sb.Append(T("report.title", BotContext.Esc(shopName), s.From.ToString(DateFormat, CultureInfo.InvariantCulture), s.To.ToString(DateFormat, CultureInfo.InvariantCulture)));
        sb.Append("\n\n").Append(T("report.collected", Money.Format(s.Collected), s.PaymentsCount));
        sb.Append('\n').Append(T("report.new_debts", Money.Format(s.NewDebts), s.DebtsCount));
        sb.Append('\n').Append(T("report.rate", Math.Round(s.CollectionRate * 100)));
        sb.Append("\n\n").Append(T("report.open", Money.Format(s.OpenTotal)));
        sb.Append('\n').Append(T("report.overdue", Money.Format(s.OverdueTotal)));

        if (s.TopDebtors.Count > 0)
        {
            sb.Append("\n\n").Append(T("report.top"));
            var number = 0;
            foreach (var d in s.TopDebtors)
            {
                var name = string.IsNullOrWhiteSpace(d.Tag) ? d.Name : $"{d.Name} ({d.Tag})";
                sb.Append('\n').Append(T("report.top.line", ++number, BotContext.Esc(name), Money.Format(d.Balance)));
            }
        }

        return sb.ToString();
    }

    private Task SendFileAsync(long chatId, ExportFile file, CancellationToken ct) => DeliverAsync(async bot =>
    {
        using var stream = new MemoryStream(file.Content);
        await bot.SendDocument(chatId, InputFile.FromStream(stream, file.FileName), caption: file.Caption, parseMode: ParseMode.Html, cancellationToken: ct);
    });

    private async Task DeliverAsync(Func<ITelegramBotClient, Task> send)
    {
        try
        {
            await send(services.GetRequiredService<ITelegramBotClient>());
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            throw new ConflictException("bot.blocked", "The bot cannot write to this user: open the bot and press Start");
        }
        catch (ApiRequestException ex)
        {
            logger.LogWarning(ex, "Telegram refused a report ({ErrorCode})", ex.ErrorCode);
            throw new ConflictException("bot.send_failed", "Telegram did not accept the message");
        }
    }

    /// <summary>Telegram chat of the acting user (a private chat id equals the user id).</summary>
    private async Task<long> ChatAsync(CancellationToken ct)
    {
        if (!options.Value.IsConfigured)
        {
            throw new ConflictException("bot.not_configured", "The Telegram bot is not configured on this server");
        }

        return await db.Users.Where(u => u.Id == tenant.UserId).Select(u => u.TelegramUserId).SingleAsync(ct);
    }

    private string T(string key) => strings.Get(tenant.Language, key);

    private string T(string key, params object?[] args) => strings.Get(tenant.Language, key, args);

    private static string FileName(string subject, string suffix)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(subject.Select(ch => invalid.Contains(ch) || ch is '"' or '\'' or '«' or '»' ? ' ' : ch).ToArray());
        clean = string.Join('_', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > 40)
        {
            clean = clean[..40];
        }

        return $"Debits_{(clean.Length == 0 ? "export" : clean)}_{suffix}.xlsx";
    }
}
