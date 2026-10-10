using Debits.Application.Abstractions;
using Debits.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Debits.Api.Auth;

/// <summary>
/// After authentication: fills <see cref="TenantContext"/> from the JWT subject and the optional <c>X-Shop-Id</c> header.
/// A header naming a shop the user is not an active member of is a 403; endpoints that need a shop enforce it via <see cref="Policies.ShopMember"/>.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public const string ShopHeader = "X-Shop-Id";

    public async Task InvokeAsync(HttpContext context, TenantContext tenant, IDebitsDbContext db, IProblemDetailsService problems)
    {
        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.GetUserId() : null;
        if (userId is not { } uid)
        {
            await next(context);
            return;
        }

        var ct = context.RequestAborted;
        if (context.Request.Headers.TryGetValue(ShopHeader, out var header) && header.Count > 0)
        {
            if (!Guid.TryParse(header.ToString(), out var shopId))
            {
                await WriteAsync(context, problems, StatusCodes.Status400BadRequest, "shop.invalid", "X-Shop-Id must be a GUID");
                return;
            }

            var membership = await db.ShopMembers
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(m => m.UserId == uid && m.ShopId == shopId && m.IsActive)
                .Select(m => new { m.Id, m.Role, m.User.Language })
                .SingleOrDefaultAsync(ct);
            if (membership is null)
            {
                await WriteAsync(context, problems, StatusCodes.Status403Forbidden, "shop.not_member", "You are not a member of this shop");
                return;
            }

            tenant.Set(shopId, membership.Id, uid, membership.Role, membership.Language);
        }
        else
        {
            var language = await db.Users.Where(u => u.Id == uid).Select(u => (Domain.Entities.AppLanguage?)u.Language).SingleOrDefaultAsync(ct);
            if (language is null)
            {
                await WriteAsync(context, problems, StatusCodes.Status401Unauthorized, "auth.user_not_found", "User no longer exists");
                return;
            }

            tenant.Set(null, null, uid, null, language.Value);
        }

        await next(context);
    }

    private static async Task WriteAsync(HttpContext context, IProblemDetailsService problems, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = ApiProblems.Create(status, code, detail),
        });
    }
}
