using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.Users;
using FieldOps.Infrastructure;
using FieldOps.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace FieldOps.UnitTests.Email;

/// <summary>AC-14: email settings validation and adapter/invitation registration.</summary>
public class EmailRegistrationTests
{
    private const string Secret = "re_super_secret_key_value";

    [Theory]
    [InlineData("", "no-reply@fieldops.test", "localhost", "1025", null, "Development", "Email:Provider")]
    [InlineData("Postmark", "no-reply@fieldops.test", "localhost", "1025", null, "Development", "Email:Provider")]
    [InlineData("Smtp", "not-an-address", "localhost", "1025", null, "Development", "Email:SenderAddress")]
    [InlineData("Resend", "no-reply@fieldops.test", "", "0", "", "Development", "Email:Resend:ApiKey")]
    [InlineData("Smtp", "no-reply@fieldops.test", "", "1025", null, "Development", "Email:Smtp:Host")]
    [InlineData("Smtp", "no-reply@fieldops.test", "localhost", "abc", null, "Development", "Email:Smtp:Port")]
    [InlineData("Smtp", "no-reply@fieldops.test", "localhost", "1025", null, "Production", "must be 'Resend' in Production")]
    [InlineData("Smtp", "no-reply@fieldops.test", "localhost", "1025", null, "Development", null)]
    [InlineData("Resend", "no-reply@fieldops.test", "", "0", Secret, "Production", null)]
    public async Task EmailSettings_InvalidFailOnStartNamingOnlyTheKeyAndValidResolveTheProviderAdapterAndInvitationDelivery(
        string provider,
        string sender,
        string host,
        string port,
        string? apiKey,
        string environment,
        string? expectedFailure)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:FieldOpsDatabase"] = "Host=localhost;Database=unused",
            ["Email:Provider"] = provider,
            ["Email:SenderAddress"] = sender,
            ["Email:SenderName"] = "FieldOps",
            ["Email:Smtp:Host"] = host,
            ["Email:Smtp:Port"] = port,
            ["Email:Smtp:EnableSsl"] = "false",
            ["Email:Resend:ApiKey"] = apiKey,
        };

        var services = BuildServices(values, environment);
        using var provider1 = services.BuildServiceProvider();
        var options = provider1.GetRequiredService<IOptions<EmailSettings>>();

        if (expectedFailure is not null)
        {
            var failure = Assert.Throws<OptionsValidationException>(() => options.Value);

            Assert.Contains(expectedFailure, string.Join(' ', failure.Failures), StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(sender, failure.Message, StringComparison.Ordinal);

            return;
        }

        var expectedAdapter = provider == "Resend" ? "ResendEmailSender" : "SmtpEmailSender";
        Assert.Equal(expectedAdapter, provider1.GetRequiredService<IEmailSender>().GetType().Name);
        Assert.Equal("InvitationDelivery", provider1.GetRequiredService<IInvitationDelivery>().GetType().Name);

        // The invitation delivery composes the message and sends it through the port.
        var recording = new RecordingEmailSender();
        var replaced = BuildServices(values, environment);
        replaced.AddSingleton<IEmailSender>(recording);
        using var replacedProvider = replaced.BuildServiceProvider();

        await replacedProvider.GetRequiredService<IInvitationDelivery>().SendAsync(
            new InvitationDeliveryMessage(
                "ivy@example.com",
                "Ivy",
                "Acme",
                "Olivia Owner",
                "Dispatcher",
                new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
                "https://app.fieldops.test/auth/invitation#token=abc"),
            CancellationToken.None);

        var message = Assert.Single(recording.Messages);
        Assert.Equal("ivy@example.com", message.To);
        Assert.Equal("Olivia Owner invited you to join Acme on FieldOps", message.Subject);
        Assert.Contains("Accept invitation: https://app.fieldops.test/auth/invitation#token=abc", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("This invitation expires on October 7, 2026 (UTC).", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.fieldops.test/auth/invitation#token=abc\"", message.HtmlBody, StringComparison.Ordinal);
    }

    private static IServiceCollection BuildServices(IReadOnlyDictionary<string, string?> values, string environment)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environment));
        services.AddInfrastructure(new FlatConfiguration(values));

        return services;
    }

    internal sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "FieldOps.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
