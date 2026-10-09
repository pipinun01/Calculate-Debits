using Debits.Application.Abstractions;
using Debits.Infrastructure.Persistence;
using Debits.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Debits.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDebitsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? string.Empty;
        return services.AddDebitsInfrastructure(connectionString);
    }

    public static IServiceCollection AddDebitsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DebitsDbContext>(options => DebitsDbContextOptions.Configure(options, connectionString));
        services.AddScoped<IDebitsDbContext>(sp => sp.GetRequiredService<DebitsDbContext>());
        services.AddScoped<ICustomerSearch, PostgresCustomerSearch>();

        // Phase 4: Groq clients
        return services;
    }
}
