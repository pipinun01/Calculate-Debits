using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Debits.Api.Errors;

/// <summary>Runs the FluentValidation validator registered for each bound action argument; failures → 400 with <c>code: validation</c>.</summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            if (context.HttpContext.RequestServices.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid)
            {
                continue;
            }

            foreach (var failure in result.Errors)
            {
                context.ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            context.Result = new BadRequestObjectResult(Problem(context.ModelState));
            return;
        }

        await next();
    }

    public static ValidationProblemDetails Problem(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState) => new(modelState)
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "validation",
        Extensions = { ["code"] = "validation" },
    };
}
