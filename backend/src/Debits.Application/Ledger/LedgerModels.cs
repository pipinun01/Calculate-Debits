using Debits.Domain.Entities;

namespace Debits.Application.Ledger;

public sealed record CreateTransactionCommand(
    Guid CustomerId,
    TransactionType Type,
    long Amount,
    string? Comment = null,
    DateOnly? DueDate = null,
    Guid? TargetDebtId = null,
    EntrySource Source = EntrySource.Manual,
    string? VoiceFileId = null,
    string? Transcript = null);

/// <summary>Null = leave unchanged. <see cref="Comment"/> "" clears the comment; <see cref="TargetDebtId"/> = <see cref="Guid.Empty"/> clears the target.</summary>
public sealed record UpdateTransactionCommand(
    long? Amount = null,
    string? Comment = null,
    DateOnly? DueDate = null,
    Guid? TargetDebtId = null);

public sealed record TransactionDto(
    Guid Id,
    Guid CustomerId,
    TransactionType Type,
    long Amount,
    string? Comment,
    DateOnly? DueDate,
    Guid? TargetDebtId,
    EntrySource Source,
    Guid CreatedByMemberId,
    string CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    /// <summary>For debts: amount not yet covered by payments. Null for payments.</summary>
    long? Remaining,
    /// <summary>Whether the acting member may edit/delete this entry right now.</summary>
    bool CanEdit);

public sealed record LedgerMutation(TransactionDto Transaction, long Balance);
