namespace FieldOps.Api.Middleware;

/// <summary>
/// Every response under <c>/messages</c> carries <c>Cache-Control: no-store</c> (technician-messages BR-18), including
/// rate-limit, authentication and authorization rejections that never reach the controller.
/// </summary>
public sealed class MessagesNoStoreMiddleware(RequestDelegate next)
{
    private const string MessagesPath = "/messages";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(MessagesPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
