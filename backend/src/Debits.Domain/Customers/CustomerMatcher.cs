using Debits.Domain.Text;

namespace Debits.Domain.Customers;

/// <summary>A customer returned by fuzzy search together with its pg_trgm similarity to the query key.</summary>
public sealed record CustomerCandidate(Guid CustomerId, string Name, string? Tag, string? Phone, string SearchKey, double Similarity)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Tag) ? Name : $"{Name} ({Tag})";
}

public abstract record CustomerResolution
{
    public sealed record Exact(CustomerCandidate Customer) : CustomerResolution;

    public sealed record Ambiguous(IReadOnlyList<CustomerCandidate> Candidates) : CustomerResolution;

    public sealed record None : CustomerResolution;
}

/// <summary>
/// Decides whether fuzzy-search candidates identify one customer.
/// Exact key equality, a single name-level match, or a strong leader (≥ <see cref="StrongThreshold"/> with a ≥ <see cref="Margin"/> gap to the runner-up) → Exact;
/// otherwise the candidates above <see cref="WeakThreshold"/> are offered for selection; nothing above → None.
/// </summary>
public static class CustomerMatcher
{
    public const double StrongThreshold = 0.6;
    public const double Margin = 0.15;
    public const double WeakThreshold = 0.3;
    public const int MaxCandidates = 5;

    public static CustomerResolution Resolve(string query, IReadOnlyList<CustomerCandidate> candidates)
    {
        var key = SearchKey.Normalize(query);
        if (key.Length == 0 || candidates.Count == 0)
        {
            return new CustomerResolution.None();
        }

        var sorted = candidates.OrderByDescending(c => c.Similarity).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();

        var exact = sorted.Where(c => c.SearchKey == key).ToList();
        if (exact.Count == 1)
        {
            return new CustomerResolution.Exact(exact[0]);
        }

        if (exact.Count > 1)
        {
            return new CustomerResolution.Ambiguous(exact.Take(MaxCandidates).ToList());
        }

        // Query equals a customer's name and the customer has a tag ("Ahmad aka" → "Ahmad aka (stomatolog)"):
        // one such customer is the one meant; several must be picked by tag, however similar their keys are.
        var byName = sorted.Where(c => !string.IsNullOrWhiteSpace(c.Tag) && SearchKey.Normalize(c.Name) == key).ToList();
        if (byName.Count == 1)
        {
            return new CustomerResolution.Exact(byName[0]);
        }

        if (byName.Count > 1)
        {
            return new CustomerResolution.Ambiguous(byName.Take(MaxCandidates).ToList());
        }

        var top = sorted[0];
        var second = sorted.Count > 1 ? sorted[1].Similarity : 0;
        if (top.Similarity >= StrongThreshold && top.Similarity - second >= Margin)
        {
            return new CustomerResolution.Exact(top);
        }

        var weak = sorted.Where(c => c.Similarity >= WeakThreshold).Take(MaxCandidates).ToList();
        return weak.Count == 0 ? new CustomerResolution.None() : new CustomerResolution.Ambiguous(weak);
    }
}
