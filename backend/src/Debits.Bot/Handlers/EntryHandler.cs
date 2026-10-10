using System.Globalization;
using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Application.Customers;
using Debits.Application.Entries;
using Debits.Application.Ledger;
using Debits.Application.Shops;
using Debits.Bot.Conversation;
using Debits.Bot.Dispatch;
using Debits.Bot.Entries;
using Debits.Domain;
using Debits.Domain.Customers;
using Debits.Domain.Entities;
using Debits.Domain.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace Debits.Bot.Handlers;

/// <summary>
/// Phrase → confirmation card → ledger. Nothing is written to the ledger (and no customer is created) until ✅.
/// The card lives in <c>pending_entries</c>; typed corrections use <c>conversation_states</c> (<c>entry:*</c>).
/// </summary>
public sealed class EntryHandler(
    IDebitsDbContext db,
    EntryDraftService drafts,
    CustomerService customers,
    CustomerDirectoryService directory,
    LedgerService ledger,
    ShopService shops,
    ConversationStore conversation,
    TimeProvider clock,
    IOptions<BotOptions> options,
    ILogger<EntryHandler> logger)
{
    public const string StatePrefix = "entry:";
    public const string StateAmount = StatePrefix + "amount";
    public const string StateDue = StatePrefix + "due";
    public const string StateCustomer = StatePrefix + "customer";
    public const string StateTag = StatePrefix + "tag";

    private static readonly string[] DateFormats = ["d.M.yyyy", "d.M.yy", "d.M", "d/M/yyyy", "d/M", "yyyy-M-d", "d-M-yyyy"];

    /// <summary>New entry from a transcript or a typed phrase. Returns false when the text does not look like an entry.</summary>
    public async Task<bool> StartAsync(BotContext ctx, string text, EntrySource source, string? voiceFileId, CancellationToken ct)
    {
        var membership = ctx.Membership!;
        await conversation.ClearAsync(ctx.User.Id, ct);

        var draft = await drafts.BuildAsync(text, ct);
        if (draft.Amount is null && draft.Customer is CustomerResolution.None)
        {
            return false;
        }

        var payload = new PendingPayload
        {
            ShopName = membership.ShopName,
            Type = draft.Type,
            Amount = draft.Amount,
            DueDate = draft.DueDate,
            Comment = draft.Comment,
            SpokenName = Limit(draft.CustomerName, CustomerDirectoryService.MaxNameLength),
            Transcript = Limit(text.Trim(), 2000)!,
            Source = source,
            VoiceFileId = voiceFileId,
            Parser = draft.Parser,
            Warnings = draft.Warnings.ToList(),
        };
        ApplyResolution(payload, draft.Customer);

        var now = clock.GetUtcNow().UtcDateTime;
        var entry = new PendingEntry
        {
            ShopId = membership.ShopId,
            UserId = ctx.User.Id,
            ChatId = ctx.ChatId,
            Payload = payload.Serialize(),
            ExpiresAt = now.AddMinutes(options.Value.PendingTtlMinutes),
        };
        db.PendingEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        await ShowAsync(ctx, entry, payload, ct, forceNew: true);
        return true;
    }

    /// <summary>Typed answer to "write the amount / term / name / tag". False when the user is not in such a step.</summary>
    public async Task<bool> HandleStateTextAsync(BotContext ctx, string text, CancellationToken ct)
    {
        var current = await conversation.GetAsync<EntryInputState>(ctx.User.Id, StatePrefix, ct);
        if (current is null)
        {
            return false;
        }

        var (state, data) = current.Value;
        var loaded = await LoadAsync(ctx, data.PendingId, ct);
        if (loaded is null || !IsOpen(loaded.Value.Entry))
        {
            await conversation.ClearAsync(ctx.User.Id, ct);
            return false;
        }

        var (entry, payload) = loaded.Value;
        text = text.Trim();
        switch (state)
        {
            case StateAmount:
                var amount = UzNumberParser.ParseAmount(text) ?? 0;
                if (amount is <= 0 or > LedgerService.MaxAmount)
                {
                    await ctx.ReplyAsync(ctx.T("entry.amount_invalid"), ct: ct);
                    return true;
                }

                payload.Amount = amount;
                payload.Warnings.RemoveAll(w => w.StartsWith("amount_", StringComparison.Ordinal));
                if (amount < RegexEntryParser.SmallAmount)
                {
                    payload.Warnings.Add(RegexEntryParser.WarnAmountSmall);
                }

                break;

            case StateDue:
                var today = await drafts.TodayAsync(ct);
                if (ParseDue(text, today) is not { } due || due < today)
                {
                    await ctx.ReplyAsync(ctx.T("entry.due_invalid"), ct: ct);
                    return true;
                }

                payload.DueDate = due;
                break;

            case StateCustomer:
                if (text.Length is 0 or > CustomerDirectoryService.MaxNameLength)
                {
                    await ctx.ReplyAsync(ctx.T("entry.ask_customer"), ct: ct);
                    return true;
                }

                payload.SpokenName = text;
                ApplyResolution(payload, await customers.ResolveAsync(text, cancellationToken: ct));
                break;

            case StateTag:
                if (text.Length is 0 or > CustomerDirectoryService.MaxTagLength)
                {
                    await ctx.ReplyAsync(ctx.T("entry.ask_tag", BotContext.Esc(payload.SpokenName)), EntryCard.SkipTag(ctx, entry.Id), ct);
                    return true;
                }

                payload.NewTag = text;
                payload.CustomerDisplay = $"{payload.SpokenName} ({text})";
                break;

            default:
                await conversation.ClearAsync(ctx.User.Id, ct);
                return false;
        }

        await conversation.ClearAsync(ctx.User.Id, ct);
        await ShowAsync(ctx, entry, payload, ct, forceNew: true);
        return true;
    }

    /// <summary>Callback <c>pe:{id}:{action}[:{arg}]</c>.</summary>
    public async Task HandleCallbackAsync(BotContext ctx, string data, CancellationToken ct)
    {
        var parts = data.Split(':');
        if (parts.Length < 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
            return;
        }

        var action = parts[2];
        var arg = parts.Length > 3 ? parts[3] : null;

        var loaded = await LoadAsync(ctx, id, ct);
        if (loaded is null)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.expired"), ct);
            return;
        }

        var (entry, payload) = loaded.Value;
        if (action == "del")
        {
            await DeleteAsync(ctx, entry, payload, ct);
            return;
        }

        if (entry.Status != PendingEntryStatus.Pending)
        {
            await ctx.AnswerCallbackAsync(ctx.T(entry.Status == PendingEntryStatus.Expired ? "entry.expired" : "entry.already_done"), ct);
            return;
        }

        if (!IsOpen(entry))
        {
            entry.Status = PendingEntryStatus.Expired;
            await db.SaveChangesAsync(ct);
            await conversation.ClearAsync(ctx.User.Id, ct);
            await ctx.AnswerCallbackAsync(ctx.T("entry.expired"), ct);
            await ctx.EditOrReplyAsync(EntryCard.Render(ctx, payload, "entry.expired"), ct: ct);
            return;
        }

        switch (action)
        {
            case "ok":
                await ConfirmAsync(ctx, entry, payload, ct);
                return;

            case "x":
                entry.Status = PendingEntryStatus.Cancelled;
                await db.SaveChangesAsync(ct);
                await conversation.ClearAsync(ctx.User.Id, ct);
                await ctx.AnswerCallbackAsync(ct: ct);
                await ctx.EditOrReplyAsync(EntryCard.Render(ctx, payload, "entry.cancelled"), ct: ct);
                return;

            case "back":
                await conversation.ClearAsync(ctx.User.Id, ct);
                break;

            case "type":
                if (payload.Type == TransactionType.Debt)
                {
                    payload.Type = TransactionType.Payment;
                    payload.DueDate = null;
                }
                else
                {
                    payload.Type = TransactionType.Debt;
                    payload.DueDate = await drafts.DefaultDueDateAsync(ct);
                }

                break;

            case "amt":
                await AskAsync(ctx, entry, StateAmount, ctx.T("entry.ask_amount"), EntryCard.Back(ctx, entry.Id), ct);
                return;

            case "cu" when arg is null:
                if (!string.IsNullOrWhiteSpace(payload.SpokenName))
                {
                    var found = await customers.SearchAsync(payload.SpokenName, CustomerMatcher.MaxCandidates, ct);
                    payload.Candidates = found.Select(ToCandidate).ToList();
                }

                await conversation.ClearAsync(ctx.User.Id, ct);
                await SaveAsync(entry, payload, ct);
                await ctx.AnswerCallbackAsync(ct: ct);
                await ctx.EditOrReplyAsync(EntryCard.Render(ctx, payload, "entry.pick_customer"), EntryCard.Customers(ctx, entry.Id, payload), ct);
                return;

            case "cu" when arg == "type":
                await AskAsync(ctx, entry, StateCustomer, ctx.T("entry.ask_customer"), EntryCard.Back(ctx, entry.Id), ct);
                return;

            case "cu" when arg == "new":
                if (string.IsNullOrWhiteSpace(payload.SpokenName))
                {
                    await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
                    return;
                }

                payload.CreateNew = true;
                payload.CustomerId = null;
                payload.NewTag = null;
                payload.CustomerDisplay = payload.SpokenName;
                await SaveAsync(entry, payload, ct);
                await AskAsync(ctx, entry, StateTag, ctx.T("entry.ask_tag", BotContext.Esc(payload.SpokenName)), EntryCard.SkipTag(ctx, entry.Id), ct);
                return;

            case "cu":
                if (!int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= payload.Candidates.Count)
                {
                    await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
                    return;
                }

                Choose(payload, payload.Candidates[index]);
                await conversation.ClearAsync(ctx.User.Id, ct);
                break;

            case "tag" when arg == "skip":
                await conversation.ClearAsync(ctx.User.Id, ct);
                break;

            case "due" when arg is null:
                await ctx.AnswerCallbackAsync(ct: ct);
                await ctx.EditOrReplyAsync(EntryCard.Render(ctx, payload, "entry.pick_due"), EntryCard.Due(ctx, entry.Id), ct);
                return;

            case "due" when arg == "pick":
                await AskAsync(ctx, entry, StateDue, ctx.T("entry.ask_due"), EntryCard.Back(ctx, entry.Id), ct);
                return;

            case "due":
                var today = await drafts.TodayAsync(ct);
                DateOnly? due = arg switch
                {
                    "eom" => DuePhraseParser.Parse("oy oxirigacha", today)?.DueDate,
                    _ when int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is > 0 and <= 366 => today.AddDays(days),
                    _ => null,
                };
                if (due is null || payload.Type != TransactionType.Debt)
                {
                    await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
                    return;
                }

                payload.DueDate = due;
                break;

            default:
                await ctx.AnswerCallbackAsync(ctx.T("callback.expired"), ct);
                return;
        }

        await ctx.AnswerCallbackAsync(ct: ct);
        await ShowAsync(ctx, entry, payload, ct);
    }

    private async Task ConfirmAsync(BotContext ctx, PendingEntry entry, PendingPayload payload, CancellationToken ct)
    {
        if (!payload.HasCustomer)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.need_customer"), ct);
            return;
        }

        if (payload.Amount is not > 0)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.need_amount"), ct);
            return;
        }

        // Atomic claim: of two simultaneous taps only one moves the row out of "pending".
        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = await db.PendingEntries
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(p => p.Id == entry.Id && p.Status == PendingEntryStatus.Pending && p.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PendingEntryStatus.Confirmed), ct);
        if (claimed == 0)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.already_done"), ct);
            return;
        }

        LedgerMutation result;
        try
        {
            result = await db.InTransactionAsync(async ct2 =>
            {
                var customerId = payload.CustomerId ?? await EnsureCustomerAsync(payload, ct2);
                return await ledger.CreateAsync(new CreateTransactionCommand(
                    customerId,
                    payload.Type,
                    payload.Amount.Value,
                    payload.Comment,
                    payload.Type == TransactionType.Debt ? payload.DueDate : null,
                    null,
                    payload.Source,
                    payload.VoiceFileId,
                    payload.Transcript), ct2);
            }, ct);
        }
        catch (Exception ex)
        {
            await db.PendingEntries
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(p => p.Id == entry.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PendingEntryStatus.Pending), CancellationToken.None);
            if (ex is not DomainException domain)
            {
                throw;
            }

            logger.LogWarning("Pending entry {Id} could not be confirmed: {Code} {Message}", entry.Id, domain.Code, domain.Message);
            await ctx.AnswerCallbackAsync(ctx.T("entry.failed"), ct);
            return;
        }

        payload.CustomerId = result.Transaction.CustomerId;
        payload.CreateNew = false;
        payload.TransactionId = result.Transaction.Id;
        payload.DueDate = result.Transaction.DueDate;
        entry.Status = PendingEntryStatus.Confirmed;
        await SaveAsync(entry, payload, ct);
        await conversation.ClearAsync(ctx.User.Id, ct);

        await ctx.AnswerCallbackAsync(ct: ct);
        await ctx.EditOrReplyAsync(EntryCard.Receipt(ctx, payload, ctx.User.FirstName, result.Balance), EntryCard.Delete(ctx, entry.Id), ct);
    }

    private async Task DeleteAsync(BotContext ctx, PendingEntry entry, PendingPayload payload, CancellationToken ct)
    {
        if (entry.Status != PendingEntryStatus.Confirmed || payload.TransactionId is not { } transactionId)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.already_done"), ct);
            return;
        }

        long balance;
        try
        {
            balance = await ledger.DeleteAsync(transactionId, "bot", ct);
        }
        catch (ForbiddenException)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.delete_forbidden"), ct);
            return;
        }
        catch (NotFoundException)
        {
            await ctx.AnswerCallbackAsync(ctx.T("entry.already_done"), ct);
            return;
        }

        payload.TransactionId = null;
        await SaveAsync(entry, payload, ct);
        await ctx.AnswerCallbackAsync(ct: ct);
        await ctx.EditOrReplyAsync(ctx.T("entry.deleted", BotContext.Esc(payload.CustomerDisplay), Money.Format(balance)), ct: ct);
    }

    /// <summary>Creates the new customer, or reuses the one that already has exactly this name + tag.</summary>
    private async Task<Guid> EnsureCustomerAsync(PendingPayload payload, CancellationToken ct)
    {
        var key = SearchKey.Normalize(payload.SpokenName!, payload.NewTag);
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.SearchKey == key, ct);
        if (existing is not null)
        {
            existing.IsArchived = false;
            payload.CustomerDisplay = existing.DisplayName;
            return existing.Id;
        }

        var created = await directory.CreateAsync(new CreateCustomerCommand(payload.SpokenName!, payload.NewTag), ct);
        payload.CustomerDisplay = string.IsNullOrWhiteSpace(created.Tag) ? created.Name : $"{created.Name} ({created.Tag})";
        return created.Id;
    }

    /// <summary>Puts the user into a typed-input step and turns the card into the question.</summary>
    private async Task AskAsync(BotContext ctx, PendingEntry entry, string state, string question, InlineKeyboardMarkup markup, CancellationToken ct)
    {
        await conversation.SetAsync(ctx.User.Id, state, new EntryInputState { PendingId = entry.Id }, ct);
        await ctx.AnswerCallbackAsync(ct: ct);
        await ctx.EditOrReplyAsync(question, markup, ct);
    }

    /// <summary>Card when the customer is known, customer picker otherwise. Edits the card in place for button presses, sends a new one after typed input.</summary>
    private async Task ShowAsync(BotContext ctx, PendingEntry entry, PendingPayload payload, CancellationToken ct, bool forceNew = false)
    {
        var (text, markup) = payload.HasCustomer
            ? (EntryCard.Render(ctx, payload), EntryCard.Main(ctx, entry.Id, payload))
            : (EntryCard.Render(ctx, payload, "entry.pick_customer"), EntryCard.Customers(ctx, entry.Id, payload));

        if (!forceNew && ctx.Callback?.Message is not null)
        {
            await SaveAsync(entry, payload, ct);
            await ctx.EditOrReplyAsync(text, markup, ct);
            return;
        }

        var previous = entry.MessageId;
        var message = await ctx.ReplyAsync(text, markup, ct);
        entry.MessageId = message.Id;
        await SaveAsync(entry, payload, ct);

        if (previous != 0 && previous != message.Id)
        {
            try
            {
                await ctx.Bot.EditMessageReplyMarkup(ctx.ChatId, previous, replyMarkup: null, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not remove the keyboard of the previous card {MessageId}", previous);
            }
        }
    }

    private async Task SaveAsync(PendingEntry entry, PendingPayload payload, CancellationToken ct)
    {
        entry.Payload = payload.Serialize();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Loads the user's own entry and switches the tenant to its shop (the user may have changed the active shop since).</summary>
    private async Task<(PendingEntry Entry, PendingPayload Payload)?> LoadAsync(BotContext ctx, long id, CancellationToken ct)
    {
        var entry = await db.PendingEntries
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .SingleOrDefaultAsync(p => p.Id == id && p.UserId == ctx.User.Id, ct);
        if (entry is null)
        {
            return null;
        }

        if (ctx.Tenant.ShopId != entry.ShopId)
        {
            var membership = (await shops.ListForUserAsync(ctx.User.Id, ct)).FirstOrDefault(m => m.ShopId == entry.ShopId);
            if (membership is null)
            {
                return null;
            }

            ctx.Tenant.Set(membership.ShopId, membership.MemberId, ctx.User.Id, membership.Role, ctx.User.Language);
        }

        return (entry, PendingPayload.Deserialize(entry.Payload));
    }

    private bool IsOpen(PendingEntry entry) => entry.Status == PendingEntryStatus.Pending && entry.ExpiresAt > clock.GetUtcNow().UtcDateTime;

    private static void ApplyResolution(PendingPayload payload, CustomerResolution resolution)
    {
        payload.CreateNew = false;
        payload.NewTag = null;
        switch (resolution)
        {
            case CustomerResolution.Exact exact:
                payload.Candidates = [ToCandidate(exact.Customer)];
                Choose(payload, payload.Candidates[0]);
                break;
            case CustomerResolution.Ambiguous ambiguous:
                payload.Candidates = ambiguous.Candidates.Select(ToCandidate).ToList();
                payload.CustomerId = null;
                payload.CustomerDisplay = null;
                break;
            default:
                payload.Candidates = [];
                payload.CustomerId = null;
                payload.CustomerDisplay = null;
                break;
        }
    }

    private static void Choose(PendingPayload payload, PendingCandidate candidate)
    {
        payload.CustomerId = candidate.Id;
        payload.CustomerDisplay = candidate.Display;
        payload.CreateNew = false;
        payload.NewTag = null;
    }

    private static PendingCandidate ToCandidate(CustomerCandidate c) => new() { Id = c.CustomerId, Name = c.Name, Tag = c.Tag };

    /// <summary>"25.10", "25.10.2026", "2026-10-25" or a phrase ("2 haftaga", "до конца месяца").</summary>
    internal static DateOnly? ParseDue(string text, DateOnly today)
    {
        foreach (var format in DateFormats)
        {
            if (DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                // Day and month only: the nearest such date that is not in the past.
                if (!format.Contains('y'))
                {
                    if (date.Day > DateTime.DaysInMonth(today.Year, date.Month))
                    {
                        return null;
                    }

                    date = new DateOnly(today.Year, date.Month, date.Day);
                    if (date < today)
                    {
                        date = date.AddYears(1);
                    }
                }

                return date;
            }
        }

        return DuePhraseParser.Parse(text, today)?.DueDate;
    }

    private static string? Limit(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
