using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace FieldOps.IntegrationTests.Api;

/// <summary>
/// Test-only: sets <c>RemoteIpAddress</c> from the X-Test-Client-IP header,
/// because the in-memory test server has no client address.
/// </summary>
public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Client-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var value)
                    && IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });

            next(app);
        };
}
