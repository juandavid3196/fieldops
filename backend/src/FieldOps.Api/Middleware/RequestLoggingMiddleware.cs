using System.Diagnostics;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Writes one log entry per request with method, path, status code, elapsed
/// time and trace id. Headers, bodies and exceptions are never logged here.
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var escaped = false;

        try
        {
            await next(context);
        }
        catch
        {
            escaped = true;
            throw;
        }
        finally
        {
            // A started response has already sent its status code.
            var statusCode = escaped && !context.Response.HasStarted
                ? StatusCodes.Status500InternalServerError
                : context.Response.StatusCode;

            var level = statusCode switch
            {
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ when context.Request.Path.StartsWithSegments("/health") => LogLevel.Debug,
                _ => LogLevel.Information,
            };

            // The query string is omitted on purpose because it may carry tokens.
            logger.Log(
                level,
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMs:0.0} ms (traceId {TraceId})",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                Activity.Current?.Id ?? context.TraceIdentifier);
        }
    }
}
