using Debits.Application.Ledger;

namespace Debits.Application.Customers;

public enum CustomerSort
{
    Name,
    Balance,
    Due,
}

public enum CustomerFilter
{
    All,
    Debtors,
    DueSoon,
    Archived,
}

public sealed record CustomerListQuery(
    string? Q = null,
    CustomerFilter Filter = CustomerFilter.All,
    CustomerSort Sort = CustomerSort.Name,
    string? Cursor = null,
    int? Limit = null);

public sealed record CustomerListItem(
    Guid Id,
    string Name,
    string? Tag,
    string? Phone,
    long Balance,
    DateOnly? NextDueDate,
    long OverdueAmount,
    long DueSoonAmount,
    bool IsArchived,
    DateTime CreatedAt);

public sealed record CustomerDetail(
    Guid Id,
    string Name,
    string? Tag,
    string? Phone,
    string? Notes,
    long Balance,
    DateOnly? NextDueDate,
    long OverdueAmount,
    long DueSoonAmount,
    bool IsArchived,
    DateTime CreatedAt,
    string? CreatedByName,
    TransactionDto? LastDebt,
    TransactionDto? LastPayment,
    IReadOnlyList<CustomerOpenDebt> OpenDebts);

public sealed record CustomerOpenDebt(Guid DebtId, long Amount, long Remaining, DateOnly DueDate, int DaysLeft, DateTime CreatedAt, string? Comment, string CreatedByName);

public sealed record CreateCustomerCommand(string Name, string? Tag = null, string? Phone = null, string? Notes = null);

/// <summary>Null = unchanged; "" clears Tag / Phone / Notes.</summary>
public sealed record UpdateCustomerCommand(string? Name = null, string? Tag = null, string? Phone = null, string? Notes = null, bool? IsArchived = null);
