using System.Text.Json;
using FieldOps.Application.Features.Branches;

namespace FieldOps.UnitTests.Branches;

/// <summary>BR-09 service ZIP code normalization and messages (AC-15).</summary>
public class ServicePostalCodesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void TryRead_NormalizesValidInputAndRejectsInvalidInputWithBr09Messages()
    {
        Assert.True(ServicePostalCodes.TryRead(
            Json("""[" 78701","78702","78701 ","","  ","SW1A 1AA","78702"]"""), required: true, out var codes, out _));
        Assert.Equal(["78701", "78702", "SW1A 1AA"], codes);

        Assert.True(ServicePostalCodes.TryRead(null, required: false, out var missing, out _));
        Assert.Empty(missing);

        var many = JsonSerializer.Serialize(Enumerable.Range(10000, 201).Select(n => n.ToString()));

        (string Json, bool Required, string Message)[] failures =
        [
            ("""["78701","bad!"]""", true, "Enter valid ZIP codes separated by commas."),
            ("""["1234567890123456789012345678901"]""", true, "Enter valid ZIP codes separated by commas."),
            ("""["a",1]""", true, "Enter a valid value."),
            ("\"78701\"", false, "Enter a valid value."),
            ("null", true, "Enter a valid value."),
            (many, false, "Enter up to 200 ZIP codes."),
        ];

        foreach (var (json, required, message) in failures)
        {
            Assert.False(ServicePostalCodes.TryRead(Json(json), required, out _, out var error));
            Assert.Equal(message, error);
        }
    }
}
