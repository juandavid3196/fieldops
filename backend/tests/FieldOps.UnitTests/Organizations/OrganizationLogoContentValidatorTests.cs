using System.Text;
using FieldOps.Application.Features.Organizations;

namespace FieldOps.UnitTests.Organizations;

/// <summary>BR-07/BR-08 logo content rules (AC-11, AC-12): type from content only, SVG hardening.</summary>
public class OrganizationLogoContentValidatorTests
{
    private const string Type = OrganizationLogoContentValidator.TypeMessage;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static byte[] Svg(string inner, string attributes = "") =>
        Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\"{attributes}>{inner}</svg>");

    public static IEnumerable<object?[]> Cases()
    {
        yield return [Png, "image/png", "image/png", null];
        yield return [Png, "IMAGE/PNG; charset=x", "image/png", null];
        yield return [Jpeg, "image/jpeg", "image/jpeg", null];
        yield return [Svg("<rect width=\"1\" height=\"1\" style=\"fill:url(#g)\"/><style>.a{fill:red}</style>"), "image/svg+xml", "image/svg+xml", null];
        yield return [Svg("<use href=\"#a\"/>"), "image/svg+xml", "image/svg+xml", null];

        yield return [Png, "image/jpeg", null, Type];
        yield return [Jpeg, "image/png", null, Type];
        yield return [Png, null, null, Type];
        yield return [Png, "text/plain", null, Type];
        yield return [Array.Empty<byte>(), "image/png", null, Type];
        yield return [Encoding.UTF8.GetBytes("plain text"), "image/png", null, Type];
        yield return [Svg("<rect/>"), "image/png", null, Type];
        yield return [new byte[OrganizationLogoContentValidator.MaxBytes + 1], "image/png", null, OrganizationLogoContentValidator.SizeMessage];

        yield return [Svg("<script>alert(1)</script>"), "image/svg+xml", null, Type];
        yield return [Svg("<SCRIPT/>"), "image/svg+xml", null, Type];
        yield return [Svg("<foreignObject/>"), "image/svg+xml", null, Type];
        yield return [Svg("<iframe/>"), "image/svg+xml", null, Type];
        yield return [Svg("<embed/>"), "image/svg+xml", null, Type];
        yield return [Svg("<object/>"), "image/svg+xml", null, Type];
        yield return [Svg("<rect onclick=\"x()\"/>"), "image/svg+xml", null, Type];
        yield return [Svg("<rect/>", " onload=\"x()\""), "image/svg+xml", null, Type];
        yield return [Svg("<image href=\"https://evil.example/a.png\"/>"), "image/svg+xml", null, Type];
        yield return [Svg("<image xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:href=\"data:image/png;base64,AA\"/>"), "image/svg+xml", null, Type];
        yield return [Svg("<style>@import url(#a);</style>"), "image/svg+xml", null, Type];
        yield return [Svg("<style>.a{fill:url(https://evil.example/x)}</style>"), "image/svg+xml", null, Type];
        yield return [Svg("<rect style=\"fill:url('http://evil')\"/>"), "image/svg+xml", null, Type];
        yield return [Svg("<rect style=\"fill:\\75rl(x)\"/>"), "image/svg+xml", null, Type];
        yield return [Svg("<?xml-stylesheet href=\"a.css\"?><rect/>"), "image/svg+xml", null, Type];
        yield return [Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x \"y\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>"), "image/svg+xml", null, Type];
        yield return [Encoding.UTF8.GetBytes("<svg><rect/></svg>"), "image/svg+xml", null, Type];
        yield return [Encoding.UTF8.GetBytes("<html xmlns=\"http://www.w3.org/2000/svg\"/>"), "image/svg+xml", null, Type];
        yield return [Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect>"), "image/svg+xml", null, Type];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check_ReturnsDetectedTypeOrExpectedMessage(
        byte[] content, string? declared, string? expectedType, string? expectedError)
    {
        var result = OrganizationLogoContentValidator.Check(content, declared);

        Assert.Equal(expectedType, result.ContentType);
        Assert.Equal(expectedError, result.Error);
    }
}
