using Debits.Api;
using Debits.Api.Auth;
using Debits.Api.Configuration;
using Debits.Application.DependencyInjection;
using Debits.Bot.DependencyInjection;
using Debits.Infrastructure.DependencyInjection;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddDotEnv(builder.Environment.ContentRootPath);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(),
        // Several hosts share one process in integration tests; the static bootstrap logger stays for Log.Fatal below.
        preserveStaticLogger: true);

    builder.Services.AddHealthChecks();

    builder.Services
        .AddDebitsApplication()
        .AddDebitsInfrastructure(builder.Configuration)
        .AddDebitsBot(builder.Configuration)
        .AddDebitsApi(builder.Configuration, builder.Environment);

    var app = builder.Build();

    if (string.IsNullOrWhiteSpace(app.Configuration["Jwt:Secret"]))
    {
        app.Logger.LogWarning("JWT_SECRET is empty; using the built-in Development secret");
    }

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseAuthentication();
    app.UseMiddleware<TenantMiddleware>();
    app.UseAuthorization();

    app.MapHealthChecks("/health");
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
