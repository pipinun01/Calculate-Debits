using Microsoft.Extensions.DependencyInjection;

namespace Debits.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDebitsApplication(this IServiceCollection services)
    {
        // Phase 3: services, validators
        return services;
    }
}
