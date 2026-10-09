using Debits.Domain.Entities;
using Debits.Domain.Views;
using Microsoft.EntityFrameworkCore;

namespace Debits.Application.Abstractions;

public interface IDebitsDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<Shop> Shops { get; }
    DbSet<ShopMember> ShopMembers { get; }
    DbSet<Invite> Invites { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<PendingEntry> PendingEntries { get; }
    DbSet<ConversationState> ConversationStates { get; }
    DbSet<ReminderLogEntry> ReminderLog { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<CustomerBalance> CustomerBalances { get; }
    DbSet<OpenDebt> OpenDebts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <paramref name="action"/> inside a database transaction (no-op nesting if one is already open).</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>Takes a row lock on the customer (<c>SELECT … FOR UPDATE</c>) for the current transaction.</summary>
    Task LockCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
}
