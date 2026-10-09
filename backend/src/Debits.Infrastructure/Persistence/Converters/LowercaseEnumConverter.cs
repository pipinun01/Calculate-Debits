using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Debits.Infrastructure.Persistence.Converters;

/// <summary>Stores enums as lowercase snake_case text: <c>DueIn3</c> → <c>due_in_3</c>, <c>Owner</c> → <c>owner</c>.</summary>
public sealed class LowercaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => ToSnake(v),
    s => FromSnake(s))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> Forward = Enum.GetValues<TEnum>().ToDictionary(v => v, v => Snake(v.ToString()));
    private static readonly Dictionary<string, TEnum> Backward = Forward.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static string ToSnake(TEnum value) => Forward[value];

    public static TEnum FromSnake(string value) => Backward.TryGetValue(value, out var v)
        ? v
        : throw new InvalidOperationException($"Unknown {typeof(TEnum).Name} value '{value}'");

    private static string Snake(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(name[i - 1]))))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
