using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Debits.Application.Users;
using Microsoft.AspNetCore.WebUtilities;

namespace Debits.Api.Auth;

public sealed record TelegramInitData(TelegramIdentity User, DateTimeOffset AuthDate, string? StartParam);

/// <summary>
/// Verifies Telegram Mini App <c>initData</c>: secret = HMAC_SHA256(key "WebAppData", bot token);
/// hash = HMAC_SHA256(secret, data_check_string) where data_check_string = sorted "key=value" lines without <c>hash</c>.
/// </summary>
public static class TelegramInitDataValidator
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static bool TryValidate(string initData, string botToken, DateTimeOffset now, out TelegramInitData? result, out string error)
    {
        result = null;
        error = "auth.init_data_invalid";
        if (string.IsNullOrWhiteSpace(initData) || string.IsNullOrEmpty(botToken))
        {
            return false;
        }

        var fields = QueryHelpers.ParseQuery(initData);
        if (!fields.TryGetValue("hash", out var hashValues) || hashValues.Count != 1)
        {
            return false;
        }

        var expected = ComputeHash(fields.Where(kv => kv.Key != "hash").Select(kv => (kv.Key, kv.Value.ToString())), botToken);
        var hashHex = hashValues.ToString();
        if (hashHex.Length != expected.Length * 2)
        {
            return false;
        }

        Span<byte> provided = stackalloc byte[expected.Length];
        try
        {
            Convert.FromHexString(hashHex, provided, out _, out _);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return false;
        }

        if (!fields.TryGetValue("auth_date", out var authDateValues) || !long.TryParse(authDateValues.ToString(), out var unix))
        {
            return false;
        }

        var authDate = DateTimeOffset.FromUnixTimeSeconds(unix);
        if (now - authDate > MaxAge || authDate - now > FutureTolerance)
        {
            error = "auth.init_data_expired";
            return false;
        }

        if (!fields.TryGetValue("user", out var userJson))
        {
            return false;
        }

        WebAppUser? user;
        try
        {
            user = JsonSerializer.Deserialize<WebAppUser>(userJson.ToString(), Json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (user is null || user.Id == 0)
        {
            return false;
        }

        result = new TelegramInitData(
            new TelegramIdentity(user.Id, user.FirstName ?? string.Empty, user.Username, user.LanguageCode),
            authDate,
            fields.TryGetValue("start_param", out var sp) ? sp.ToString() : null);
        error = string.Empty;
        return true;
    }

    /// <summary>Also used by tests to sign synthetic init data.</summary>
    public static byte[] ComputeHash(IEnumerable<(string Key, string Value)> fields, string botToken)
    {
        var dataCheckString = string.Join('\n', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString));
    }

    private sealed class WebAppUser
    {
        public long Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public string? LanguageCode { get; set; }
        [JsonPropertyName("is_bot")]
        public bool IsBot { get; set; }
    }
}
