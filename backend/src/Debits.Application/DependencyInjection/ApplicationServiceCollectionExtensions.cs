using Debits.Application.Customers;
using Debits.Application.Ledger;
using Debits.Application.Members;
using Debits.Application.Shops;
using Debits.Application.Users;
using Debits.Application.Tenancy;
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

        services.AddScoped<UserService>();
        services.AddScoped<ShopService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<AllocationSyncService>();

        // Phase 3: LedgerService, ReportService, validators
        return services;
    }
}
