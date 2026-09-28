using FieldOps.Api.Controllers;
using FieldOps.Application.Features.Branches;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Maps a <see cref="DuplicateBranchCodeException"/> raised by a
/// unique-constraint race in branch create/update (a concurrent request that
/// also passed the pre-insert check) to 409 ValidationProblemDetails with a
/// field error on <c>code</c> (BR-04). Registered before
/// <see cref="GlobalExceptionHandler"/>, structural clone of
/// <see cref="DuplicateEmailExceptionHandler"/>. Shares its field key and
/// message with the controller's pre-insert-check path so both never drift.
/// </summary>
public sealed class DuplicateBranchCodeExceptionHandler(
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DuplicateBranchCodeException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        var problemDetails = new ValidationProblemDetails(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [BranchesController.DuplicateCodeKey] = [BranchesController.DuplicateCodeMessage],
            })
        {
            Status = StatusCodes.Status409Conflict,
        };

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });

        return true;
    }
}
