using Debits.Domain.Text;

namespace Debits.Domain.Tests.Text;

public class SearchKeyTests
{
    [Theory]
    [InlineData("Ахмад ака", null, "ahmad aka")]
    [InlineData("Axmad aka", null, "ahmad aka")]
    [InlineData("Qodir", null, "kodir")]
    [InlineData("Қодир", null, "kodir")]
    [InlineData("Ahmad aka", null, "ahmad aka")]
    [InlineData("Ahmad aka", "stomatolog", "ahmad aka stomatolog")]
    [InlineData("  Ahmad   AKA ", " stomatolog ", "ahmad aka stomatolog")]
    [InlineData("O'tkir", null, "otkir")]
    [InlineData("Oʻtkir", null, "otkir")]
    [InlineData("O’tkir", null, "otkir")]
    [InlineData("Ўткир", null, "otkir")]
    [InlineData("G'ulnora (qo'shni)", null, "gulnora koshni")]
    [InlineData("Ahmad-aka, 2-dom", null, "ahmad aka 2 dom")]
    [InlineData("", null, "")]
    [InlineData("   ", "tag", "tag")]
    public void Normalize_ProducesStableKey(string name, string? tag, string expected)
    {
        SearchKey.Normalize(name, tag).Should().Be(expected);
    }

    [Theory]
    [InlineData("Ахмад ака", "axmad aka")]
    [InlineData("qirq to'qqiz", "qirq toqqiz")]
    [InlineData("oy oxirigacha", "oy oxirigacha")]
    public void NormalizeText_DoesNotFoldLetters(string input, string expected)
    {
        SearchKey.NormalizeText(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_CyrillicAndLatinSpellingsMatch()
    {
        SearchKey.Normalize("Ўткир ака").Should().Be(SearchKey.Normalize("O'tkir aka"));
        SearchKey.Normalize("Шерзод").Should().Be(SearchKey.Normalize("Sherzod"));
        SearchKey.Normalize("Ахмад").Should().Be(SearchKey.Normalize("Ahmad"));
        SearchKey.Normalize("Хуршид").Should().Be(SearchKey.Normalize("Hurshid"));
    }
}
