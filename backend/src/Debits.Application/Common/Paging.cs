using System.Buffers.Text;
using System.Text;

namespace Debits.Application.Common;

public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Opaque offset cursor: shops have hundreds of customers at most, so an offset is good enough and keeps ordering free.</summary>
public static class Cursor
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 200;

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    public static int Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/').PadRight(cursor.Length + (4 - cursor.Length % 4) % 4, '='));
            var text = Encoding.ASCII.GetString(bytes);
            return text.StartsWith("o:", StringComparison.Ordinal) && int.TryParse(text.AsSpan(2), out var offset) && offset >= 0 ? offset : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    public static string Encode(int offset) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes("o:" + offset)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Takes <paramref name="limit"/> + 1 rows to know whether there is a next page.</summary>
    public static Page<T> Build<T>(List<T> rowsPlusOne, int offset, int limit)
    {
        var hasMore = rowsPlusOne.Count > limit;
        var items = hasMore ? rowsPlusOne.Take(limit).ToList() : rowsPlusOne;
        return new Page<T>(items, hasMore ? Encode(offset + limit) : null);
    }
}
