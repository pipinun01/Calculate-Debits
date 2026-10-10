using Debits.Application.Audit;
using Debits.Application.Customers;
using Debits.Application.Abstractions;
using Debits.Application.Debts;
using Debits.Application.Entries;
using Debits.Application.Export;
using Debits.Application.Ledger;
using Debits.Application.Members;
using Debits.Application.Reports;
using Debits.Application.Shops;
using Debits.Application.Tenancy;
using Debits.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Debits.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDebitsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<AuditWriter>();
        services.AddScoped<UserService>();
        services.AddScoped<ShopService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CustomerDirectoryService>();
        services.AddScoped<AllocationSyncService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<DebtService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ExportService>();
        services.AddScoped<AuditService>();

        services.AddSingleton<RegexEntryParser>();
        services.TryAddSingleton<IEntryParser, CompositeEntryParser>();
        services.AddScoped<EntryDraftService>();

        return services;
    }
}
