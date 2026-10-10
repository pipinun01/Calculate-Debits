using Debits.Application.Export;

namespace Debits.Application.Abstractions;

/// <summary>Localised caption for a worksheet label key (<c>xl.*</c>).</summary>
public delegate string ExportText(string key);

/// <summary>Builds .xlsx files from export data.</summary>
public interface IExcelExporter
{
    byte[] CustomerLedger(CustomerLedgerExport data, ExportText text);

    byte[] Shop(ShopExport data, ExportText text);
}
