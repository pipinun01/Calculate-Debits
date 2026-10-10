using System.Text.Json;
using System.Text.Json.Serialization;
using Debits.Api.Auth;
using Debits.Api.Errors;
using Debits.Api.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Debits.Api;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Used when <c>JWT_SECRET</c> is empty in Development; production refuses to start without a real one.</summary>
    public const string DevelopmentSecret = "debits-development-only-secret-do-not-use-in-prod";

    public static IServiceCollection AddDebitsApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddControllers(o => o.Filters.Add<ValidationFilter>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new AppLanguageJsonConverter());
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            })
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx => new BadRequestObjectResult(ValidationFilter.Problem(ctx.ModelState)));

        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddProblemDetails();
        ValidatorOptions.Global.LanguageManager.Enabled = false; // English messages regardless of the server culture; clients use `code`
        services.AddValidatorsFromAssemblyContaining<ValidationFilter>(includeInternalTypes: true);

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.PostConfigure<JwtOptions>(o =>
        {
            if (!string.IsNullOrWhiteSpace(o.Secret))
            {
                return;
            }

            o.Secret = environment.IsDevelopment()
                ? DevelopmentSecret
                : throw new InvalidOperationException("JWT_SECRET is required outside Development");
        });
        services.AddSingleton<JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((o, jwt, clock) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = JwtTokenService.Key(jwt.Value.Secret),
                    ValidateIssuerSigningKey = true,
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    // Our TimeProvider, not DateTime.UtcNow: tests run on a fake clock.
                    LifetimeValidator = (_, expires, _, p) => expires is { } e && e.ToUniversalTime() + p.ClockSkew > clock.GetUtcNow().UtcDateTime,
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = ctx.HttpContext,
                            ProblemDetails = ApiProblems.Create(StatusCodes.Status401Unauthorized, "auth.unauthorized", ctx.ErrorDescription ?? "Authentication required"),
                        });
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ShopMember, p => p.RequireAuthenticatedUser().AddRequirements(new ShopMemberRequirement()))
            .AddPolicy(Policies.OwnerOnly, p => p.RequireAuthenticatedUser().AddRequirements(new ShopMemberRequirement(), new OwnerRequirement()));
        services.AddScoped<IAuthorizationHandler, TenantAuthorizationHandler>();
        services.Replace(ServiceDescriptor.Scoped<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>());

        return services;
    }
}
