using Debits.Application.Abstractions;
using Debits.Application.Common;
using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Audit;

/// <param name="Subject">Who or what the action was about: a customer or a staff member name. Null for shop-level actions.</param>
/// <param name="Amount">For ledger entries: the amount as it is now.</param>
public sealed record AuditEventDto(
    long Id,
    string Action,
    string Entity,
    string? MemberName,
    DateTime CreatedAt,
    string? Subject,
    long? Amount,
    TransactionType? TransactionType);

/// <summary>The shop's audit trail for the owner, newest first.</summary>
public sealed class AuditService(IDebitsDbContext db)
{
    public async Task<Page<AuditEventDto>> ListAsync(string? cursor, int? limit, CancellationToken ct = default)
    {
        var offset = Cursor.Decode(cursor);
        var take = Cursor.ClampLimit(limit);
        var events = await db.AuditEvents
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(offset)
            .Take(take + 1)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.Entity,
                a.EntityId,
                a.CreatedAt,
                MemberName = db.ShopMembers.Where(m => m.Id == a.MemberId).Select(m => m.User.FirstName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var transactionIds = Ids(events.Where(e => e.Entity == "transaction").Select(e => e.EntityId));
        var customerIds = Ids(events.Where(e => e.Entity == "customer").Select(e => e.EntityId));
        var memberIds = Ids(events.Where(e => e.Entity == "member").Select(e => e.EntityId));

        // Deleted entries stay in the trail, so the soft-delete filter is lifted (the tenant filter stays).
        var transactions = await db.Transactions
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(t => transactionIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Type, t.Amount, Customer = t.Customer.Name, t.Customer.Tag })
            .ToDictionaryAsync(t => t.Id, ct);
        var customers = await db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Tag })
            .ToDictionaryAsync(c => c.Id, ct);
        var members = await db.ShopMembers
            .Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, m.User.FirstName })
            .ToDictionaryAsync(m => m.Id, ct);

        var items = events.Select(e =>
        {
            Guid.TryParse(e.EntityId, out var id);
            string? subject = null;
            long? amount = null;
            TransactionType? type = null;
            if (e.Entity == "transaction" && transactions.TryGetValue(id, out var t))
            {
                (subject, amount, type) = (Name(t.Customer, t.Tag), t.Amount, t.Type);
            }
            else if (e.Entity == "customer" && customers.TryGetValue(id, out var c))
            {
                subject = Name(c.Name, c.Tag);
            }
            else if (e.Entity == "member" && members.TryGetValue(id, out var m))
            {
                subject = m.FirstName;
            }

            return new AuditEventDto(e.Id, e.Action, e.Entity, e.MemberName, e.CreatedAt, subject, amount, type);
        }).ToList();

        return Cursor.Build(items, offset, take);
    }

    private static List<Guid> Ids(IEnumerable<string> raw) =>
        raw.Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();

    private static string Name(string name, string? tag) => string.IsNullOrWhiteSpace(tag) ? name : $"{name} ({tag})";
}
