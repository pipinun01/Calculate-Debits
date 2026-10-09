using Debits.Application.Abstractions;
using Debits.Application.Tenancy;
using Debits.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Debits.Infrastructure.Search;

/// <summary>pg_trgm <c>similarity()</c> over <c>customers.search_key</c>, restricted to the current shop and non-archived rows.</summary>
public sealed class PostgresCustomerSearch(IDebitsDbContext db, ITenantContext tenant) : ICustomerSearch
{
    /// <summary>Anything below this is noise and not returned at all.</summary>
    public const double MinSimilarity = 0.2;

    public async Task<IReadOnlyList<CustomerCandidate>> FindSimilarAsync(string searchKey, int limit, CancellationToken cancellationToken = default)
    {
        var shopId = tenant.ShopId ?? throw new TenantMismatchException("Customer search requires a tenant");

        var rows = await db.Customers
            .Where(c => c.ShopId == shopId && !c.IsArchived)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Tag,
                c.Phone,
                c.SearchKey,
                Similarity = c.SearchKey == searchKey ? 1.0 : EF.Functions.TrigramsSimilarity(c.SearchKey, searchKey),
            })
            .Where(x => x.Similarity >= MinSimilarity || x.SearchKey.StartsWith(searchKey + " "))
            .OrderByDescending(x => x.Similarity)
            .ThenBy(x => x.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CustomerCandidate(r.Id, r.Name, r.Tag, r.Phone, r.SearchKey, r.Similarity)).ToList();
    }
}
