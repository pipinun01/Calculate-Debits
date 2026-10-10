using Debits.Api.Auth;
using Debits.Application.Common;
using Debits.Application.Tenancy;
using Debits.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace Debits.Api.Errors;

/// <summary><see cref="DomainException"/> → ProblemDetails with <c>code</c> (the Mini App maps it to an i18n key).</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problems, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = domain switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ForbiddenException => StatusCodes.Status403Forbidden,
            TenantMismatchException => StatusCodes.Status403Forbidden,
            ConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        logger.LogInformation("Domain error {Code} ({Status}) on {Method} {Path}: {Message}", domain.Code, status, context.Request.Method, context.Request.Path, domain.Message);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = ApiProblems.Create(status, domain.Code, domain.Message),
        });
    }
}
