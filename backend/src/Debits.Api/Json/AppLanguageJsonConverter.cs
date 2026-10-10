using System.Text.Json;
using System.Text.Json.Serialization;
using Debits.Domain.Entities;

namespace Debits.Api.Json;

/// <summary><see cref="AppLanguage"/> ⇄ "uz-Latn" | "uz-Cyrl" | "ru" (the same codes as the locale files).</summary>
public sealed class AppLanguageJsonConverter : JsonConverter<AppLanguage>
{
    public override AppLanguage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var code = reader.GetString() ?? string.Empty;
        try
        {
            return AppLanguageCodes.FromCode(code);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new JsonException($"Unknown language '{code}'; expected uz-Latn, uz-Cyrl or ru");
        }
    }

    public override void Write(Utf8JsonWriter writer, AppLanguage value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToCode());
}
