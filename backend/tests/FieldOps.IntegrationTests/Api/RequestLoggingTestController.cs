using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.IntegrationTests.Api;

/// <summary>
/// Test-only endpoints used to exercise request logging.
/// </summary>
[ApiController]
[Route("test/logging")]
public sealed class RequestLoggingTestController : ControllerBase
{
    [HttpGet("ok")]
    public IActionResult GetOk() => Ok();

    [HttpGet("throw-after-start")]
    public async Task ThrowAfterResponseStarted()
    {
        Response.StatusCode = StatusCodes.Status200OK;
        await Response.WriteAsync("partial");
        await Response.Body.FlushAsync();

        throw new InvalidOperationException("Failure after the response started");
    }
}
