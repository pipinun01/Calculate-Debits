using Debits.Application.Audit;
using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Application.Reports;
using Debits.Application.Tenancy;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Export;

/// <param name="LocalTime">Entry time in the shop's zone.</param>
/// <param name="Remaining">For debts: what is still unpaid. Null for payments.</param>
/// <param name="Balance">Customer balance right after this entry (customer ledger only).</param>
public sealed record ExportRow(
    DateTime LocalTime,
    string CustomerName,
    TransactionType Type,
    long Amount,
    DateOnly? DueDate,
    long? Remaining,
    string? Comment,
    string CreatedByName,
    long? Balance);

public sealed record CustomerLedgerExport(
    string ShopName,
    string CustomerName,
    string? CustomerPhone,
    long Balance,
    DateTime GeneratedLocal,
    IReadOnlyList<ExportRow> Rows);

public sealed record ExportDebtor(string Name, string? Phone, long Balance, long Overdue, DateOnly? NextDueDate);

public sealed record ShopExport(
    string ShopName,
    DateTime GeneratedLocal,
    ReportSummary Summary,
    IReadOnlyList<ExportDebtor> Debtors,
    IReadOnlyList<ExportRow> Transactions);

/// <summary>Reads everything an export file needs in the current shop and leaves an audit trail of who took the data out.</summary>
public sealed class ExportService(IDebitsDbContext db, ITenantContext tenant, TimeProvider clock, ReportService reports, AuditWriter audit)
{
    /// <summary>A notebook page per customer rarely exceeds a few hundred lines; this only guards the worksheet against a runaway shop.</summary>
    public const int MaxRows = 20_000;

    public async Task<CustomerLedgerExport> CustomerLedgerAsync(Guid customerId, CancellationToken ct = default)
    {
        var shop = await ShopAsync(ct);
        var customer = await db.Customers.Where(c => c.Id == customerId).Select(c => new { c.Name, c.Tag, c.Phone }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("customer.not_found", "Customer not found");
        var name = DisplayName(customer.Name, customer.Tag);

        var rows = await Rows(db.Transactions.Where(t => t.CustomerId == customerId), ct);
        var balance = 0L;
        var ledger = rows.Select(r =>
        {
            balance += r.Type == TransactionType.Debt ? r.Amount : -r.Amount;
            return new ExportRow(Local(r.CreatedAt, shop.Timezone), name, r.Type, r.Amount, r.DueDate, r.Remaining, r.Comment, r.CreatedByName, balance);
        }).ToList();

        audit.Write("export.customer", "customer", customerId, new { rows = ledger.Count });
        await db.SaveChangesAsync(ct);
        return new CustomerLedgerExport(shop.Name, name, customer.Phone, balance, Local(clock.GetUtcNow().UtcDateTime, shop.Timezone), ledger);
    }

    public async Task<ShopExport> ShopAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var shop = await ShopAsync(ct);
        var summary = await reports.SummaryAsync(from, to, ct);
        var today = LocalTime.Today(clock, shop.Timezone);

        var debtors = await db.Customers
            .Select(c => new
            {
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
            .Take(MaxRows)
            .ToListAsync(ct);

        var (fromUtc, toUtc) = LocalTime.UtcRange(summary.From, summary.To, shop.Timezone);
        var rows = await Rows(db.Transactions.Where(t => t.CreatedAt >= fromUtc && t.CreatedAt < toUtc), ct);

        audit.Write("export.shop", "shop", shop.Id, new { summary.From, summary.To, rows = rows.Count });
        await db.SaveChangesAsync(ct);
        return new ShopExport(
            shop.Name,
            Local(clock.GetUtcNow().UtcDateTime, shop.Timezone),
            summary,
            debtors.Select(d => new ExportDebtor(DisplayName(d.Name, d.Tag), d.Phone, d.Balance, d.Overdue, d.NextDueDate)).ToList(),
            rows.Select(r => new ExportRow(Local(r.CreatedAt, shop.Timezone), DisplayName(r.CustomerName, r.CustomerTag), r.Type, r.Amount, r.DueDate, r.Remaining, r.Comment, r.CreatedByName, null)).ToList());
    }

    private sealed record Raw(DateTime CreatedAt, string CustomerName, string? CustomerTag, TransactionType Type, long Amount, DateOnly? DueDate, long? Remaining, string? Comment, string CreatedByName);

    /// <summary>Oldest first, the way a paper notebook reads.</summary>
    private async Task<List<Raw>> Rows(IQueryable<Transaction> source, CancellationToken ct)
    {
        var rows = await source
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Take(MaxRows)
            .Select(t => new
            {
                t.CreatedAt,
                CustomerName = t.Customer.Name,
                CustomerTag = t.Customer.Tag,
                t.Type,
                t.Amount,
                t.DueDate,
                Allocated = db.PaymentAllocations.Where(a => a.DebtId == t.Id).Sum(a => (long?)a.Amount) ?? 0,
                t.Comment,
                CreatedByName = t.CreatedBy.User.FirstName,
            })
            .ToListAsync(ct);
        return rows.Select(r => new Raw(r.CreatedAt, r.CustomerName, r.CustomerTag, r.Type, r.Amount, r.DueDate,
            r.Type == TransactionType.Debt ? r.Amount - r.Allocated : null, r.Comment, r.CreatedByName)).ToList();
    }

    private static string DisplayName(string name, string? tag) => string.IsNullOrWhiteSpace(tag) ? name : $"{name} ({tag})";

    private static DateTime Local(DateTime utc, string timezone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), LocalTime.Zone(timezone));

    private async Task<Shop> ShopAsync(CancellationToken ct) => await db.Shops.AsNoTracking().SingleAsync(s => s.Id == tenant.ShopId, ct);
}
