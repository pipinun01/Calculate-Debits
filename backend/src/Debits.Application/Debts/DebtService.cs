using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Debts;

public enum DebtStatus
{
    Open,
    Overdue,
    DueSoon,
    OnTime,
}

public sealed record OpenDebtDto(
    Guid DebtId,
    Guid CustomerId,
    string CustomerName,
    string? CustomerTag,
    string? CustomerPhone,
    long Amount,
    long Remaining,
    DateOnly DueDate,
    /// <summary>Days until due; negative when overdue.</summary>
    int DaysLeft,
    string? Comment,
    string CreatedByName,
    DateTime CreatedAt);

public sealed record DebtSummary(
    DateOnly Today,
    long Total,
    long Overdue,
    long DueSoon,
    long OnTime,
    int OverdueCount,
    int DueSoonCount,
    int DebtorsCount,
    int ActiveCustomers);

/// <summary>Open debts of the shop (the "Qarzlar" screen and the balance hero on the customers screen).</summary>
public sealed class DebtService(IDebitsDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public const int DueSoonDays = 3;

    public async Task<DebtSummary> SummaryAsync(CancellationToken ct = default)
    {
        var today = await TodayAsync(ct);
        var soon = today.AddDays(DueSoonDays);

        var rows = await db.OpenDebts.Select(d => new { d.CustomerId, d.Remaining, d.DueDate }).ToListAsync(ct);
        var activeCustomers = await db.Customers.CountAsync(c => !c.IsArchived, ct);

        var overdue = rows.Where(r => r.DueDate < today).ToList();
        var dueSoon = rows.Where(r => r.DueDate >= today && r.DueDate <= soon).ToList();
        var onTime = rows.Where(r => r.DueDate > soon).ToList();

        return new DebtSummary(
            today,
            rows.Sum(r => r.Remaining),
            overdue.Sum(r => r.Remaining),
            dueSoon.Sum(r => r.Remaining),
            onTime.Sum(r => r.Remaining),
            overdue.Select(r => r.CustomerId).Distinct().Count(),
            dueSoon.Select(r => r.CustomerId).Distinct().Count(),
            rows.Select(r => r.CustomerId).Distinct().Count(),
            activeCustomers);
    }

    public async Task<Page<OpenDebtDto>> ListAsync(DebtStatus status, string? cursor, int? limit, CancellationToken ct = default)
    {
        var today = await TodayAsync(ct);
        var soon = today.AddDays(DueSoonDays);

        var q = db.OpenDebts.AsQueryable();
        q = status switch
        {
            DebtStatus.Overdue => q.Where(d => d.DueDate < today),
            DebtStatus.DueSoon => q.Where(d => d.DueDate >= today && d.DueDate <= soon),
            DebtStatus.OnTime => q.Where(d => d.DueDate > soon),
            _ => q,
        };

        var offset = Cursor.Decode(cursor);
        var take = Cursor.ClampLimit(limit);
        var rows = await q
            .OrderBy(d => d.DueDate)
            .ThenBy(d => d.CreatedAt)
            .Skip(offset)
            .Take(take + 1)
            .Select(d => new
            {
                d.DebtId,
                d.CustomerId,
                Customer = db.Customers.Where(c => c.Id == d.CustomerId).Select(c => new { c.Name, c.Tag, c.Phone }).First(),
                d.Amount,
                d.Remaining,
                d.DueDate,
                Comment = db.Transactions.Where(t => t.Id == d.DebtId).Select(t => t.Comment).FirstOrDefault(),
                CreatedByName = db.ShopMembers.Where(m => m.Id == d.CreatedByMemberId).Select(m => m.User.FirstName).FirstOrDefault() ?? "",
                d.CreatedAt,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new OpenDebtDto(
            r.DebtId, r.CustomerId, r.Customer.Name, r.Customer.Tag, r.Customer.Phone, r.Amount, r.Remaining, r.DueDate,
            r.DueDate.DayNumber - today.DayNumber, r.Comment, r.CreatedByName, r.CreatedAt)).ToList();
        return Cursor.Build(items, offset, take);
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tz = await db.Shops.Where(s => s.Id == tenant.ShopId).Select(s => s.Timezone).SingleAsync(ct);
        return LocalTime.Today(clock, tz);
    }
}
