using Debits.Application.Abstractions;
using Debits.Application.Entries;
using Debits.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Debits.Application.Tests.Entries;

public class CompositeEntryParserTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private readonly ILlmEntryParser _llm = Substitute.For<ILlmEntryParser>();

    private CompositeEntryParser Sut(bool configured = true)
    {
        _llm.IsConfigured.Returns(configured);
        return new CompositeEntryParser([_llm], NullLogger<CompositeEntryParser>.Instance);
    }

    private void LlmReturns(TransactionType? intent, string? customer, long? amount, DateOnly? due = null, string? comment = null) =>
        _llm.ParseAsync(Arg.Any<string>(), Today, Arg.Any<AppLanguage>(), Arg.Any<CancellationToken>())
            .Returns(new ParsedEntry(intent, customer, amount, due, comment, 0.9, "groq", []));

    [Fact]
    public async Task NotConfigured_UsesRegexOnly()
    {
        var result = await Sut(configured: false).ParseAsync("Ahmad aka 550 ming qarz", Today, AppLanguage.UzLatn);

        result.Parser.Should().Be("regex");
        result.AmountUzs.Should().Be(550_000);
        await _llm.DidNotReceiveWithAnyArgs().ParseAsync(default!, default, default, default);
    }

    [Fact]
    public async Task LlmFailure_FallsBackToRegex()
    {
        _llm.ParseAsync(Arg.Any<string>(), Today, Arg.Any<AppLanguage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("429"));

        var result = await Sut().ParseAsync("Nodir uka 120 ming to'ladi", Today, AppLanguage.UzLatn);

        result.Parser.Should().Be("regex");
        result.Intent.Should().Be(TransactionType.Payment);
        result.CustomerName.Should().Be("Nodir uka");
        result.AmountUzs.Should().Be(120_000);
    }

    [Fact]
    public async Task LlmResult_IsUsed_AndRegexFillsTheGaps()
    {
        LlmReturns(TransactionType.Debt, "Zulfiya opa", 250_000, due: null, comment: " un, shakar ");

        var result = await Sut().ParseAsync("Zulfiya opaga 250 ming nasiya, bir haftaga, un bilan shakar", Today, AppLanguage.UzLatn);

        result.Parser.Should().Be("groq");
        result.CustomerName.Should().Be("Zulfiya opa");
        result.Comment.Should().Be("un, shakar");
        result.DueDate.Should().Be(new DateOnly(2026, 10, 16), "the regex found the term the LLM left empty");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task AmountMismatch_PrefersRegex_AndWarns()
    {
        LlmReturns(TransactionType.Debt, "Ahmad aka", 55_000);

        var result = await Sut().ParseAsync("Ahmad akaga besh yuz ellik ming qarz", Today, AppLanguage.UzLatn);

        result.AmountUzs.Should().Be(550_000);
        result.Warnings.Should().Contain(RegexEntryParser.WarnAmountMismatch);
    }

    [Fact]
    public async Task BareSmallNumber_KeepsLlmAmount_ButWarns()
    {
        LlmReturns(TransactionType.Debt, "Sardor", 50_000);

        var result = await Sut().ParseAsync("Sardor ellik qarz", Today, AppLanguage.UzLatn);

        result.AmountUzs.Should().Be(50_000);
        result.Warnings.Should().Contain(RegexEntryParser.WarnAmountMismatch);
    }

    [Fact]
    public async Task Payment_DropsDueDate_AndMissingFieldsAreFlagged()
    {
        LlmReturns(TransactionType.Payment, null, null, due: new DateOnly(2026, 10, 20));

        var result = await Sut().ParseAsync("to'ladi", Today, AppLanguage.UzLatn);

        result.DueDate.Should().BeNull();
        result.Warnings.Should().Contain([RegexEntryParser.WarnAmountMissing, RegexEntryParser.WarnCustomerMissing]);
    }

    [Fact]
    public async Task SmallTalk_DoesNotSpendAnLlmCall()
    {
        var result = await Sut().ParseAsync("salom, ishlar qalay", Today, AppLanguage.UzLatn);

        result.Parser.Should().Be("regex");
        await _llm.DidNotReceiveWithAnyArgs().ParseAsync(default!, default, default, default);
    }

    [Fact]
    public async Task LongText_IsTruncated_BeforeTheLlmSeesIt()
    {
        LlmReturns(null, null, null);

        await Sut().ParseAsync("qarz " + new string('a', 5000), Today, AppLanguage.UzLatn);

        await _llm.Received(1).ParseAsync(Arg.Is<string>(s => s.Length == CompositeEntryParser.MaxTextLength), Today, Arg.Any<AppLanguage>(), Arg.Any<CancellationToken>());
    }
}
