using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? UserId => int.TryParse(Principal?.FindFirstValue("sub"), out var id) ? id : null;
    public string Username => Principal?.FindFirstValue("unique_name") ?? "anonymous";
    public bool IsManager => Principal?.FindFirstValue("role") is nameof(UserRole.Admin) or nameof(UserRole.Manager);
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Runs the FluentValidation validator for each bound argument before the action executes.</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values.OfType<object>())
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => JsonName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Title = "Please fix the highlighted fields.",
                Status = StatusCodes.Status400BadRequest,
                Detail = errors.Values.First()[0],
            });
            return;
        }
        await next();
    }

    private static string JsonName(string property) =>
        string.IsNullOrEmpty(property) ? "request" : char.ToLowerInvariant(property[0]) + property[1..];
}

/// <summary>Maps exceptions to RFC 7807 ProblemDetails. Stack traces stay on the server.</summary>
public sealed class ProblemExceptionHandler(ILogger<ProblemExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (404, ex.Message),
            ConflictException => (409, ex.Message),
            UniqueConstraintException => (409, "This record already exists."),
            TooManyRequestsException => (429, ex.Message),
            ForbiddenException => (403, ex.Message),
            BadRequestException or DomainException => (400, ex.Message),
            UnauthorizedAccessException => (401, ex.Message),
            DbUpdateConcurrencyException => (409, "Someone else changed this record at the same time. Reload and try again."),
            DbUpdateException => (409, "The change conflicts with existing data."),
            OperationCanceledException => (499, "Request cancelled."),
            _ => (500, "Something went wrong on our side. The error has been logged."),
        };

        if (status >= 500) log.LogError(ex, "Unhandled exception on {Method} {Path}", http.Request.Method, http.Request.Path);
        else if (status == 409) log.LogWarning("Conflict on {Method} {Path}: {Title}", http.Request.Method, http.Request.Path, title);

        var problem = new ProblemDetails { Status = status, Title = title, Instance = http.Request.Path };
        problem.Extensions["traceId"] = http.TraceIdentifier;
        if (env.IsDevelopment() && status >= 500) problem.Detail = ex.ToString();

        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
        return true;
    }
}

public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var headers = ctx.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        var path = ctx.Request.Path;
        headers.ContentSecurityPolicy = path.StartsWithSegments("/api")
            ? "default-src 'none'; frame-ancestors 'none'"
            : path.StartsWithSegments("/swagger")
                ? "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'"
                : "default-src 'self'; img-src 'self' data:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; frame-ancestors 'none'";
        await next();
    });
}
