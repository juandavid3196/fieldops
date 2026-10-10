using FieldOps.Application.Features.OnlinePayments;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace FieldOps.Infrastructure.Pdf;

/// <summary>Lays out a composed <see cref="ReceiptPdfDocument"/> with PDFsharp-MigraDoc in the embedded Inter font (customer-invoice-payments BR-19).</summary>
public sealed class MigraDocReceiptPdfRenderer : IReceiptPdfRenderer
{
    public byte[] Render(ReceiptPdfDocument model)
    {
        var (document, section) = PdfSections.Start(model.Title, model.OrganizationName, model.OrganizationLines, model.Logo, model.PoweredBy);

        PdfSections.Text(section, model.Heading, 13, bold: true, color: PdfSections.Brand).Format.SpaceBefore = Unit.FromPoint(10);
        PdfSections.MetaTable(section, model.Meta);

        PdfSections.Heading(section, model.BillToHeading);

        foreach (var line in model.BillToLines)
        {
            PdfSections.Text(section, line, 9.5);
        }

        PdfSections.Heading(section, "Payment");
        AmountsTable(section, model.Amounts);

        PdfSections.Text(section, model.ThankYou, 10, bold: true).Format.SpaceBefore = Unit.FromPoint(16);

        return PdfSections.Render(document);
    }

    private static void AmountsTable(Section section, IReadOnlyList<(string Label, string Value, bool Emphasis)> amounts)
    {
        var table = section.AddTable();
        table.Format.SpaceBefore = Unit.FromPoint(4);
        table.AddColumn(Unit.FromCentimeter(10));
        table.AddColumn(Unit.FromCentimeter(3.5));
        table.AddColumn(Unit.FromCentimeter(3.5));

        foreach (var (label, value, emphasis) in amounts)
        {
            var row = table.AddRow();
            row.Cells[1].AddParagraph(label);
            row.Cells[2].AddParagraph(value).Format.Alignment = ParagraphAlignment.Right;

            if (emphasis)
            {
                row.Format.Font.Bold = true;
                row.Borders.Top.Color = PdfSections.Ink;
                row.Borders.Top.Width = 0.75;
            }
        }
    }
}
