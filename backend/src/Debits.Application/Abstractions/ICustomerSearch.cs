using Debits.Domain.Customers;

namespace Debits.Application.Abstractions;

/// <summary>Trigram search over <c>customers.search_key</c> within the current tenant.</summary>
public interface ICustomerSearch
{
    Task<IReadOnlyList<CustomerCandidate>> FindSimilarAsync(string searchKey, int limit, CancellationToken cancellationToken = default);
}
