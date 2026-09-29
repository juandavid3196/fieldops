using System.Xml;

namespace FieldOps.Application.Features.Organizations;

/// <summary>Outcome of <see cref="OrganizationLogoContentValidator.Check"/>.</summary>
public readonly record struct OrganizationLogoCheck(string? ContentType, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Logo content rules (BR-07, BR-08): the type is derived from the bytes only
/// (PNG signature, JPEG signature, or a safe SVG) and must equal the declared
/// part type. Never trusts file names or extensions.
/// </summary>
public static class OrganizationLogoContentValidator
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public const string SizeMessage = "Choose a file of 2 MB or smaller.";

    public const string TypeMessage = "Choose a JPG, PNG or SVG file.";

    public const string PngType = "image/png";

    public const string JpegType = "image/jpeg";

    public const string SvgType = "image/svg+xml";

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> ForbiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "foreignObject",
        "iframe",
        "embed",
        "object",
    };

    public static OrganizationLogoCheck Check(byte[] content, string? declaredContentType)
    {
        if (content.Length > MaxBytes)
        {
            return new OrganizationLogoCheck(null, SizeMessage);
        }

        var declared = NormalizeDeclaredType(declaredContentType);
        var detected = Detect(content);

        if (detected is null || declared is null || !string.Equals(detected, declared, StringComparison.Ordinal))
        {
            return new OrganizationLogoCheck(null, TypeMessage);
        }

        return new OrganizationLogoCheck(detected, null);
    }

    private static string? NormalizeDeclaredType(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return null;
        }

        var semicolon = declared.IndexOf(';', StringComparison.Ordinal);
        var type = (semicolon >= 0 ? declared[..semicolon] : declared).Trim().ToLowerInvariant();

        return type is PngType or JpegType or SvgType ? type : null;
    }

    private static string? Detect(byte[] content)
    {
        if (content.Length == 0)
        {
            return null;
        }

        if (content.AsSpan().StartsWith(PngSignature))
        {
            return PngType;
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return JpegType;
        }

        return IsSafeSvg(content) ? SvgType : null;
    }

    private static bool IsSafeSvg(byte[] content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxBytes,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, settings);

            var sawRoot = false;
            var inStyleElement = false;

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.ProcessingInstruction:
                        return false;

                    case XmlNodeType.Element:
                        if (!sawRoot)
                        {
                            if (!string.Equals(reader.LocalName, "svg", StringComparison.Ordinal)
                                || !string.Equals(reader.NamespaceURI, SvgNamespace, StringComparison.Ordinal))
                            {
                                return false;
                            }

                            sawRoot = true;
                        }

                        if (ForbiddenElements.Contains(reader.LocalName) || !AreAttributesSafe(reader))
                        {
                            return false;
                        }

                        inStyleElement = string.Equals(reader.LocalName, "style", StringComparison.OrdinalIgnoreCase)
                            && !reader.IsEmptyElement;
                        break;

                    case XmlNodeType.EndElement:
                        inStyleElement = false;
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                        if (inStyleElement && !IsSafeStyle(reader.Value))
                        {
                            return false;
                        }

                        break;
                }
            }

            return sawRoot;
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException)
        {
            return false;
        }
    }

    private static bool AreAttributesSafe(XmlReader reader)
    {
        if (!reader.HasAttributes)
        {
            return true;
        }

        var safe = true;

        if (reader.MoveToFirstAttribute())
        {
            do
            {
                var name = reader.LocalName;
                var value = reader.Value;

                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    safe = false;
                }
                else if (string.Equals(name, "href", StringComparison.OrdinalIgnoreCase)
                    && !value.TrimStart().StartsWith('#'))
                {
                    safe = false;
                }
                else if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
                {
                    safe = IsSafeStyle(value);
                }
                else
                {
                    safe = HasOnlyLocalUrls(value);
                }
            }
            while (safe && reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        return safe;
    }

    private static bool IsSafeStyle(string css) =>
        !css.Contains('\\', StringComparison.Ordinal)
        && !css.Contains("@import", StringComparison.OrdinalIgnoreCase)
        && HasOnlyLocalUrls(css);

    // Every url( reference must point to a #fragment inside the document.
    private static bool HasOnlyLocalUrls(string value)
    {
        var index = 0;

        while ((index = value.IndexOf("url(", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            index += "url(".Length;

            var target = value[index..].TrimStart().TrimStart('"', '\'').TrimStart();

            if (!target.StartsWith('#'))
            {
                return false;
            }
        }

        return true;
    }
}
