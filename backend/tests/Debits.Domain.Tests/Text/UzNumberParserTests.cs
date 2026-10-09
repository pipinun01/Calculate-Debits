using Debits.Domain.Text;

namespace Debits.Domain.Tests.Text;

public class UzNumberParserTests
{
    [Theory]
    // digits
    [InlineData("550000", 550_000)]
    [InlineData("550 000", 550_000)]
    [InlineData("550.000", 550_000)]
    [InlineData("550,000", 550_000)]
    [InlineData("1 500 000", 1_500_000)]
    [InlineData("1.500.000", 1_500_000)]
    [InlineData("1,500,000", 1_500_000)]
    [InlineData("1,5 mln", 1_500_000)]
    [InlineData("1.5 million", 1_500_000)]
    [InlineData("2,5 млн", 2_500_000)]
    [InlineData("550 ming", 550_000)]
    [InlineData("550ming", 550_000)]
    [InlineData("550 минг", 550_000)]
    [InlineData("550 тысяч", 550_000)]
    [InlineData("550 тыс", 550_000)]
    [InlineData("5 mln 200 ming", 5_200_000)]
    [InlineData("3 million", 3_000_000)]
    [InlineData("12 so'm", 12)]
    // uz-Latn words
    [InlineData("besh yuz ellik ming", 550_000)]
    [InlineData("besh yuz ming", 500_000)]
    [InlineData("ellik ming", 50_000)]
    [InlineData("ellik", 50)]
    [InlineData("ming", 1_000)]
    [InlineData("million", 1_000_000)]
    [InlineData("bir million", 1_000_000)]
    [InlineData("bir million ikki yuz ming", 1_200_000)]
    [InlineData("o'n besh ming", 15_000)]
    [InlineData("oʻn besh ming", 15_000)]
    [InlineData("yigirma besh ming", 25_000)]
    [InlineData("to'rt yuz ming", 400_000)]
    [InlineData("to'qqiz yuz to'qson to'qqiz ming", 999_000)]
    [InlineData("ikki yuz ellik", 250)]
    [InlineData("yarim million", 500_000)]
    [InlineData("bir yarim million", 1_500_000)]
    [InlineData("ikki yarim million", 2_500_000)]
    [InlineData("uch yuz oltmish besh ming", 365_000)]
    [InlineData("sakson ming", 80_000)]
    [InlineData("yetti yuz ming", 700_000)]
    // uz-Cyrl words
    [InlineData("беш юз эллик минг", 550_000)]
    [InlineData("эллик минг", 50_000)]
    [InlineData("ўн беш минг", 15_000)]
    [InlineData("тўрт юз минг", 400_000)]
    [InlineData("бир миллион", 1_000_000)]
    [InlineData("икки юз минг", 200_000)]
    [InlineData("етти юз минг", 700_000)]
    [InlineData("ярим миллион", 500_000)]
    [InlineData("тўқсон минг", 90_000)]
    // ru words
    [InlineData("пятьсот тысяч", 500_000)]
    [InlineData("пятьсот пятьдесят тысяч", 550_000)]
    [InlineData("двести пятьдесят тысяч", 250_000)]
    [InlineData("сто тысяч", 100_000)]
    [InlineData("сто пятьдесят тысяч", 150_000)]
    [InlineData("тысяча", 1_000)]
    [InlineData("пятнадцать тысяч", 15_000)]
    [InlineData("миллион двести тысяч", 1_200_000)]
    [InlineData("полтора миллиона", 1_500_000)]
    [InlineData("пол миллиона", 500_000)]
    [InlineData("два миллиона", 2_000_000)]
    [InlineData("двадцать пять тысяч", 25_000)]
    [InlineData("девятьсот девяносто девять тысяч", 999_000)]
    // mixes and sentences
    [InlineData("Ahmad aka 550 ming qarz", 550_000)]
    [InlineData("Ахмад ака 550 минг қарз", 550_000)]
    [InlineData("Ahmad aka besh yuz ellik ming so'm qarz oldi", 550_000)]
    [InlineData("Ahmad aka 550 ming 3 kunga", 550_000)]
    [InlineData("Ahmad aka 3 kunga 550 000", 550_000)]
    [InlineData("Ahmad aka 50000 so'm 1 hafta", 50_000)]
    [InlineData("Ахмад 500 тысяч сум на неделю", 500_000)]
    [InlineData("Ahmad aka 5 ming 2 hafta", 5_000)]
    [InlineData("Karim 200 ming to'ladi", 200_000)]
    [InlineData("Карим оплатил 200 тысяч", 200_000)]
    [InlineData("Dilnoza opa 1,5 mln qarz, oy oxirigacha", 1_500_000)]
    [InlineData("Ahmad aka 550000 so'm 3 kunga", 550_000)]
    public void ParseAmount_FindsExpectedValue(string text, long expected)
    {
        UzNumberParser.ParseAmount(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ahmad aka qarz")]
    [InlineData("Ахмад ака қарз берди")]
    [InlineData("hello world")]
    public void ParseAmount_ReturnsNull_WhenNoNumber(string text)
    {
        UzNumberParser.ParseAmount(text).Should().BeNull();
    }

    [Fact]
    public void FindAll_ReturnsEveryNumberWithSpans()
    {
        var text = "Ahmad aka 550 ming 3 kunga";
        var all = UzNumberParser.FindAll(text);

        all.Should().HaveCount(2);
        all[0].Value.Should().Be(550_000);
        all[0].HasScaleWord.Should().BeTrue();
        text.Substring(all[0].Start, all[0].Length).Should().Be("550 ming");
        all[1].Value.Should().Be(3);
        all[1].HasScaleWord.Should().BeFalse();
        text.Substring(all[1].Start, all[1].Length).Should().Be("3");
    }

    [Fact]
    public void FindAll_MarksCurrencyWord()
    {
        var all = UzNumberParser.FindAll("50000 so'm 1 hafta");
        all[0].HasCurrencyWord.Should().BeTrue();
        all[1].HasCurrencyWord.Should().BeFalse();
    }

    [Theory]
    [InlineData("uch", 3)]
    [InlineData("15", 15)]
    [InlineData("o'n besh", 15)]
    [InlineData("две", 2)]
    public void ParseSingle_ParsesSmallNumbers(string phrase, long expected)
    {
        UzNumberParser.ParseSingle(phrase).Should().Be(expected);
    }
}
