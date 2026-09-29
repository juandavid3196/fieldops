using FieldOps.Application.Features.Users;
using FieldOps.Infrastructure;
using FieldOps.Infrastructure.Invitations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace FieldOps.UnitTests.Invitations;

/// <summary>AC-25: SMTP settings validation and adapter registration.</summary>
public class SmtpDeliveryRegistrationTests
{
    [Theory]
    [InlineData("", "1025", "no-reply@fieldops.test", false)]
    [InlineData("localhost", "0", "no-reply@fieldops.test", false)]
    [InlineData("localhost", "70000", "no-reply@fieldops.test", false)]
    [InlineData("localhost", "abc", "no-reply@fieldops.test", false)]
    [InlineData("localhost", "1025", "", false)]
    [InlineData("localhost", "1025", "not-an-address", false)]
    [InlineData("localhost", "1025", "no-reply@fieldops.test", true)]
    public void SmtpSettings_InvalidFailValidationAndValidResolveTheSmtpAdapterAsTheOnlyDelivery(
        string host, string port, string sender, bool valid)
    {
        var configuration = new FlatConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FieldOpsDatabase"] = "Host=localhost;Database=unused",
            ["Email:Smtp:Host"] = host,
            ["Email:Smtp:Port"] = port,
            ["Email:Smtp:EnableSsl"] = "false",
            ["Email:Smtp:SenderAddress"] = sender,
            ["Email:Smtp:SenderName"] = "FieldOps",
        });

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IInvitationDelivery));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SmtpSettings>>();

        if (!valid)
        {
            Assert.Throws<OptionsValidationException>(() => options.Value);
            return;
        }

        Assert.Equal(1025, options.Value.Port);
        Assert.Equal("SmtpInvitationDelivery", provider.GetRequiredService<IInvitationDelivery>().GetType().Name);
    }

    // Minimal key/value configuration: the project has no configuration builder package.
    private sealed class FlatConfiguration(IReadOnlyDictionary<string, string?> values, string path = "")
        : IConfigurationRoot, IConfigurationSection
    {
        public string Key => path.Split(':')[^1];

        public string Path => path;

        public string? Value
        {
            get => values.GetValueOrDefault(path);
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationProvider> Providers => [];

        public string? this[string key]
        {
            get => values.GetValueOrDefault(Combine(key));
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new FlatConfiguration(values, Combine(key));

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        public void Reload()
        {
        }

        private string Combine(string key) => path.Length == 0 ? key : $"{path}:{key}";
    }
}
