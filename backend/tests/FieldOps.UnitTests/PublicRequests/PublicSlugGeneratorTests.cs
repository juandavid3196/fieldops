using FieldOps.Application.Features.PublicRequests;

namespace FieldOps.UnitTests.PublicRequests;

public class PublicSlugGeneratorTests
{
    // BR-21 steps 1-5.
    [Theory]
    [InlineData("Café Ñandú", "cafe-nandu")]
    [InlineData("Café & Co.", "cafe-co")]
    [InlineData("  --Acme   Field_Services!!  ", "acme-field-services")]
    [InlineData("ACME 24/7", "acme-24-7")]
    [InlineData("&&& ---", "organization")]
    [InlineData("", "organization")]
    [InlineData(null, "organization")]
    public void Normalize_AppliesTheNormalizationRules(string? name, string expected) =>
        Assert.Equal(expected, PublicSlugGenerator.Normalize(name));

    [Fact]
    public void Generate_UsesBaseWhenFreeOtherwiseTheLowestFreeSuffix()
    {
        Assert.Equal("cafe-co", PublicSlugGenerator.Generate("Café & Co.", new HashSet<string>()));
        Assert.Equal("cafe-co-2", PublicSlugGenerator.Generate("Café & Co.", new HashSet<string> { "cafe-co" }));
        Assert.Equal(
            "cafe-co-3",
            PublicSlugGenerator.Generate("Café & Co.", new HashSet<string> { "cafe-co", "cafe-co-2", "cafe-co-4" }));
    }

    [Fact]
    public void Generate_KeepsTotalLengthWithinSixtyIncludingSuffixAndTrimsTrailingHyphen()
    {
        // 57 "a", a hyphen and "bbb": the 60-character base ends in "-bb".
        var name = new string('a', 57) + " bbb";
        var first = PublicSlugGenerator.Generate(name, new HashSet<string>());

        Assert.Equal(new string('a', 57) + "-bb", first);

        // Truncating to 58 leaves a trailing hyphen, which is trimmed before the suffix.
        var second = PublicSlugGenerator.Generate(name, new HashSet<string> { first });

        Assert.Equal(new string('a', 57) + "-2", second);

        var long80 = new string('z', 80);
        var third = PublicSlugGenerator.Generate(long80, new HashSet<string> { new('z', 60) });

        Assert.Equal(new string('z', 58) + "-2", third);
        Assert.StartsWith(PublicSlugGenerator.LookupPrefix(long80), third, StringComparison.Ordinal);
    }
}
