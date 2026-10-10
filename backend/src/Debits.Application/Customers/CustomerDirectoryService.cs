using Debits.Application.Abstractions;
using Debits.Application.Audit;
using Debits.Application.Common;
using Debits.Application.Ledger;
using Debits.Application.Tenancy;
using Debits.Domain;
using Debits.Domain.Entities;
using Debits.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Customers;

/// <summary>Customer list / card / create / edit for the Mini App. Fuzzy search itself lives in <see cref="CustomerService"/>.</summary>
public sealed class CustomerDirectoryService(
    IDebitsDbContext db,
    ITenantContext tenant,
    TimeProvider clock,
    CustomerService search,
    LedgerService ledger,
    AuditWriter audit)
{
    public const int MaxNameLength = 128;
    public const int MaxTagLength = 64;
    public const int MaxPhoneLength = 32;
    public const int MaxNotesLength = 1024;
    public const int DueSoonDays = 3;
    public const int SearchLimit = 50;

    public async Task<Page<CustomerListItem>> ListAsync(CustomerListQuery query, CancellationToken ct = default)
    {
        var today = await TodayAsync(ct);
        var soon = today.AddDays(DueSoonDays);
        var archived = query.Filter == CustomerFilter.Archived;

        IReadOnlyList<Guid>? searchIds = null;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var candidates = await search.SearchAsync(query.Q, SearchLimit, ct);
            searchIds = candidates.Select(c => c.CustomerId).ToList();
            if (searchIds.Count == 0)
            {
                return new Page<CustomerListItem>([], null);
            }
        }

        var q = db.Customers.Where(c => c.IsArchived == archived);
        if (searchIds is not null)
        {
            q = q.Where(c => searchIds.Contains(c.Id));
        }

        // Anonymous projection: EF can filter/sort over its members, which it cannot do over a positional record constructor.
        var projected = q.Select(c => new
        {
            c.Id,
            c.Name,
            c.Tag,
            c.Phone,
            Balance = db.CustomerBalances.Where(b => b.CustomerId == c.Id).Select(b => b.Balance).FirstOrDefault(),
            NextDueDate = db.OpenDebts.Where(d => d.CustomerId == c.Id).Min(d => (DateOnly?)d.DueDate),
            OverdueAmount = db.OpenDebts.Where(d => d.CustomerId == c.Id && d.DueDate < today).Sum(d => d.Remaining),
            DueSoonAmount = db.OpenDebts.Where(d => d.CustomerId == c.Id && d.DueDate >= today && d.DueDate <= soon).Sum(d => d.Remaining),
            c.IsArchived,
            c.CreatedAt,
        });

        projected = query.Filter switch
        {
            CustomerFilter.Debtors => projected.Where(x => x.Balance > 0),
            CustomerFilter.DueSoon => projected.Where(x => x.OverdueAmount > 0 || x.DueSoonAmount > 0),
            _ => projected,
        };

        if (searchIds is not null)
        {
            // Keep similarity order from the search; the result set is capped at SearchLimit.
            var rows = await projected.ToListAsync(ct);
            var order = searchIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            return new Page<CustomerListItem>(
                rows.OrderBy(r => order[r.Id])
                    .Select(x => new CustomerListItem(x.Id, x.Name, x.Tag, x.Phone, x.Balance, x.NextDueDate, x.OverdueAmount, x.DueSoonAmount, x.IsArchived, x.CreatedAt))
                    .ToList(),
                null);
        }

        projected = query.Sort switch
        {
            CustomerSort.Balance => projected.OrderByDescending(x => x.Balance).ThenBy(x => x.Name),
            CustomerSort.Due => projected.OrderBy(x => x.NextDueDate == null).ThenBy(x => x.NextDueDate).ThenBy(x => x.Name),
            _ => projected.OrderBy(x => x.Name).ThenBy(x => x.Id),
        };

        var offset = Cursor.Decode(query.Cursor);
        var take = Cursor.ClampLimit(query.Limit);
        var page = await projected
            .Skip(offset)
            .Take(take + 1)
            .Select(x => new CustomerListItem(x.Id, x.Name, x.Tag, x.Phone, x.Balance, x.NextDueDate, x.OverdueAmount, x.DueSoonAmount, x.IsArchived, x.CreatedAt))
            .ToListAsync(ct);
        return Cursor.Build(page, offset, take);
    }

    public async Task<CustomerDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var today = await TodayAsync(ct);
        var soon = today.AddDays(DueSoonDays);

        var head = await db.Customers
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Tag,
                c.Phone,
                c.Notes,
                c.IsArchived,
                c.CreatedAt,
                CreatedByName = db.ShopMembers.Where(m => m.Id == c.CreatedByMemberId).Select(m => m.User.FirstName).FirstOrDefault(),
                Balance = db.CustomerBalances.Where(b => b.CustomerId == c.Id).Select(b => b.Balance).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("customer.not_found", "Customer not found");

        var openDebts = await db.OpenDebts
            .Where(d => d.CustomerId == id)
            .OrderBy(d => d.DueDate)
            .ThenBy(d => d.CreatedAt)
            .Select(d => new
            {
                d.DebtId,
                d.Amount,
                d.Remaining,
                d.DueDate,
                d.CreatedAt,
                Comment = db.Transactions.Where(t => t.Id == d.DebtId).Select(t => t.Comment).FirstOrDefault(),
                CreatedByName = db.ShopMembers.Where(m => m.Id == d.CreatedByMemberId).Select(m => m.User.FirstName).FirstOrDefault() ?? "",
            })
            .ToListAsync(ct);

        var lastDebt = await ledger.Query(db.Transactions.Where(t => t.CustomerId == id && t.Type == TransactionType.Debt).OrderByDescending(t => t.CreatedAt)).FirstOrDefaultAsync(ct);
        var lastPayment = await ledger.Query(db.Transactions.Where(t => t.CustomerId == id && t.Type == TransactionType.Payment).OrderByDescending(t => t.CreatedAt)).FirstOrDefaultAsync(ct);

        return new CustomerDetail(
            head.Id, head.Name, head.Tag, head.Phone, head.Notes, head.Balance,
            openDebts.Count == 0 ? null : openDebts.Min(d => d.DueDate),
            openDebts.Where(d => d.DueDate < today).Sum(d => d.Remaining),
            openDebts.Where(d => d.DueDate >= today && d.DueDate <= soon).Sum(d => d.Remaining),
            head.IsArchived, head.CreatedAt, head.CreatedByName, lastDebt, lastPayment,
            openDebts.Select(d => new CustomerOpenDebt(d.DebtId, d.Amount, d.Remaining, d.DueDate, d.DueDate.DayNumber - today.DayNumber, d.CreatedAt, d.Comment, d.CreatedByName)).ToList());
    }

    public async Task<CustomerDetail> CreateAsync(CreateCustomerCommand cmd, CancellationToken ct = default)
    {
        var name = ValidateName(cmd.Name);
        var tag = Clean(cmd.Tag, MaxTagLength, "customer.tag_invalid");
        var phone = Clean(cmd.Phone, MaxPhoneLength, "customer.phone_invalid");
        var notes = Clean(cmd.Notes, MaxNotesLength, "customer.notes_invalid");

        var customer = Customer.Create(tenant.ShopId ?? Guid.Empty, name, tag, tenant.MemberId, phone);
        customer.Notes = notes;
        await EnsureUniqueAsync(customer.SearchKey, excludeId: null, ct);

        db.Customers.Add(customer);
        audit.Write("customer.create", "customer", customer.Id, new { customer.Name, customer.Tag });
        await db.SaveChangesAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<CustomerDetail> UpdateAsync(Guid id, UpdateCustomerCommand cmd, CancellationToken ct = default)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("customer.not_found", "Customer not found");

        if (cmd.Name is not null)
        {
            customer.Name = ValidateName(cmd.Name);
        }

        if (cmd.Tag is not null)
        {
            customer.Tag = Clean(cmd.Tag, MaxTagLength, "customer.tag_invalid");
        }

        if (cmd.Phone is not null)
        {
            customer.Phone = Clean(cmd.Phone, MaxPhoneLength, "customer.phone_invalid");
        }

        if (cmd.Notes is not null)
        {
            customer.Notes = Clean(cmd.Notes, MaxNotesLength, "customer.notes_invalid");
        }

        if (cmd.IsArchived is { } archived)
        {
            customer.IsArchived = archived;
        }

        customer.RefreshSearchKey();
        await EnsureUniqueAsync(customer.SearchKey, customer.Id, ct);
        audit.Write("customer.update", "customer", customer.Id, new { customer.Name, customer.Tag, customer.Phone, customer.IsArchived });
        await db.SaveChangesAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    private async Task EnsureUniqueAsync(string searchKey, Guid? excludeId, CancellationToken ct)
    {
        if (searchKey.Length == 0)
        {
            throw new DomainException("customer.name_invalid", "Name must contain letters or digits");
        }

        var clash = await db.Customers.AnyAsync(c => c.SearchKey == searchKey && c.Id != excludeId, ct);
        if (clash)
        {
            throw new ConflictException("customer.exists", "A customer with the same name and tag already exists; add a tag to tell them apart");
        }
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tz = await db.Shops.Where(s => s.Id == tenant.ShopId).Select(s => s.Timezone).SingleAsync(ct);
        return LocalTime.Today(clock, tz);
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new DomainException("customer.name_invalid", $"Name must be 1..{MaxNameLength} characters");
        }

        return name;
    }

    private static string? Clean(string? value, int max, string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > max)
        {
            throw new DomainException(code, $"Value must be at most {max} characters");
        }

        return value;
    }
}
