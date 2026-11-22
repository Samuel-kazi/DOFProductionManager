using DOF.Application.Common.Exceptions;
using DOF.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOF.Api.Middleware;

/// <summary>Translates application exceptions into ProblemDetails responses. Unknown errors are logged, never swallowed.</summary>
public sealed class DofExceptionHandler(IProblemDetailsService problemDetails, ILogger<DofExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, "Conflict", e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict",
                "This record was modified by someone else. Reload the latest version and try again."),
            AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, "Authentication failed", e.Message),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
