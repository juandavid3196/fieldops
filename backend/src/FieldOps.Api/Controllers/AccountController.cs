using System.Globalization;
using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The signed-in user's own account: profile, photo and password (account
/// settings FR-05, FR-08, FR-11). No endpoint takes a user or organization
/// identifier; identity comes only from the validated session.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per
/// <see cref="SessionsController"/> (JSON) and <see cref="OrganizationLogoController"/>
/// (multipart). A body over the size limit surfaces as a
/// <see cref="BadHttpRequestException"/> mapped to 413 by
/// <c>BadHttpRequestExceptionHandler</c>; a wrong content type gets 415.
/// </remarks>
[Route("account")]
[Authorize]
[NoStore]
public sealed class AccountController(
    GetAccountProfileHandler getProfileHandler,
    UpdateAccountProfileHandler updateProfileHandler,
    GetUserPhotoHandler getPhotoHandler,
    UploadUserPhotoHandler uploadPhotoHandler,
    RemoveUserPhotoHandler removePhotoHandler,
    ChangePasswordHandler changePasswordHandler,
    UpdateLanguageHandler updateLanguageHandler,
    ILogger<AccountController> logger) : ControllerBase
{
    public const int MaxJsonBodyBytes = 4 * 1024;

    public const int MaxPhotoRequestBodyBytes = 6 * 1024 * 1024;

    public const string FileKey = "file";

    private const string PhotoContentSecurityPolicy = "default-src 'none'; sandbox";

    [HttpGet("profile")]
    [ProducesResponseType<AccountProfileView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var profile = await getProfileHandler.HandleAsync(
            ticket.UserId, ticket.OrganizationId, ticket.MembershipId, cancellationToken);

        return profile is null ? Unauthorized() : Ok(profile);
    }

    [HttpPut("profile")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<AccountProfileView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateAccountProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        // Empty or JSON null body, malformed JSON or a type mismatch: keyless 400;
        // a missing Content-Type fails formatter selection: 415.
        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        // Only these three fields are read; any identifier the client sends is ignored.
        var result = await updateProfileHandler.HandleAsync(
            ticket.UserId,
            ticket.OrganizationId,
            ticket.MembershipId,
            GetClientIpAddress(),
            new UpdateAccountProfileCommand(request.FirstName, request.LastName, request.Phone),
            cancellationToken);

        switch (result)
        {
            case UpdateAccountProfileResult.Succeeded succeeded:
                return Ok(succeeded.Profile);

            case UpdateAccountProfileResult.Invalid invalid:
                return FieldProblem(invalid.Errors);

            case UpdateAccountProfileResult.NotFound:
                return Unauthorized();

            default:
                throw new InvalidOperationException("Unknown update account profile result.");
        }
    }

    [HttpPut("language")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<UpdateLanguageView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UpdateLanguage(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateLanguageRequest request,
        CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        // Only the language is read; any identifier the client sends is ignored.
        var result = await updateLanguageHandler.HandleAsync(
            ticket.UserId,
            ticket.OrganizationId,
            GetClientIpAddress(),
            new UpdateLanguageCommand(request.LanguageText),
            cancellationToken);

        return result switch
        {
            UpdateLanguageResult.Succeeded succeeded => Ok(new UpdateLanguageView(succeeded.PreferredLanguage)),
            UpdateLanguageResult.Invalid invalid => FieldProblem(invalid.Errors),
            UpdateLanguageResult.NotFound => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown update language result."),
        };
    }

    [HttpGet("photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var photo = await getPhotoHandler.HandleAsync(ticket.UserId, cancellationToken);

        if (photo is null)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = PhotoContentSecurityPolicy;
        Response.Headers.ContentDisposition = "inline";

        return File(photo.Content, photo.ContentType);
    }

    [HttpPut("photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxPhotoRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPhotoRequestBodyBytes)]
    [ProducesResponseType<UserPhotoMetadata>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadPhoto(CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        // The form is read here, not by model binding, so an oversized body
        // (BadHttpRequestException, 413) reaches BadHttpRequestExceptionHandler
        // while a malformed multipart body is a plain 400.
        IFormFile? file;

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile(FileKey);
        }
        catch (InvalidDataException)
        {
            return BadRequest();
        }

        if (file is null)
        {
            return FileProblem(UserPhotoContentValidator.TypeMessage);
        }

        // Copy at most limit + 1 bytes so an oversized part is detected
        // without buffering more than the validator needs.
        var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            await CopyAtMostAsync(source, buffer, UserPhotoContentValidator.MaxBytes + 1L, cancellationToken);
        }

        var result = await uploadPhotoHandler.HandleAsync(
            ticket.UserId,
            ticket.OrganizationId,
            GetClientIpAddress(),
            buffer.ToArray(),
            file.ContentType,
            cancellationToken);

        return result switch
        {
            UploadUserPhotoResult.Succeeded succeeded => Ok(succeeded.Photo),
            UploadUserPhotoResult.Invalid invalid => FileProblem(invalid.Message),
            _ => throw new InvalidOperationException("Unknown upload user photo result."),
        };
    }

    [HttpDelete("photo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemovePhoto(CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        await removePhotoHandler.HandleAsync(
            ticket.UserId, ticket.OrganizationId, GetClientIpAddress(), cancellationToken);

        return NoContent();
    }

    [HttpPost("password")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        // BR-05 step 1: the failure limit is checked before the body is looked at.
        if (changePasswordHandler.GetRetryAfter(ticket.UserId) is { } retryAfter)
        {
            return TooManyRequests(retryAfter);
        }

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        var session = SessionCookieEvents.GetValidatedSession(HttpContext);

        if (session is null)
        {
            return Unauthorized();
        }

        var result = await changePasswordHandler.HandleAsync(
            ticket.UserId,
            ticket.OrganizationId,
            GetClientIpAddress(),
            new ChangePasswordCommand(request.CurrentPassword, request.NewPassword),
            cancellationToken);

        switch (result)
        {
            case ChangePasswordResult.Succeeded succeeded:
                // The new password is committed. Re-issue this session's cookie with
                // a sign-in time not earlier than password_changed_at (BR-05); if that
                // fails there is no rollback: the generic 500 follows and the old
                // cookie is rejected at its next request.
                try
                {
                    await SessionCookieIssuer.SignInAsync(
                        HttpContext, session, ticket.MembershipId, succeeded.ChangedAt, ticket.RememberMe);
                }
                catch (Exception ex)
                {
                    logger.LogError("Session cookie re-issue failed after a password change: {ExceptionType}", ex.GetType().Name);
                    throw;
                }

                return NoContent();

            case ChangePasswordResult.Invalid invalid:
                return FieldProblem(invalid.Errors);

            case ChangePasswordResult.Throttled throttled:
                return TooManyRequests(throttled.RetryAfter);

            case ChangePasswordResult.NotFound:
                return Unauthorized();

            default:
                throw new InvalidOperationException("Unknown change password result.");
        }
    }

    private static async Task CopyAtMostAsync(
        Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;

        while (total < limit)
        {
            var toRead = (int)Math.Min(chunk.Length, limit - total);
            var read = await source.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken);

            if (read == 0)
            {
                return;
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }
    }

    private StatusCodeResult TooManyRequests(TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));
        Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        return StatusCode(StatusCodes.Status429TooManyRequests);
    }

    private IActionResult FieldProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }

        return ValidationProblem(ModelState);
    }

    private IActionResult FileProblem(string message)
    {
        ModelState.AddModelError(FileKey, message);

        return ValidationProblem(ModelState);
    }

    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    private bool HasUnsupportedContentType() =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException);
}

/// <summary>
/// Sets <c>Cache-Control: no-store</c> on every response of the account
/// controller (BR-11), including a 413 written by the exception handler after
/// the response headers were cleared.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class NoStoreAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var response = context.HttpContext.Response;

        response.OnStarting(
            static state =>
            {
                ((HttpResponse)state).Headers.CacheControl = "no-store";

                return Task.CompletedTask;
            },
            response);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
