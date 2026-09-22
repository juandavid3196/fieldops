using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FieldOps.Api.HealthChecks;

/// <summary>
/// Writes a compact JSON health report. Descriptions, exceptions and data
/// are omitted so infrastructure details are never exposed.
/// </summary>
public static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var response = new HealthCheckResponse(
            report.Status.ToString(),
            report.TotalDuration.TotalMilliseconds,
            report.Entries
                .Select(entry => new HealthCheckEntryResponse(
                    entry.Key,
                    entry.Value.Status.ToString(),
                    entry.Value.Duration.TotalMilliseconds))
                .ToList());

        return context.Response.WriteAsJsonAsync(response);
    }
}

public sealed record HealthCheckResponse(
    string Status,
    double TotalDurationMs,
    IReadOnlyList<HealthCheckEntryResponse> Checks);

public sealed record HealthCheckEntryResponse(
    string Name,
    string Status,
    double DurationMs);
