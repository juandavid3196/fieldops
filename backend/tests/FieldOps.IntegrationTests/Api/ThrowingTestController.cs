using Microsoft.AspNetCore.Mvc;

namespace FieldOps.IntegrationTests.Api;

/// <summary>
/// Test-only endpoint used to exercise the global exception handler.
/// </summary>
[ApiController]
[Route("test/throw")]
public sealed class ThrowingTestController : ControllerBase
{
    public const string ExceptionMessage = "Sensitive internal failure detail";

    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException(ExceptionMessage);
}
