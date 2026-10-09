using Debits.Domain.Customers;

namespace Debits.Domain.Tests.Customers;

public class CustomerMatcherTests
{
    private static CustomerCandidate C(string name, string? tag, double sim) =>
        new(Guid.CreateVersion7(), name, tag, null, Domain.Text.SearchKey.Normalize(name, tag), sim);

    [Fact]
    public void NoCandidates_IsNone()
    {
        CustomerMatcher.Resolve("Ahmad aka", []).Should().BeOfType<CustomerResolution.None>();
    }

    [Fact]
    public void ExactKeyMatch_WinsEvenWithOtherStrongCandidates()
    {
        var exact = C("Ahmad aka", null, 1.0);
        var r = CustomerMatcher.Resolve("Ахмад ака", [C("Axmad aka", "stomatolog", 0.7), exact]);
        r.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Should().Be(exact);
    }

    [Fact]
    public void StrongLeaderWithMargin_IsExact()
    {
        var lead = C("Ahmad aka", "qoshni", 0.8);
        var r = CustomerMatcher.Resolve("Ahmad aka qoshni", [lead, C("Ahmad aka", "stomatolog", 0.55)]);
        r.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Should().Be(lead);
    }

    [Fact]
    public void StrongLeaderWithoutMargin_IsAmbiguous()
    {
        var r = CustomerMatcher.Resolve("Ahmad aka", [C("Ahmad aka", "qoshni", 0.65), C("Ahmad aka", "stomatolog", 0.6)]);
        r.Should().BeOfType<CustomerResolution.Ambiguous>().Which.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void QueryEqualsNameOfSingleTaggedCustomer_IsExact()
    {
        var tagged = C("Ahmad aka", "stomatolog", 0.45);
        var r = CustomerMatcher.Resolve("Ахмад ака", [tagged, C("Ahmadjon", null, 0.4)]);
        r.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Should().Be(tagged);
    }

    [Fact]
    public void QueryEqualsNameOfSeveralTaggedCustomers_IsAmbiguous_EvenWithStrongLeader()
    {
        var r = CustomerMatcher.Resolve("Ahmad aka", [C("Ahmad aka", "qo", 0.85), C("Ahmad aka", "stomatolog", 0.45)]);
        r.Should().BeOfType<CustomerResolution.Ambiguous>().Which.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void WeakCandidates_AreOfferedForSelection()
    {
        var r = CustomerMatcher.Resolve("Ahmad", [C("Ahmad aka", "qoshni", 0.45), C("Ahmadjon", null, 0.4), C("Karim", null, 0.1)]);
        r.Should().BeOfType<CustomerResolution.Ambiguous>().Which.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void OnlyNoise_IsNone()
    {
        CustomerMatcher.Resolve("Ahmad", [C("Karim", null, 0.1)]).Should().BeOfType<CustomerResolution.None>();
    }

    [Fact]
    public void Ambiguous_IsCappedAtMaxCandidates()
    {
        var many = Enumerable.Range(0, 10).Select(i => C($"Ahmad {i}", null, 0.5)).ToList();
        CustomerMatcher.Resolve("Ahmad", many).Should().BeOfType<CustomerResolution.Ambiguous>()
            .Which.Candidates.Should().HaveCount(CustomerMatcher.MaxCandidates);
    }
}
