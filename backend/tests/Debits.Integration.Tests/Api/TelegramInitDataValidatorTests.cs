using Debits.Api.Auth;

namespace Debits.Integration.Tests.Api;

public class TelegramInitDataValidatorTests
{
    private const string Token = "123456789:TESTTOKENabcdefghijklmnopqrstuvwxyz";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static string Sign(DateTimeOffset authDate, string token = Token, string? startParam = null)
    {
        var fields = new List<(string Key, string Value)>
        {
            ("auth_date", authDate.ToUnixTimeSeconds().ToString()),
            ("user", """{"id":42,"first_name":"Ahmad","last_name":"Aka","username":"ahmad","language_code":"uz"}"""),
        };
        if (startParam is not null)
        {
            fields.Add(("start_param", startParam));
        }

        var hash = Convert.ToHexStringLower(TelegramInitDataValidator.ComputeHash(fields, token));
        return string.Join('&', fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}")) + "&hash=" + hash;
    }

    [Fact]
    public void Valid_ParsesUserAndStartParam()
    {
        TelegramInitDataValidator.TryValidate(Sign(Now.AddMinutes(-1), startParam: "inv_abc"), Token, Now, out var result, out _).Should().BeTrue();
        result!.User.TelegramUserId.Should().Be(42);
        result.User.FirstName.Should().Be("Ahmad");
        result.User.Username.Should().Be("ahmad");
        result.User.LanguageCode.Should().Be("uz");
        result.StartParam.Should().Be("inv_abc");
    }

    [Fact]
    public void WrongToken_Fails()
    {
        TelegramInitDataValidator.TryValidate(Sign(Now), "1:other", Now, out _, out var error).Should().BeFalse();
        error.Should().Be("auth.init_data_invalid");
    }

    [Fact]
    public void ModifiedField_Fails()
    {
        var data = Sign(Now).Replace("Ahmad", "Mallory");
        TelegramInitDataValidator.TryValidate(data, Token, Now, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Expired_Fails()
    {
        TelegramInitDataValidator.TryValidate(Sign(Now.AddHours(-25)), Token, Now, out _, out var error).Should().BeFalse();
        error.Should().Be("auth.init_data_expired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("hash=zz")]
    [InlineData("auth_date=1&user=%7B%7D")]
    public void Garbage_Fails(string data)
    {
        TelegramInitDataValidator.TryValidate(data, Token, Now, out _, out _).Should().BeFalse();
    }
}
