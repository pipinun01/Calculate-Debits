using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Application.Customers;
using Debits.Application.Tenancy;
using Debits.Domain.Customers;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Entries;

/// <summary>What a phrase means in the current shop: parsed fields with defaults applied plus the customer lookup.</summary>
public sealed record EntryDraft(
    string Text,
    TransactionType Type,
    long? Amount,
    DateOnly? DueDate,
    bool DueDateIsDefault,
    string? Comment,
    string? CustomerName,
    CustomerResolution Customer,
    double Confidence,
    string Parser,
    IReadOnlyList<string> Warnings);

/// <summary>Text → <see cref="EntryDraft"/>. Shared by the bot (voice / text) and <c>POST /api/entries/parse</c>; writes nothing.</summary>
public sealed class EntryDraftService(IEntryParser parser, CustomerService customers, IDebitsDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<EntryDraft> BuildAsync(string text, CancellationToken ct = default)
    {
        var shop = await db.Shops.Where(s => s.Id == tenant.ShopId).Select(s => new { s.Timezone, s.DefaultDueDays, s.Language }).SingleAsync(ct);
        var today = LocalTime.Today(clock, shop.Timezone);

        var parsed = await parser.ParseAsync(text, today, shop.Language, ct);
        var type = parsed.Intent ?? TransactionType.Debt;

        DateOnly? due = null;
        var dueIsDefault = false;
        if (type == TransactionType.Debt)
        {
            // A date in the past is a misheard phrase, not a real term.
            due = parsed.DueDate is { } d && d >= today ? d : null;
            dueIsDefault = due is null;
            due ??= today.AddDays(shop.DefaultDueDays);
        }

        CustomerResolution resolution = string.IsNullOrWhiteSpace(parsed.CustomerName)
            ? new CustomerResolution.None()
            : await customers.ResolveAsync(parsed.CustomerName, cancellationToken: ct);

        return new EntryDraft(text, type, parsed.AmountUzs, due, dueIsDefault, parsed.Comment, parsed.CustomerName, resolution, parsed.Confidence, parsed.Parser, parsed.Warnings);
    }

    /// <summary>Names of the customers the shop dealt with most recently: the spelling hint for speech recognition.</summary>
    public async Task<IReadOnlyList<string>> RecentCustomerNamesAsync(int limit = 30, CancellationToken ct = default)
    {
        return await db.Customers
            .Where(c => !c.IsArchived)
            .OrderByDescending(c => c.Transactions.Max(t => (DateTime?)t.CreatedAt) ?? c.CreatedAt)
            .Take(limit)
            .Select(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<DateOnly> DefaultDueDateAsync(CancellationToken ct = default)
    {
        var shop = await db.Shops.Where(s => s.Id == tenant.ShopId).Select(s => new { s.Timezone, s.DefaultDueDays }).SingleAsync(ct);
        return LocalTime.Today(clock, shop.Timezone).AddDays(shop.DefaultDueDays);
    }

    public async Task<DateOnly> TodayAsync(CancellationToken ct = default)
    {
        var tz = await db.Shops.Where(s => s.Id == tenant.ShopId).Select(s => s.Timezone).SingleAsync(ct);
        return LocalTime.Today(clock, tz);
    }
}
