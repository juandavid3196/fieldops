using FieldOps.Application.Features.OnlinePayments;
using MigraDoc.DocumentObjectModel;

namespace FieldOps.Infrastructure.Pdf;

/// <summary>
/// Lays out a composed <see cref="CompletionReportPdfDocument"/> (customer-invoice-payments BR-20). The document carries no
/// signature image, internal notes, incidents, materials, costs or time entries, so none can be rendered.
/// </summary>
public sealed class MigraDocCompletionReportPdfRenderer : ICompletionReportPdfRenderer
{
    public byte[] Render(CompletionReportPdfDocument model)
    {
        var (document, section) = PdfSections.Start(model.Title, model.OrganizationName, model.OrganizationLines, model.Logo, model.PoweredBy);

        PdfSections.Text(section, model.Heading, 13, bold: true, color: PdfSections.Brand).Format.SpaceBefore = Unit.FromPoint(10);
        PdfSections.MetaTable(section, model.Meta);

        if (!string.IsNullOrWhiteSpace(model.ServiceAddress))
        {
            PdfSections.Heading(section, "Service address");
            PdfSections.Text(section, model.ServiceAddress, 9.5);
        }

        if (!string.IsNullOrWhiteSpace(model.Summary))
        {
            PdfSections.Heading(section, "Completion summary");
            PdfSections.Text(section, model.Summary, 9.5);
        }

        if (model.Checklist.Count > 0)
        {
            PdfSections.Heading(section, "Checklist");
            var table = section.AddTable();
            table.Borders.Bottom.Color = PdfSections.Rule;
            table.Borders.Bottom.Width = 0.5;
            table.AddColumn(Unit.FromCentimeter(12));
            table.AddColumn(Unit.FromCentimeter(5));

            foreach (var item in model.Checklist)
            {
                var row = table.AddRow();
                row.Cells[0].AddParagraph(item.Label);
                row.Cells[1].AddParagraph(item.State).Format.Alignment = ParagraphAlignment.Right;
            }
        }

        if (model.SignOff.Count > 0)
        {
            PdfSections.Heading(section, "Customer sign-off");
            PdfSections.MetaTable(section, model.SignOff);
        }

        return PdfSections.Render(document);
    }
}
