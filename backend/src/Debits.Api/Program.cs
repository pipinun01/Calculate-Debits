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
        .WriteTo.Console());

    builder.Services.AddControllers();
    builder.Services.AddProblemDetails();
    builder.Services.AddHealthChecks();

    builder.Services
        .AddDebitsApplication()
        .AddDebitsInfrastructure(builder.Configuration)
        .AddDebitsBot(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

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
