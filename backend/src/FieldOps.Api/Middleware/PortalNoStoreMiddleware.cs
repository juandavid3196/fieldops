namespace FieldOps.Api.Middleware;

/// <summary>
/// Every response under <c>/portal</c> carries <c>Cache-Control: no-store</c> (customer portal BR-09), including rate-limit,
/// authentication and media-type rejections that never reach the controller. A binary endpoint overrides it with
/// <c>private, no-store</c>.
/// </summary>
public sealed class PortalNoStoreMiddleware(RequestDelegate next)
{
    private const string PortalPath = "/portal";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(PortalPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
