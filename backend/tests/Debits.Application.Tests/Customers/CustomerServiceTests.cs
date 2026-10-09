using Debits.Application.Abstractions;
using Debits.Application.Customers;
using Debits.Domain.Customers;
using NSubstitute;

namespace Debits.Application.Tests.Customers;

public class CustomerServiceTests
{
    private readonly ICustomerSearch _search = Substitute.For<ICustomerSearch>();

    [Fact]
    public async Task SearchAsync_NormalizesQuery_BeforeHittingSearch()
    {
        _search.FindSimilarAsync("ahmad aka", 10, Arg.Any<CancellationToken>()).Returns([]);
        var sut = new CustomerService(_search);

        await sut.SearchAsync("  Ахмад   Ака ");

        await _search.Received(1).FindSimilarAsync("ahmad aka", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_DoesNotTouchSearch()
    {
        var sut = new CustomerService(_search);

        var result = await sut.SearchAsync("   ");

        result.Should().BeEmpty();
        await _search.DidNotReceiveWithAnyArgs().FindSimilarAsync(default!, default, default);
    }

    [Fact]
    public async Task ResolveAsync_CombinesNameAndTag_AndDelegatesToMatcher()
    {
        var candidate = new CustomerCandidate(Guid.CreateVersion7(), "Ahmad aka", "stomatolog", null, "ahmad aka stomatolog", 1.0);
        _search.FindSimilarAsync("ahmad aka stomatolog", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([candidate]);
        var sut = new CustomerService(_search);

        var result = await sut.ResolveAsync("Ahmad aka", "stomatolog");

        result.Should().BeOfType<CustomerResolution.Exact>().Which.Customer.Should().Be(candidate);
    }

    [Fact]
    public async Task ResolveAsync_EmptyName_IsNone()
    {
        var sut = new CustomerService(_search);

        (await sut.ResolveAsync("")).Should().BeOfType<CustomerResolution.None>();
    }
}
