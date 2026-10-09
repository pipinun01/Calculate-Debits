using Debits.Domain.Text;

namespace Debits.Domain.Tests.Text;

public class UzTranslitTests
{
    [Theory]
    [InlineData("Ахмад ака", "Axmad aka")]
    [InlineData("Ўткир", "O'tkir")]
    [InlineData("Ғани", "G'ani")]
    [InlineData("Қодир", "Qodir")]
    [InlineData("Ҳасан", "Hasan")]
    [InlineData("Ергаш", "Yergash")]
    [InlineData("Саодат", "Saodat")]
    [InlineData("Шерзод", "Sherzod")]
    [InlineData("Чори", "Chori")]
    [InlineData("Юлдуз", "Yulduz")]
    [InlineData("Яшар", "Yashar")]
    [InlineData("Ёқуб", "Yoqub")]
    [InlineData("Сергей", "Sergey")]
    [InlineData("Игорь", "Igor")]
    [InlineData("Ahmad aka", "Ahmad aka")]
    [InlineData("минг", "ming")]
    [InlineData("тўрт", "to'rt")]
    [InlineData("эллик", "ellik")]
    [InlineData("пятьсот", "pyatsot")]
    [InlineData("", "")]
    public void ToLatin_TransliteratesCyrillic(string input, string expected)
    {
        UzTranslit.ToLatin(input).Should().Be(expected);
    }

    [Fact]
    public void ToLatin_KeepsAllCapsWords()
    {
        UzTranslit.ToLatin("ШЕРЗОД").Should().Be("SHERZOD");
    }
}
