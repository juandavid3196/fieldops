using FieldOps.Api.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FieldOps.IntegrationTests.Api;

public class HostingConfigurationTests
{
    [Theory]
    [InlineData(false, new string[0], new string[0], false, 1, true)]
    [InlineData(true, new string[0], new string[0], false, 1, false)]
    [InlineData(true, new string[0], new string[0], true, 1, true)]
    [InlineData(true, new[] { "10.0.0.1" }, new string[0], true, 1, false)]
    [InlineData(true, new string[0], new[] { "100.64.0.0/10" }, false, 1, true)]
    [InlineData(true, new[] { "not-an-ip" }, new string[0], false, 1, false)]
    [InlineData(true, new string[0], new[] { "10.0.0.0/99" }, false, 1, false)]
    [InlineData(true, new string[0], new[] { "10.0.0.0/8" }, false, 9, false)]
    public void ForwardedHeadersValidator_RequiresExplicitTrust(
        bool enabled,
        string[] proxies,
        string[] networks,
        bool trustAll,
        int limit,
        bool valid)
    {
        var result = new ForwardedHeadersSettingsValidator().Validate(
            null,
            new ForwardedHeadersSettings
            {
                Enabled = enabled,
                KnownProxies = proxies,
                KnownNetworks = networks,
                TrustAllProxies = trustAll,
                ForwardLimit = limit,
            });

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void DataProtectionValidator_RequiresAWritableKeyDirectoryInProduction()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fieldops-keys-{Guid.NewGuid():N}");
        var blocker = Path.Combine(Path.GetTempPath(), $"fieldops-file-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "x");

        try
        {
            var production = new DataProtectionSettingsValidator(new FakeEnvironment(Environments.Production));
            var development = new DataProtectionSettingsValidator(new FakeEnvironment(Environments.Development));

            Assert.True(production.Validate(null, new DataProtectionSettings()).Failed);
            Assert.True(development.Validate(null, new DataProtectionSettings()).Succeeded);
            Assert.True(production.Validate(null, new DataProtectionSettings { KeysPath = directory }).Succeeded);
            Assert.True(production.Validate(null, new DataProtectionSettings { KeysPath = Path.Combine(blocker, "keys") }).Failed);
            Assert.True(production.Validate(null, new DataProtectionSettings { KeysPath = directory, ApplicationName = " " }).Failed);
        }
        finally
        {
            File.Delete(blocker);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Startup_InProductionWithoutDataProtectionKeys_Fails()
    {
        await using var factory = new FieldOpsApiFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FieldOpsDatabase"] = FieldOpsApiFactory.UnreachableConnectionString,
                ["Cors:AllowedOrigins:0"] = FieldOpsApiFactory.AllowedOrigin,
                ["DataProtection:KeysPath"] = string.Empty,
            });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("DataProtection:KeysPath", exception.Message);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
