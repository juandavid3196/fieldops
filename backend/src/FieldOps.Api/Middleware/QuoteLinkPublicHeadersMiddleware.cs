namespace FieldOps.Api.Middleware;

/// <summary>
/// Every response under <c>/public/quote-links</c> is uncached and leaves no referrer (customer-quote-approval BR-21):
/// <c>Cache-Control: no-store</c>, <c>Referrer-Policy: no-referrer</c> and <c>X-Content-Type-Options: nosniff</c>,
/// including rate-limit, size-limit and media-type rejections that never reach the controller. The headers are set when
/// the response starts, so an error handler that resets the response cannot drop them; an image or PDF endpoint
/// overrides the cache value with <c>private, no-store</c>.
/// </summary>
public sealed class QuoteLinkPublicHeadersMiddleware(RequestDelegate next)
{
    private const string QuoteLinksPath = "/public/quote-links";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(QuoteLinksPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(
                static state =>
                {
                    var headers = ((HttpContext)state).Response.Headers;

                    if (!headers.ContainsKey("Cache-Control") || !headers.CacheControl.ToString().Contains("no-store", StringComparison.Ordinal))
                    {
                        headers.CacheControl = "no-store";
                    }

                    headers["Referrer-Policy"] = "no-referrer";
                    headers.XContentTypeOptions = "nosniff";

                    return Task.CompletedTask;
                },
                context);
        }

        return next(context);
    }
}
