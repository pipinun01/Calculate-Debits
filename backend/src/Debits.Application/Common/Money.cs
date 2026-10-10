using System.Globalization;

namespace Debits.Application.Common;

public static class Money
{
    private static readonly NumberFormatInfo Grouped = new() { NumberGroupSeparator = " ", NumberGroupSizes = [3] };

    /// <summary>1250000 → "1 250 000". Amounts are always shown in full digits (never "1.25 mln") so a wrong scale is visible.</summary>
    public static string Format(long amount) => amount.ToString("#,0", Grouped);
}
