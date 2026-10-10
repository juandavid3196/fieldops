using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace FieldOps.Infrastructure.Pdf;

/// <summary>Layout pieces shared by the receipt and completion report renderers; the look follows <see cref="MigraDocInvoicePdfRenderer"/>.</summary>
internal static class PdfSections
{
    public static readonly Color Ink = Color.FromRgb(15, 23, 42);

    public static readonly Color Muted = Color.FromRgb(100, 116, 139);

    public static readonly Color Rule = Color.FromRgb(226, 232, 240);

    public static readonly Color Brand = Color.FromRgb(37, 99, 235);

    /// <summary>A Letter document in the embedded Inter font with the footer line and the organization header.</summary>
    public static (Document Document, Section Section) Start(
        string title, string organizationName, IReadOnlyList<string> organizationLines, byte[]? logo, string poweredBy)
    {
        InterFontResolver.Register();

        var document = new Document();
        document.Info.Title = title;
        document.Info.Author = organizationName;

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = InterFontResolver.FamilyName;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.2);

        var powered = section.Footers.Primary.AddParagraph(poweredBy);
        powered.Format.Font.Size = 8;
        powered.Format.Font.Color = Muted;
        powered.Format.Alignment = ParagraphAlignment.Center;

        if (logo is { Length: > 0 })
        {
            var image = section.AddImage("base64:" + Convert.ToBase64String(logo));
            image.LockAspectRatio = true;
            image.Height = Unit.FromCentimeter(1.6);
        }

        Text(section, organizationName, 17, bold: true);

        foreach (var line in organizationLines)
        {
            Text(section, line, 9.5, color: Muted);
        }

        return (document, section);
    }

    public static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);

        return stream.ToArray();
    }

    public static Paragraph Text(Section section, string text, double size, bool bold = false, Color? color = null)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.Font.Size = size;
        paragraph.Format.Font.Bold = bold;

        if (color is { } value)
        {
            paragraph.Format.Font.Color = value;
        }

        return paragraph;
    }

    public static void Heading(Section section, string text)
    {
        var heading = Text(section, text, 11, bold: true);
        heading.Format.SpaceBefore = Unit.FromPoint(12);
        heading.Format.SpaceAfter = Unit.FromPoint(4);
        heading.Format.KeepWithNext = true;
    }

    public static void MetaTable(Section section, IReadOnlyList<(string Label, string Value)> meta)
    {
        var table = section.AddTable();
        table.Format.SpaceAfter = Unit.FromPoint(0);
        table.AddColumn(Unit.FromCentimeter(4));
        table.AddColumn(Unit.FromCentimeter(13));

        foreach (var (label, value) in meta)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(label).Format.Font.Color = Muted;
            row.Cells[1].AddParagraph(value);
        }
    }
}
