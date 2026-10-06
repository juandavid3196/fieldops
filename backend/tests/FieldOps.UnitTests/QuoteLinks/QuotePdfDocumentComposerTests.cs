using System.Text;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Infrastructure.Pdf;

namespace FieldOps.UnitTests.QuoteLinks;

public class QuotePdfDocumentComposerTests
{
    private static readonly Guid Chosen = Guid.NewGuid();

    private static readonly Guid Skipped = Guid.NewGuid();

    private static PublicQuote Quote(PublicResponse? response, string status = "sent") =>
        new(
            new PublicOrganization("Acme Plumbing", "+1 555 010 0100", false),
            new PublicQuoteInfo("Q-2036", 2, status, new DateOnly(2030, 5, 6), new DateOnly(2030, 6, 5), "Drain cleaning", "Thanks for choosing us", "Due on completion"),
            new PublicCustomer("Carla Customer", "1 Seed St, Austin TX"),
            ["Drain cleaning"],
            [
                new PublicLine(null, "Drain cleaning", "Main line", 2m, "hr", 95m, 190m, false),
                new PublicLine(Chosen, "Camera inspection", string.Empty, 1m, "unit", 120m, 120m, true),
                new PublicLine(Skipped, "Extra trap", string.Empty, 1m, "unit", 40m, 40m, true),
            ],
            new PublicTotals(190m, 10m, "Tax (8.25%)", 14.85m, 194.85m, "USD"),
            [],
            new PublicProgress(new DateOnly(2030, 5, 1), null),
            null,
            response);

    // AC-17: optional items are marked per state, totals follow the response, and the stamp is the BR-19 text.
    [Fact]
    public void Compose_MarksOptionalItemsAndTotalsPerResponseState()
    {
        var before = QuotePdfDocumentComposer.Compose(Quote(null), 2030);
        var approved = QuotePdfDocumentComposer.Compose(
            Quote(
                new PublicResponse("approved", new DateOnly(2030, 5, 7), [Chosen], new PublicTotals(310m, 10m, "Tax (8.25%)", 24.75m, 324.75m, "USD")),
                "approved"),
            2030);
        var declined = QuotePdfDocumentComposer.Compose(
            Quote(new PublicResponse("rejected", new DateOnly(2030, 5, 7), [], null), "rejected"), 2030);

        Assert.All(before.OptionalLines, line => Assert.Equal("Optional — not included in the total", line.Status));
        Assert.Null(before.ResponseStamp);
        Assert.Equal("194.85 USD", before.Totals[^1].Amount);
        Assert.Equal(["Subtotal", "Discount", "Tax (8.25%)", "Total"], before.Totals.Select(total => total.Label).ToArray());
        Assert.Equal("−10.00 USD", before.Totals[1].Amount);

        Assert.Equal(["Included", "Not included"], approved.OptionalLines.Select(line => line.Status).ToArray());
        Assert.Equal("Approved on May 7, 2030", approved.ResponseStamp);
        Assert.Equal("324.75 USD", approved.Totals[^1].Amount);

        Assert.Equal("Declined on May 7, 2030", declined.ResponseStamp);
        Assert.All(declined.OptionalLines, line => Assert.Equal("Optional — not included in the total", line.Status));
        Assert.Equal("194.85 USD", declined.Totals[^1].Amount);

        Assert.Equal("2 hr", before.Lines[0].Quantity);
        Assert.Equal("1", before.OptionalLines[0].Quantity);
        Assert.Equal("Main line", before.Lines[0].Description);
        Assert.Null(before.OptionalLines[0].Description);
        Assert.Equal("© 2030 Acme Plumbing. All rights reserved.", before.Copyright);
        Assert.Equal("Powered by FieldOps", before.PoweredBy);
        Assert.Equal("Q-2036-v2.pdf", QuotePdfDocumentComposer.FileName(Quote(null)));
    }

    // The embedded Inter font renders the approved glyphs on any host, with no system font.
    [Fact]
    public void Render_WithApprovedGlyphs_ProducesAPdfWithEmbeddedFonts()
    {
        var document = QuotePdfDocumentComposer.Compose(
            Quote(new PublicResponse("approved", new DateOnly(2030, 5, 7), [Chosen], new PublicTotals(310m, 10m, "Tax (8.25%)", 24.75m, 324.75m, "USD")), "approved"),
            2030);

        var bytes = new MigraDocQuotePdfRenderer().Render(document);
        var text = Encoding.Latin1.GetString(bytes);

        Assert.Equal("%PDF-", text[..5]);
        Assert.True(bytes.Length > 10_000);
        Assert.Contains("Inter", text, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", text, StringComparison.Ordinal);
    }
}
