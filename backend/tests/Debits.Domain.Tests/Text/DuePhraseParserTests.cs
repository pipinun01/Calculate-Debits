using Debits.Domain.Text;

namespace Debits.Domain.Tests.Text;

public class DuePhraseParserTests
{
    private static readonly DateOnly Today = new(2026, 10, 9); // Friday

    [Theory]
    // uz-Latn
    [InlineData("3 kunga", "2026-10-12")]
    [InlineData("uch kunga", "2026-10-12")]
    [InlineData("10 kun", "2026-10-19")]
    [InlineData("o'n kunga", "2026-10-19")]
    [InlineData("bir haftaga", "2026-10-16")]
    [InlineData("haftaga", "2026-10-16")]
    [InlineData("1 hafta", "2026-10-16")]
    [InlineData("ikki haftaga", "2026-10-23")]
    [InlineData("2 haftaga", "2026-10-23")]
    [InlineData("bir oyga", "2026-11-09")]
    [InlineData("oyga", "2026-11-09")]
    [InlineData("oy oxirigacha", "2026-10-31")]
    [InlineData("oyning oxirigacha", "2026-10-31")]
    [InlineData("oy oxiriga", "2026-10-31")]
    [InlineData("hafta oxirigacha", "2026-10-11")]
    [InlineData("ertaga", "2026-10-10")]
    [InlineData("indinga", "2026-10-11")]
    [InlineData("Ahmad aka 550 ming 3 kunga", "2026-10-12")]
    [InlineData("Ahmad aka 550 ming, bir haftaga", "2026-10-16")]
    // uz-Cyrl
    [InlineData("3 кунга", "2026-10-12")]
    [InlineData("уч кунга", "2026-10-12")]
    [InlineData("бир ҳафтага", "2026-10-16")]
    [InlineData("ой охиригача", "2026-10-31")]
    [InlineData("эртага", "2026-10-10")]
    [InlineData("Ахмад ака 550 минг қарз, бир ойга", "2026-11-09")]
    // ru
    [InlineData("на 3 дня", "2026-10-12")]
    [InlineData("через 10 дней", "2026-10-19")]
    [InlineData("на один день", "2026-10-10")]
    [InlineData("на неделю", "2026-10-16")]
    [InlineData("на две недели", "2026-10-23")]
    [InlineData("на месяц", "2026-11-09")]
    [InlineData("до конца месяца", "2026-10-31")]
    [InlineData("завтра", "2026-10-10")]
    [InlineData("послезавтра", "2026-10-11")]
    [InlineData("Ахмад 500 тысяч сум на неделю", "2026-10-16")]
    public void Parse_FindsDueDate(string text, string expected)
    {
        var result = DuePhraseParser.Parse(text, Today);
        result.Should().NotBeNull();
        result!.DueDate.Should().Be(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ahmad aka 550 ming")]
    [InlineData("Oybek 100 ming")]
    [InlineData("Ахмад ака 550 минг қарз")]
    public void Parse_ReturnsNull_WhenNoDuePhrase(string text)
    {
        DuePhraseParser.Parse(text, Today).Should().BeNull();
    }

    [Fact]
    public void Parse_EndOfMonth_OnLastDay_GoesToNextMonth()
    {
        DuePhraseParser.Parse("oy oxirigacha", new DateOnly(2026, 10, 31))!.DueDate.Should().Be(new DateOnly(2026, 11, 30));
    }

    [Fact]
    public void Parse_EndOfWeek_OnSunday_GoesToNextSunday()
    {
        DuePhraseParser.Parse("hafta oxirigacha", new DateOnly(2026, 10, 11))!.DueDate.Should().Be(new DateOnly(2026, 10, 18));
    }

    [Fact]
    public void Parse_OneMonth_ClampsToMonthEnd()
    {
        DuePhraseParser.Parse("bir oyga", new DateOnly(2026, 1, 31))!.DueDate.Should().Be(new DateOnly(2026, 2, 28));
    }
}
