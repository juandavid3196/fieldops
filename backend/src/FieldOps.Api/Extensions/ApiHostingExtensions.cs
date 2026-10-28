using System.Net;
using FieldOps.Api.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Extensions;

/// <summary>Deployment concerns: proxy trust and Data Protection keys.</summary>
public static class ApiHostingExtensions
{
    public static IServiceCollection AddApiHosting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(ForwardedHeadersSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();

        services
            .AddOptions<DataProtectionSettings>()
            .Bind(configuration.GetSection(DataProtectionSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DataProtectionSettings>, DataProtectionSettingsValidator>();

        // Only X-Forwarded-For and -Proto are processed: links are built from
        // the configured frontend origin, so the Host header is never trusted.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
            {
                var value = settings.Value;

                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = value.ForwardLimit;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();

                if (value.TrustAllProxies)
                {
                    // Explicit opt-in: the default loopback-only lists are emptied above.
                    return;
                }

                foreach (var proxy in value.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in value.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        var keys = configuration.GetSection(DataProtectionSettings.SectionName).Get<DataProtectionSettings>();
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(string.IsNullOrWhiteSpace(keys?.ApplicationName)
                ? DataProtectionSettings.DefaultApplicationName
                : keys.ApplicationName);

        if (!string.IsNullOrWhiteSpace(keys?.KeysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keys.KeysPath));
        }

        return services;
    }

    public static bool UsesForwardedHeaders(this IConfiguration configuration) =>
        configuration.GetSection(ForwardedHeadersSettings.SectionName).GetValue<bool>(nameof(ForwardedHeadersSettings.Enabled));
}
