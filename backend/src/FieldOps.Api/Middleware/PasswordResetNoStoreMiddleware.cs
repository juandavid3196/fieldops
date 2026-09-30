namespace FieldOps.Api.Middleware;

/// <summary>
/// Every response under <c>/password-resets</c> carries
/// <c>Cache-Control: no-store</c> (BR-16), including rate-limit and
/// media-type rejections that never reach the controller.
/// </summary>
public sealed class PasswordResetNoStoreMiddleware(RequestDelegate next)
{
    private const string PasswordResetsPath = "/password-resets";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(PasswordResetsPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
