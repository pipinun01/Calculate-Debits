using Debits.Api;
using Debits.Api.Auth;
using Debits.Api.Configuration;
using Debits.Application.DependencyInjection;
using Debits.Bot.DependencyInjection;
using Debits.Infrastructure.DependencyInjection;
using Debits.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
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

    // Behind Caddy in the compose network: the app port is not published, so the proxy's headers can be trusted.
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });

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

    // Production (Database__MigrateOnStartup=true): the schema is brought up to date before the bot and reminders start.
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DebitsDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied: {Count} ({Names})", pending.Count, string.Join(", ", pending));
    }

    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // The built Mini App (wwwroot, filled by the Docker image). In development Vite serves it and wwwroot is absent.
    var miniApp = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
    if (miniApp)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            // Vite puts a content hash into /assets file names: those never change. Everything else (locales) is revalidated.
            OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] =
                ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
        });
    }

    app.UseAuthentication();
    app.UseMiddleware<TenantMiddleware>();
    app.UseAuthorization();

    app.MapHealthChecks("/health");
    app.MapControllers();

    if (miniApp)
    {
        // Client-side routes (/customers/…, /debts?shop=…) get index.html; unknown API and bot paths stay 404.
        app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "not_found", extensions: new Dictionary<string, object?> { ["code"] = "not_found" }));
        app.MapFallback("/tg/{**path}", () => Results.NotFound());
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
        });
    }

    await app.RunAsync();
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
