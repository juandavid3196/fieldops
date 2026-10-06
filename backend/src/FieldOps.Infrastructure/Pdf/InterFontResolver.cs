using System.Reflection;
using PdfSharp.Fonts;

namespace FieldOps.Infrastructure.Pdf;

/// <summary>
/// The only font source of the quote PDF: Inter Regular and Bold embedded in this assembly (SIL OFL 1.1, see
/// <c>Pdf/Fonts/OFL.txt</c>). No installed or system font is ever read, so the output is the same on every host.
/// </summary>
internal sealed class InterFontResolver : IFontResolver
{
    public const string FamilyName = "Inter";

    private const string RegularFace = "Inter-Regular";

    private const string BoldFace = "Inter-Bold";

    private static readonly object Gate = new();

    private static bool registered;

    private static readonly Lazy<byte[]> Regular = new(() => Load("FieldOps.Pdf.Fonts.Inter-Regular.ttf"));

    private static readonly Lazy<byte[]> Bold = new(() => Load("FieldOps.Pdf.Fonts.Inter-Bold.ttf"));

    /// <summary>Registers the resolver in <see cref="GlobalFontSettings"/> once per process.</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new InterFontResolver();
            registered = true;
        }
    }

    // Every request maps to Inter; italic has no face and renders as the regular one.
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace);

    public byte[]? GetFont(string faceName) =>
        faceName switch
        {
            RegularFace => Regular.Value,
            BoldFace => Bold.Value,
            _ => null,
        };

    private static byte[] Load(string resourceName)
    {
        using var stream = typeof(InterFontResolver).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded font {resourceName} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
