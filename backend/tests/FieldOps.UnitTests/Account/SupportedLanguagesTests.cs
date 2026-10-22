using FieldOps.Application.Features.Account;

namespace FieldOps.UnitTests.Account;

public sealed class SupportedLanguagesTests
{
    [Theory]
    [InlineData("en", true, "en")]
    [InlineData("es", true, "es")]
    [InlineData("ES", false, "en")]
    [InlineData(" es", false, "en")]
    [InlineData("es-MX", false, "en")]
    [InlineData("", false, "en")]
    [InlineData(null, false, "en")]
    public void IsSupportedAndNormalize_AreOrdinalExactAndUntrimmed(string? code, bool supported, string normalized)
    {
        Assert.Equal(supported, SupportedLanguages.IsSupported(code));
        Assert.Equal(normalized, SupportedLanguages.Normalize(code));
    }
}
