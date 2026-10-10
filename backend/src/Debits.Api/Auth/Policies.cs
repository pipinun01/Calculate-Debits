using Debits.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Debits.Api.Auth;

public static class Policies
{
    /// <summary>Authenticated and acting inside a shop (<c>X-Shop-Id</c> resolved to an active membership).</summary>
    public const string ShopMember = "ShopMember";

    /// <summary>Shop member with the owner role.</summary>
    public const string OwnerOnly = "OwnerOnly";
}

public sealed class ShopMemberRequirement : IAuthorizationRequirement;

public sealed class OwnerRequirement : IAuthorizationRequirement;

public sealed class TenantAuthorizationHandler(ITenantContext tenant) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.Requirements)
        {
            switch (requirement)
            {
                case ShopMemberRequirement when tenant.ShopId is not null:
                    context.Succeed(requirement);
                    break;
                case OwnerRequirement when tenant.ShopId is not null && tenant.IsOwner:
                    context.Succeed(requirement);
                    break;
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>Turns authorization failures into ProblemDetails with a stable <c>code</c> instead of empty 401/403.</summary>
public sealed class ProblemAuthorizationResultHandler(ITenantContext tenant, IProblemDetailsService problems) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded)
        {
            await next(context);
            return;
        }

        if (result.Challenged || context.User.Identity?.IsAuthenticated != true)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, "auth.unauthorized", "Authentication required");
            return;
        }

        if (result.Forbidden && result.AuthorizationFailure is { } failure)
        {
            if (failure.FailedRequirements.OfType<ShopMemberRequirement>().Any() && tenant.ShopId is null)
            {
                await WriteAsync(context, StatusCodes.Status400BadRequest, "shop.required", "X-Shop-Id header is required");
                return;
            }

            if (failure.FailedRequirements.OfType<OwnerRequirement>().Any())
            {
                await WriteAsync(context, StatusCodes.Status403Forbidden, "forbidden.owner_only", "Only shop owners may do this");
                return;
            }
        }

        await _default.HandleAsync(next, context, policy, result);
    }

    private async Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = ApiProblems.Create(status, code, detail),
        });
    }
}

public static class ApiProblems
{
    public static ProblemDetails Create(int status, string code, string detail) => new()
    {
        Status = status,
        Title = code,
        Detail = detail,
        Extensions = { ["code"] = code },
    };
}
