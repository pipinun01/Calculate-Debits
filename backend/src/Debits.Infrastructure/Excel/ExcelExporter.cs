using ClosedXML.Excel;
using Debits.Application.Abstractions;
using Debits.Application.Export;
using Debits.Domain.Entities;

namespace Debits.Infrastructure.Excel;

/// <summary>ClosedXML workbooks: one sheet for a customer ledger, three (summary, debtors, entries) for a shop.</summary>
public sealed class ExcelExporter : IExcelExporter
{
    private const string MoneyFormat = "#,##0";
    private const string DateFormat = "dd.MM.yyyy";
    private const string DateTimeFormat = "dd.MM.yyyy HH:mm";

    public byte[] CustomerLedger(CustomerLedgerExport data, ExportText text)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(SheetName(text("xl.sheet.ledger")));

        var row = Facts(ws, 1,
            (text("xl.shop"), data.ShopName, null),
            (text("xl.customer"), data.CustomerName, null),
            (text("xl.phone"), data.CustomerPhone ?? string.Empty, null),
            (text("xl.balance"), data.Balance, MoneyFormat),
            (text("xl.generated"), data.GeneratedLocal, DateTimeFormat));

        Entries(ws, row + 1, data.Rows, text, withCustomer: false);
        return Save(workbook);
    }

    public byte[] Shop(ShopExport data, ExportText text)
    {
        using var workbook = new XLWorkbook();
        var s = data.Summary;

        var summary = workbook.AddWorksheet(SheetName(text("xl.sheet.summary")));
        Facts(summary, 1,
            (text("xl.shop"), data.ShopName, null),
            (text("xl.period"), $"{s.From.ToString(DateFormat)} — {s.To.ToString(DateFormat)}", null),
            (text("xl.generated"), data.GeneratedLocal, DateTimeFormat),
            (text("xl.collected"), s.Collected, MoneyFormat),
            (text("xl.payments_count"), s.PaymentsCount, null),
            (text("xl.new_debts"), s.NewDebts, MoneyFormat),
            (text("xl.debts_count"), s.DebtsCount, null),
            (text("xl.rate"), s.CollectionRate, "0%"),
            (text("xl.open_total"), s.OpenTotal, MoneyFormat),
            (text("xl.overdue_total"), s.OverdueTotal, MoneyFormat));
        summary.Columns().AdjustToContents();

        var debtors = workbook.AddWorksheet(SheetName(text("xl.sheet.debtors")));
        Header(debtors, 1, text("xl.customer"), text("xl.phone"), text("xl.balance"), text("xl.overdue"), text("xl.next_due"));
        var r = 2;
        foreach (var d in data.Debtors)
        {
            debtors.Cell(r, 1).Value = d.Name;
            debtors.Cell(r, 2).Value = d.Phone ?? string.Empty;
            Money(debtors.Cell(r, 3), d.Balance);
            Money(debtors.Cell(r, 4), d.Overdue);
            Date(debtors.Cell(r, 5), d.NextDueDate);
            r++;
        }

        debtors.Cell(r, 1).Value = text("xl.total");
        Money(debtors.Cell(r, 3), data.Debtors.Sum(d => d.Balance));
        Money(debtors.Cell(r, 4), data.Debtors.Sum(d => d.Overdue));
        debtors.Row(r).Style.Font.Bold = true;
        debtors.SheetView.FreezeRows(1);
        debtors.Columns().AdjustToContents();

        var entries = workbook.AddWorksheet(SheetName(text("xl.sheet.transactions")));
        Entries(entries, 1, data.Transactions, text, withCustomer: true);
        return Save(workbook);
    }

    private static void Entries(IXLWorksheet ws, int headerRow, IReadOnlyList<ExportRow> rows, ExportText text, bool withCustomer)
    {
        var headers = new List<string> { text("xl.date") };
        if (withCustomer)
        {
            headers.Add(text("xl.customer"));
        }

        headers.AddRange([text("xl.type"), text("xl.amount"), text("xl.due"), text("xl.remaining"), text("xl.comment"), text("xl.by")]);
        if (!withCustomer)
        {
            headers.Add(text("xl.running"));
        }

        Header(ws, headerRow, [.. headers]);
        var (debt, payment) = (text("xl.type.debt"), text("xl.type.payment"));

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            var c = 1;
            ws.Cell(r, c).Value = row.LocalTime;
            ws.Cell(r, c++).Style.DateFormat.Format = DateTimeFormat;
            if (withCustomer)
            {
                ws.Cell(r, c++).Value = row.CustomerName;
            }

            ws.Cell(r, c++).Value = row.Type == TransactionType.Debt ? debt : payment;
            Money(ws.Cell(r, c++), row.Amount);
            Date(ws.Cell(r, c++), row.DueDate);
            if (row.Remaining is { } remaining)
            {
                Money(ws.Cell(r, c), remaining);
            }

            c++;
            ws.Cell(r, c++).Value = row.Comment ?? string.Empty;
            ws.Cell(r, c++).Value = row.CreatedByName;
            if (row.Balance is { } balance)
            {
                Money(ws.Cell(r, c), balance);
            }

            if (row.Type == TransactionType.Payment)
            {
                ws.Row(r).Style.Font.FontColor = XLColor.FromHtml("#047857");
            }

            r++;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();
    }

    /// <summary>«Label — value» lines at the top of a sheet; returns the next free row.</summary>
    private static int Facts(IXLWorksheet ws, int row, params (string Label, XLCellValue Value, string? Format)[] facts)
    {
        foreach (var (label, value, format) in facts)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = value;
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            if (format is not null)
            {
                ws.Cell(row, 2).Style.NumberFormat.Format = format;
            }

            row++;
        }

        return row;
    }

    private static void Header(IXLWorksheet ws, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            ws.Cell(row, i + 1).Value = titles[i];
        }

        var range = ws.Range(row, 1, row, titles.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDE9FE");
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
    }

    private static void Money(IXLCell cell, long amount)
    {
        cell.Value = amount;
        cell.Style.NumberFormat.Format = MoneyFormat;
    }

    private static void Date(IXLCell cell, DateOnly? date)
    {
        if (date is { } d)
        {
            cell.Value = d.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = DateFormat;
        }
    }

    /// <summary>Excel allows 31 characters and none of <c>: \ / ? * [ ]</c> in a sheet name.</summary>
    private static string SheetName(string name)
    {
        var clean = new string(name.Where(ch => !@":\/?*[]".Contains(ch)).ToArray()).Trim();
        return clean.Length > 31 ? clean[..31] : clean.Length == 0 ? "Sheet" : clean;
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
