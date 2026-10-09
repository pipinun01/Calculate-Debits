using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Debits.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDebitsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Phase 1: DbContext + Npgsql; Phase 4: Groq clients
        return services;
    }
}
