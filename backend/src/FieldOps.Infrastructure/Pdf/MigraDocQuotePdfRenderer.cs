using FieldOps.Application.Features.QuoteLinks;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace FieldOps.Infrastructure.Pdf;

/// <summary>Lays out a composed <see cref="QuotePdfDocument"/> with PDFsharp-MigraDoc (MIT) in the embedded Inter font (BR-19).</summary>
public sealed class MigraDocQuotePdfRenderer : IQuotePdfRenderer
{
    private static readonly Color Ink = Color.FromRgb(15, 23, 42);

    private static readonly Color Muted = Color.FromRgb(100, 116, 139);

    private static readonly Color Rule = Color.FromRgb(226, 232, 240);

    private static readonly Color Brand = Color.FromRgb(37, 99, 235);

    public byte[] Render(QuotePdfDocument model)
    {
        InterFontResolver.Register();

        var document = new Document();
        document.Info.Title = model.Title;
        document.Info.Author = model.OrganizationName;

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = InterFontResolver.FamilyName;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.2);

        var footer = section.Footers.Primary.AddParagraph(model.Copyright);
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Muted;
        footer.Format.Alignment = ParagraphAlignment.Center;
        var powered = section.Footers.Primary.AddParagraph(model.PoweredBy);
        powered.Format.Font.Size = 8;
        powered.Format.Font.Color = Muted;
        powered.Format.Alignment = ParagraphAlignment.Center;

        Text(section, model.OrganizationName, 17, bold: true);

        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            Text(section, model.Phone, 9.5, color: Muted);
        }

        var title = Text(section, model.Title, 13, bold: true, color: Brand);
        title.Format.SpaceBefore = Unit.FromPoint(10);

        if (model.ResponseStamp is not null)
        {
            Text(section, model.ResponseStamp, 11, bold: true);
        }

        MetaTable(section, model.Meta);

        Heading(section, "Scope");
        Text(section, model.Scope, 9.5);

        if (!string.IsNullOrWhiteSpace(model.CustomerMessage))
        {
            Text(section, model.CustomerMessage, 9.5, color: Muted);
        }

        if (model.ScopeOfWork.Count > 0)
        {
            Heading(section, "Scope of work");

            foreach (var item in model.ScopeOfWork)
            {
                var row = section.AddParagraph("• " + item);
                row.Format.LeftIndent = Unit.FromPoint(8);
                row.Format.FirstLineIndent = Unit.FromPoint(-8);
            }
        }

        Heading(section, "Price details");
        LinesTable(section, model.Lines);

        if (model.OptionalLines.Count > 0)
        {
            Heading(section, "Optional items");
            LinesTable(section, model.OptionalLines);
        }

        TotalsTable(section, model.Totals);

        if (!string.IsNullOrWhiteSpace(model.Terms))
        {
            Heading(section, "Terms");
            Text(section, model.Terms, 9.5);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);

        return stream.ToArray();
    }

    private static Paragraph Text(Section section, string text, double size, bool bold = false, Color? color = null)
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

    private static void Heading(Section section, string text)
    {
        var heading = Text(section, text, 11, bold: true);
        heading.Format.SpaceBefore = Unit.FromPoint(12);
        heading.Format.SpaceAfter = Unit.FromPoint(4);
        heading.Format.KeepWithNext = true;
    }

    private static void MetaTable(Section section, IReadOnlyList<QuotePdfMeta> meta)
    {
        var table = section.AddTable();
        table.Format.SpaceAfter = Unit.FromPoint(0);
        table.AddColumn(Unit.FromCentimeter(3.5));
        table.AddColumn(Unit.FromCentimeter(13.5));

        foreach (var item in meta)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(item.Label).Format.Font.Color = Muted;
            row.Cells[1].AddParagraph(item.Value);
        }
    }

    private static void LinesTable(Section section, IReadOnlyList<QuotePdfLine> lines)
    {
        var table = section.AddTable();
        table.Borders.Bottom.Color = Rule;
        table.Borders.Bottom.Width = 0.5;
        table.AddColumn(Unit.FromCentimeter(8));
        table.AddColumn(Unit.FromCentimeter(2.5));
        table.AddColumn(Unit.FromCentimeter(3));
        table.AddColumn(Unit.FromCentimeter(3.5));

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Format.Font.Color = Muted;
        header.Cells[0].AddParagraph("Description");
        header.Cells[1].AddParagraph("Qty");
        header.Cells[2].AddParagraph("Rate");
        header.Cells[3].AddParagraph("Amount").Format.Alignment = ParagraphAlignment.Right;
        header.Cells[2].Format.Alignment = ParagraphAlignment.Right;

        foreach (var line in lines)
        {
            var row = table.AddRow();
            row.KeepWith = 0;
            row.Cells[0].AddParagraph(line.Name);

            if (line.Description is not null)
            {
                var description = row.Cells[0].AddParagraph(line.Description);
                description.Format.Font.Size = 8.5;
                description.Format.Font.Color = Muted;
            }

            if (line.Status is not null)
            {
                var status = row.Cells[0].AddParagraph(line.Status);
                status.Format.Font.Size = 8.5;
                status.Format.Font.Bold = true;
            }

            row.Cells[1].AddParagraph(line.Quantity);
            row.Cells[2].AddParagraph(line.UnitPrice).Format.Alignment = ParagraphAlignment.Right;
            row.Cells[3].AddParagraph(line.Amount).Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void TotalsTable(Section section, IReadOnlyList<QuotePdfTotal> totals)
    {
        var table = section.AddTable();
        table.Format.SpaceBefore = Unit.FromPoint(10);
        table.AddColumn(Unit.FromCentimeter(10));
        table.AddColumn(Unit.FromCentimeter(3.5));
        table.AddColumn(Unit.FromCentimeter(3.5));

        foreach (var total in totals)
        {
            var row = table.AddRow();
            row.Cells[1].AddParagraph(total.Label);
            var amount = row.Cells[2].AddParagraph(total.Amount);
            amount.Format.Alignment = ParagraphAlignment.Right;

            if (total.IsGrandTotal)
            {
                row.Format.Font.Bold = true;
                row.Format.Font.Size = 11;
                row.Borders.Top.Color = Ink;
                row.Borders.Top.Width = 0.75;
            }
        }
    }
}
