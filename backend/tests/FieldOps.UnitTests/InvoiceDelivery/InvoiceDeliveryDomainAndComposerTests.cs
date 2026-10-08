using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Domain.Invoices;
using FieldOps.Infrastructure.Pdf;

namespace FieldOps.UnitTests.InvoiceDelivery;

public class InvoiceDeliveryDomainAndComposerTests
{
    private static Invoice NewDraft() =>
        Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), 7, Guid.NewGuid(), Guid.NewGuid(), "USD", 100m, 8m, 108m, 108m, Guid.NewGuid(),
            new DateOnly(2030, 1, 20), new DateOnly(2030, 1, 20), 0m, "due_upon_receipt");

    // BR-09, BR-10, BR-14: the concurrency token moves only on a change, is strictly greater and has microsecond precision.
    [Fact]
    public void Invoice_DeliveryChangesTouchTheTokenOnlyWhenSomethingChangedAndSendFreezesTheDraft()
    {
        var invoice = NewDraft();
        var token = invoice.UpdatedAt;
        var now = invoice.UpdatedAt.AddSeconds(5).AddTicks(7);

        Assert.True(invoice.UpdateDelivery("net_15", new DateOnly(2030, 2, 4), "carla@example.com", "Hello", now));
        Assert.True(invoice.UpdatedAt > token);
        Assert.Equal(0, invoice.UpdatedAt.UtcTicks % 10);
        Assert.Equal(new DateOnly(2030, 1, 20), invoice.IssueDate);

        var saved = invoice.UpdatedAt;
        Assert.False(invoice.UpdateDelivery("net_15", new DateOnly(2030, 2, 4), "carla@example.com", "Hello", now.AddSeconds(5)));
        Assert.Equal(saved, invoice.UpdatedAt);

        // A clock that did not move still yields a different token.
        Assert.True(invoice.UpdateDelivery("net_30", new DateOnly(2030, 2, 19), "carla@example.com", "Hello", saved.AddDays(-1)));
        Assert.True(invoice.UpdatedAt > saved);

        invoice.MarkSent("net_30", new DateOnly(2030, 2, 19), "carla@example.com", "Hello", "{}", now.AddSeconds(30));

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.NotNull(invoice.SentAt);
        Assert.Equal("{}", invoice.CustomerSnapshot);
        Assert.Throws<InvalidOperationException>(() => invoice.UpdateDelivery("net_15", null, null, "x", now));
        Assert.Throws<InvalidOperationException>(() => invoice.MarkSent("net_15", null, "a@b.co", "x", "{}", now));
        Assert.Throws<ArgumentException>(() => NewDraft().MarkSent("net_15", null, " ", "x", "{}", now));
    }

    // BR-12, BR-16: the DRAFT flag follows the status, a logo only travels as PNG or JPEG, and the email encodes every value.
    [Fact]
    public void PdfDocumentAndEmail_FollowTheStatusAndEncodeValues()
    {
        var preview = new InvoicePreview(
            "INV-7",
            new DateOnly(2030, 1, 20),
            new DateOnly(2030, 2, 4),
            "net_15",
            "USD",
            "UTC",
            new InvoiceOrganizationView("Acme <b>", ["1 Main St"], "+1 555", "hi@acme.test", true),
            new InvoiceBillTo("Carla Customer", "carla@example.com", null, []),
            "1 Seed St, Austin TX",
            "WO-3",
            [new InvoiceLineView("Labor", "Detail", "2", "h", 100m, 0m, 200m)],
            new InvoiceTotalsView(200m, 20m, "Tax", 0m, 180m),
            null);
        var logo = new byte[] { 1, 2, 3 };

        var draft = InvoicePdfDocumentComposer.Compose(new InvoicePdfSource(preview, true, logo, "image/png"));
        var sent = InvoicePdfDocumentComposer.Compose(new InvoicePdfSource(preview, false, logo, "image/svg+xml"));

        Assert.True(draft.IsDraft);
        Assert.False(sent.IsDraft);
        Assert.Equal(logo, draft.Logo);
        Assert.Null(sent.Logo);
        Assert.Equal(["Subtotal", "Discount", "Tax", "Total due"], draft.Totals.Select(total => total.Label));
        Assert.Equal("2 h", draft.Lines[0].Quantity);
        Assert.Equal("—", draft.Lines[0].Tax);

        // The layout renders both states, with a real PNG logo, into a PDF; the draft carries the extra mark text.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
        var renderer = new MigraDocInvoicePdfRenderer();
        var draftPdf = renderer.Render(InvoicePdfDocumentComposer.Compose(new InvoicePdfSource(preview, true, png, "image/png")));
        var sentPdf = renderer.Render(InvoicePdfDocumentComposer.Compose(new InvoicePdfSource(preview, false, null, null)));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(draftPdf, 0, 4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(sentPdf, 0, 4));
        Assert.NotEqual(draftPdf.Length, sentPdf.Length);
        Assert.Equal("INV-7.pdf", InvoiceDeliveryRules.SafeFileName("INV-7"));
        Assert.Equal("INV-7.pdf", InvoiceDeliveryRules.SafeFileName("../INV-7\r\n"));

        var data = new InvoiceEmailData(
            Guid.NewGuid(), "carla@example.com", "Acme <b>\r\nBcc: x", "+1 555", "INV-7", 180m, "USD", new DateOnly(2030, 2, 4), "Hi <script>\nBye");
        var message = InvoiceEmailComposer.Compose(new InvoiceEmail(data, "https://app.test/invoices/view#token=abc"));

        Assert.Equal("carla@example.com", message.To);
        Assert.Equal("Acme <b>  Bcc: x: invoice INV-7", message.Subject);
        Assert.Contains("Invoice INV-7 · Total due 180.00 USD · Due Feb 4, 2030", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("View your invoice: https://app.test/invoices/view#token=abc", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Questions? Call us at +1 555.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Hi &lt;script&gt;\nBye", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", message.HtmlBody, StringComparison.Ordinal);
    }
}
