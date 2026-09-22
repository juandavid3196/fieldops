using System.Net;
using Microsoft.Extensions.Hosting;

namespace FieldOps.IntegrationTests.Api;

public class OpenApiTests
{
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task Development_ExposesOpenApiAndSwaggerUi(string path)
    {
        await using var factory = FieldOpsApiFactory.Create(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task Production_DoesNotExposeOpenApiOrSwaggerUi(string path)
    {
        await using var factory = FieldOpsApiFactory.Create(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
