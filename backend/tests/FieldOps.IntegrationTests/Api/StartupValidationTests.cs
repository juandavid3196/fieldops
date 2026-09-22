using FieldOps.Api.Configuration;
using Microsoft.Extensions.Options;

namespace FieldOps.IntegrationTests.Api;

public class StartupValidationTests
{
    [Fact]
    public async Task Startup_WithoutConnectionString_Fails()
    {
        await using var factory = FieldOpsApiFactory.Create(connectionString: null);

        var exception = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("FieldOpsDatabase", exception.Message);
    }

    [Fact]
    public async Task Startup_WithoutCorsOrigins_Fails()
    {
        await using var factory = FieldOpsApiFactory.CreateWithoutCorsOrigins();

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", exception.Message);
    }

    [Fact]
    public async Task Startup_WithWildcardCorsOrigin_Fails()
    {
        await using var factory = FieldOpsApiFactory.Create(allowedOrigins: "*");

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.fieldops.test")]
    [InlineData("https://app.fieldops.test/")]
    [InlineData("https://app.fieldops.test/path")]
    [InlineData("ftp://app.fieldops.test")]
    [InlineData("app.fieldops.test")]
    [InlineData(" ")]
    public void CorsValidator_RejectsInvalidOrigins(string origin)
    {
        var result = new CorsSettingsValidator().Validate(
            null,
            new CorsSettings { AllowedOrigins = [origin] });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("https://app.fieldops.test")]
    public void CorsValidator_AcceptsAbsoluteOrigins(string origin)
    {
        var result = new CorsSettingsValidator().Validate(
            null,
            new CorsSettings { AllowedOrigins = [origin] });

        Assert.True(result.Succeeded);
    }
}
