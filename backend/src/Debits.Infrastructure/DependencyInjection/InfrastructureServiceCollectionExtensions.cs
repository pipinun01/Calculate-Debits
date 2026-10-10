using System.Net.Http.Headers;
using Debits.Application.Abstractions;
using Debits.Application.Entries;
using Debits.Infrastructure.Excel;
using Debits.Infrastructure.Groq;
using Debits.Infrastructure.Persistence;
using Debits.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Debits.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDebitsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? string.Empty;
        services.Configure<GroqOptions>(configuration.GetSection(GroqOptions.Section));
        return services.AddDebitsInfrastructure(connectionString);
    }

    public static IServiceCollection AddDebitsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DebitsDbContext>(options => DebitsDbContextOptions.Configure(options, connectionString));
        services.AddScoped<IDebitsDbContext>(sp => sp.GetRequiredService<DebitsDbContext>());
        services.AddScoped<ICustomerSearch, PostgresCustomerSearch>();
        services.AddSingleton<IExcelExporter, ExcelExporter>();

        services.AddOptions<GroqOptions>();
        services.AddHttpClient(GroqOptions.HttpClientName, (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<GroqOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        });
        services.AddSingleton<ISpeechToText, GroqSpeechToText>();
        services.AddSingleton<ILlmEntryParser, GroqEntryParser>();

        return services;
    }
}
