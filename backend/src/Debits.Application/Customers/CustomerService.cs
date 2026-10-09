using Debits.Application.Abstractions;
using Debits.Domain.Customers;
using Debits.Domain.Text;

namespace Debits.Application.Customers;

public sealed class CustomerService(ICustomerSearch search)
{
    public const int DefaultSearchLimit = 10;

    /// <summary>Fuzzy search for the Mini App list / autocomplete. Empty query → empty result.</summary>
    public async Task<IReadOnlyList<CustomerCandidate>> SearchAsync(string query, int limit = DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        var key = SearchKey.Normalize(query);
        if (key.Length == 0)
        {
            return [];
        }

        return await search.FindSimilarAsync(key, limit, cancellationToken);
    }

    /// <summary>Resolves a spoken/typed customer reference to Exact / Ambiguous / None.</summary>
    public async Task<CustomerResolution> ResolveAsync(string name, string? tag = null, CancellationToken cancellationToken = default)
    {
        var key = SearchKey.Normalize(name, tag);
        if (key.Length == 0)
        {
            return new CustomerResolution.None();
        }

        var candidates = await search.FindSimilarAsync(key, CustomerMatcher.MaxCandidates * 2, cancellationToken);
        return CustomerMatcher.Resolve(key, candidates);
    }
}
