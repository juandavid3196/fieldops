using System.Text.RegularExpressions;
using FieldOps.Application.Features.Quotes;

namespace FieldOps.UnitTests.Quotes;

/// <summary>quote-builder BR-27 (AC-17, AC-24): token shape, stored hash and expiry at the end of the local day.</summary>
public partial class QuoteAccessTokensTests
{
    [Fact]
    public void Generate_ReturnsUrlSafeTokenOf32BytesAndALowercaseHexHashDifferentFromIt()
    {
        var (raw, hash) = QuoteAccessTokens.Generate();
        var (other, _) = QuoteAccessTokens.Generate();

        Assert.Equal(43, raw.Length);
        Assert.Matches(Base64Url(), raw);
        Assert.NotEqual(raw, other);
        Assert.Equal(64, hash.Length);
        Assert.Matches(LowerHex(), hash);
        Assert.NotEqual(raw, hash);
        Assert.Equal(hash, QuoteAccessTokens.Hash(raw));
    }

    [Theory]
    [InlineData("America/Chicago", "2026-10-31", "2026-11-01T05:00:00+00:00")]
    [InlineData("America/Chicago", "2026-11-01", "2026-11-02T06:00:00+00:00")]
    [InlineData("America/Chicago", "2027-03-13", "2027-03-14T06:00:00+00:00")]
    [InlineData("America/Chicago", "2027-03-14", "2027-03-15T05:00:00+00:00")]
    [InlineData("UTC", "2026-12-31", "2027-01-01T00:00:00+00:00")]
    public void ExpiresAt_IsTheNextLocalMidnightAcrossDaylightSavingChanges(string zoneId, string validUntil, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        var expiresAt = QuoteAccessTokens.ExpiresAt(DateOnly.Parse(validUntil), zone);

        Assert.Equal(DateTimeOffset.Parse(expected), expiresAt);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex Base64Url();

    [GeneratedRegex("^[0-9a-f]+$")]
    private static partial Regex LowerHex();
}
