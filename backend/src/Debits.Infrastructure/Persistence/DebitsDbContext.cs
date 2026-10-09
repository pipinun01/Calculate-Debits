using Debits.Application.Abstractions;
using Debits.Application.Tenancy;
using Debits.Domain.Entities;
using Debits.Domain.Views;
using Debits.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Debits.Infrastructure.Persistence;

public sealed class DebitsDbContext(DbContextOptions<DebitsDbContext> options, ITenantContext tenant, TimeProvider clock)
    : DbContext(options), IDebitsDbContext
{
    public const string TenantFilter = "Tenant";
    public const string SoftDeleteFilter = "SoftDelete";

    private readonly ITenantContext _tenant = tenant;

    /// <summary>Evaluated per query as a parameter by the named tenant filter.</summary>
    private Guid? CurrentShopId => _tenant.ShopId;

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShopMember> ShopMembers => Set<ShopMember>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<PendingEntry> PendingEntries => Set<PendingEntry>();
    public DbSet<ConversationState> ConversationStates => Set<ConversationState>();
    public DbSet<ReminderLogEntry> ReminderLog => Set<ReminderLogEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CustomerBalance> CustomerBalances => Set<CustomerBalance>();
    public DbSet<OpenDebt> OpenDebts => Set<OpenDebt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DebitsDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                typeof(DebitsDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }

        modelBuilder.Entity<Transaction>().HasQueryFilter(SoftDeleteFilter, t => !t.IsDeleted);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<MemberRole>().HaveConversion<LowercaseEnumConverter<MemberRole>>().HaveMaxLength(16);
        configurationBuilder.Properties<TransactionType>().HaveConversion<LowercaseEnumConverter<TransactionType>>().HaveMaxLength(16);
        configurationBuilder.Properties<EntrySource>().HaveConversion<LowercaseEnumConverter<EntrySource>>().HaveMaxLength(16);
        configurationBuilder.Properties<PendingEntryStatus>().HaveConversion<LowercaseEnumConverter<PendingEntryStatus>>().HaveMaxLength(16);
        configurationBuilder.Properties<ReminderKind>().HaveConversion<LowercaseEnumConverter<ReminderKind>>().HaveMaxLength(16);
        configurationBuilder.Properties<ReminderRecipients>().HaveConversion<LowercaseEnumConverter<ReminderRecipients>>().HaveMaxLength(16);
        configurationBuilder.Properties<AppLanguage>().HaveConversion<AppLanguageConverter>().HaveMaxLength(8);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(TenantFilter, e => CurrentShopId == null || e.ShopId == CurrentShopId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantAndTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTenantAndTimestamps();
        return base.SaveChanges();
    }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var tx = await Database.BeginTransactionAsync(ct);
            var result = await action(ct);
            await tx.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    public Task LockCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Database.ExecuteSqlAsync($"SELECT id FROM customers WHERE id = {customerId} FOR UPDATE", cancellationToken);

    private void ApplyTenantAndTimestamps()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var shopId = _tenant.ShopId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Entity is ITenantEntity tenantEntity)
            {
                if (tenantEntity.ShopId == Guid.Empty)
                {
                    tenantEntity.ShopId = shopId
                        ?? throw new TenantMismatchException($"{entry.Metadata.ClrType.Name} requires ShopId and no tenant is set");
                }
                else if (shopId is not null && tenantEntity.ShopId != shopId)
                {
                    throw new TenantMismatchException(
                        $"{entry.Metadata.ClrType.Name} belongs to shop {tenantEntity.ShopId} but current tenant is {shopId}");
                }
            }

            if (entry.State == EntityState.Added && entry.Entity is IHasCreatedAt created && created.CreatedAt == default)
            {
                created.CreatedAt = now;
            }

            if (entry.Entity is IHasUpdatedAt updated && (entry.State == EntityState.Modified || updated.UpdatedAt == default))
            {
                updated.UpdatedAt = now;
            }
        }
    }
}
