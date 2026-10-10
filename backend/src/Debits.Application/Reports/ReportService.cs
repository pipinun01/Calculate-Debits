using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Application.Tenancy;
using Debits.Domain;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Reports;

public sealed record FlowPoint(DateOnly From, DateOnly To, long Debts, long Payments);

public sealed record TopDebtor(Guid CustomerId, string Name, string? Tag, string? Phone, long Balance, long Overdue, DateOnly? NextDueDate);

public sealed record ReportSummary(
    DateOnly From,
    DateOnly To,
    long Collected,
    long CollectedPrevious,
    long NewDebts,
    long NewDebtsPrevious,
    int PaymentsCount,
    int DebtsCount,
    /// <summary>Collected ÷ new debts for the period, 0 when nothing was lent.</summary>
    double CollectionRate,
    long OpenTotal,
    long OverdueTotal,
    IReadOnlyList<FlowPoint> Flow,
    IReadOnlyList<TopDebtor> TopDebtors);

public sealed record CollectionPoint(DateOnly Date, long Payments, int PaymentsCount, long Debts, int DebtsCount);

public sealed class ReportService(IDebitsDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public const int MaxRangeDays = 366 * 2;
    public const int TopDebtorsDefault = 5;

    public async Task<ReportSummary> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var shop = await ShopAsync(ct);
        var today = LocalTime.Today(clock, shop.Timezone);
        var (f, t) = Range(from, to, today);
        var days = t.DayNumber - f.DayNumber + 1;
        var (pf, pt) = (f.AddDays(-days), f.AddDays(-1));

        var current = await LoadAsync(f, t, shop.Timezone, ct);
        var previous = await LoadAsync(pf, pt, shop.Timezone, ct);

        var collected = current.Where(x => x.Type == TransactionType.Payment).Sum(x => x.Amount);
        var newDebts = current.Where(x => x.Type == TransactionType.Debt).Sum(x => x.Amount);

        var open = await db.OpenDebts.Select(d => new { d.Remaining, d.DueDate }).ToListAsync(ct);

        return new ReportSummary(
            f, t,
            collected,
            previous.Where(x => x.Type == TransactionType.Payment).Sum(x => x.Amount),
            newDebts,
            previous.Where(x => x.Type == TransactionType.Debt).Sum(x => x.Amount),
            current.Count(x => x.Type == TransactionType.Payment),
            current.Count(x => x.Type == TransactionType.Debt),
            newDebts == 0 ? 0 : Math.Round((double)collected / newDebts, 4),
            open.Sum(d => d.Remaining),
            open.Where(d => d.DueDate < today).Sum(d => d.Remaining),
            Buckets(f, t, current),
            await TopDebtorsAsync(TopDebtorsDefault, today, ct));
    }

    public async Task<IReadOnlyList<TopDebtor>> TopDebtorsAsync(int limit, CancellationToken ct = default)
    {
        var shop = await ShopAsync(ct);
        return await TopDebtorsAsync(limit, LocalTime.Today(clock, shop.Timezone), ct);
    }

    public async Task<IReadOnlyList<CollectionPoint>> CollectionsAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var shop = await ShopAsync(ct);
        var today = LocalTime.Today(clock, shop.Timezone);
        var (f, t) = Range(from, to, today);
        var rows = await LoadAsync(f, t, shop.Timezone, ct);

        return rows
            .GroupBy(r => r.LocalDate)
            .OrderBy(g => g.Key)
            .Select(g => new CollectionPoint(
                g.Key,
                g.Where(x => x.Type == TransactionType.Payment).Sum(x => x.Amount),
                g.Count(x => x.Type == TransactionType.Payment),
                g.Where(x => x.Type == TransactionType.Debt).Sum(x => x.Amount),
                g.Count(x => x.Type == TransactionType.Debt)))
            .ToList();
    }

    private async Task<IReadOnlyList<TopDebtor>> TopDebtorsAsync(int limit, DateOnly today, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        return await db.Customers
            .Where(c => !c.IsArchived)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Tag,
                c.Phone,
                Balance = db.CustomerBalances.Where(b => b.CustomerId == c.Id).Select(b => b.Balance).FirstOrDefault(),
                Overdue = db.OpenDebts.Where(d => d.CustomerId == c.Id && d.DueDate < today).Sum(d => d.Remaining),
                NextDueDate = db.OpenDebts.Where(d => d.CustomerId == c.Id).Min(d => (DateOnly?)d.DueDate),
            })
            .Where(x => x.Balance > 0)
            .OrderByDescending(x => x.Balance)
            .ThenBy(x => x.Name)
            .Take(limit)
            .Select(x => new TopDebtor(x.Id, x.Name, x.Tag, x.Phone, x.Balance, x.Overdue, x.NextDueDate))
            .ToListAsync(ct);
    }

    private sealed record Row(TransactionType Type, long Amount, DateOnly LocalDate);

    private async Task<List<Row>> LoadAsync(DateOnly from, DateOnly to, string timezone, CancellationToken ct)
    {
        var (fromUtc, toUtc) = LocalTime.UtcRange(from, to, timezone);
        var rows = await db.Transactions
            .Where(t => t.CreatedAt >= fromUtc && t.CreatedAt < toUtc)
            .Select(t => new { t.Type, t.Amount, t.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => new Row(r.Type, r.Amount, LocalTime.ToLocalDate(r.CreatedAt, timezone))).ToList();
    }

    /// <summary>Weekly buckets for ranges up to ~3 months, monthly beyond.</summary>
    private static List<FlowPoint> Buckets(DateOnly from, DateOnly to, List<Row> rows)
    {
        var points = new List<FlowPoint>();
        var monthly = to.DayNumber - from.DayNumber > 92;
        var start = from;
        while (start <= to)
        {
            var end = monthly ? new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1) : start.AddDays(6);
            if (end > to)
            {
                end = to;
            }

            var inBucket = rows.Where(r => r.LocalDate >= start && r.LocalDate <= end).ToList();
            points.Add(new FlowPoint(start, end,
                inBucket.Where(r => r.Type == TransactionType.Debt).Sum(r => r.Amount),
                inBucket.Where(r => r.Type == TransactionType.Payment).Sum(r => r.Amount)));
            start = end.AddDays(1);
        }

        return points;
    }

    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is null && to is null)
        {
            return LocalTime.Month(today);
        }

        var f = from ?? LocalTime.Month(to!.Value).From;
        var t = to ?? today;
        if (t < f)
        {
            throw new DomainException("report.range_invalid", "'to' must not be before 'from'");
        }

        if (t.DayNumber - f.DayNumber > MaxRangeDays)
        {
            throw new DomainException("report.range_too_long", $"Range must be at most {MaxRangeDays} days");
        }

        return (f, t);
    }

    private async Task<Shop> ShopAsync(CancellationToken ct) => await db.Shops.SingleAsync(s => s.Id == tenant.ShopId, ct);
}
