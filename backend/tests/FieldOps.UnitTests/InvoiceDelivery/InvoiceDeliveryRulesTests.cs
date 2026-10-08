using FieldOps.Application.Features.InvoiceDelivery;

namespace FieldOps.UnitTests.InvoiceDelivery;

public class InvoiceDeliveryRulesTests
{
    private static DeliveryBodyText Body(string? terms = "net_15", string? recipient = "carla@example.com", string? message = "Hello", string? updatedAt = "x") =>
        new(terms, recipient, message, updatedAt);

    // BR-08, BR-09, BR-13 and the Delivery field rules: one table for the terms, the recipient on save and on send, and the message.
    [Theory]
    [InlineData("net_15", "carla@example.com", "Hello", false, null)]
    [InlineData("due_upon_receipt", "  ", "Hello", false, null)]
    [InlineData("net_30", "", "Hello", true, "recipientEmail:Enter the customer's email address.")]
    [InlineData("net_30", "not-an-email", "Hello", false, "recipientEmail:Enter a valid email address.")]
    [InlineData("net_14", "carla@example.com", "Hello", false, "paymentTerms:Select payment terms.")]
    [InlineData(null, "carla@example.com", "Hello", false, "paymentTerms:Select payment terms.")]
    [InlineData("net_15", "carla@example.com", "   ", false, "message:Enter a message.")]
    [InlineData("net_15", "carla@example.com", null, true, "message:Enter a message.")]
    public void Validate_AppliesTheFieldRules(string? terms, string? recipient, string? message, bool recipientRequired, string? expected)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var input = InvoiceDeliveryRules.Validate(Body(terms, recipient, message), recipientRequired, errors);

        if (expected is null)
        {
            Assert.NotNull(input);
            Assert.Empty(errors);
            Assert.Equal(string.IsNullOrWhiteSpace(recipient) ? null : recipient.Trim(), input.RecipientEmail);
        }
        else
        {
            var (key, text) = (expected.Split(':', 2)[0], expected.Split(':', 2)[1]);
            Assert.Null(input);
            Assert.Equal(text, Assert.Single(errors[key]));
        }
    }

    [Fact]
    public void Validate_TrimsAndEnforcesTheLengthLimits()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var exact = new string('m', InvoiceDeliveryMessages.MessageMaxLength);

        var accepted = InvoiceDeliveryRules.Validate(Body(message: $"  {exact}  "), recipientRequired: true, errors);

        Assert.Equal(exact, accepted!.Message);

        var tooLong = InvoiceDeliveryRules.Validate(Body(message: exact + "x", recipient: new string('a', 250) + "@x.co"), recipientRequired: true, errors);

        Assert.Null(tooLong);
        Assert.Equal(InvoiceDeliveryMessages.MessageTooLong, Assert.Single(errors["message"]));
        Assert.Equal(InvoiceDeliveryMessages.RecipientInvalid, Assert.Single(errors["recipientEmail"]));
    }

    // BR-06 and BR-08: the template falls back from the contact to the customer to "there", is cut at 500 characters,
    // the due date follows the terms and only the fields that differ are reported as changed.
    [Fact]
    public void DefaultsDueDateAndChangedFields_FollowTheRules()
    {
        Assert.StartsWith("Hi Carla,\n\nHere's your invoice INV-7 for Fix drain. Thank you for choosing Acme!", InvoiceDeliveryRules.DefaultMessage("Carla", "Carla Customer", "INV-7", "Fix drain", "Acme"), StringComparison.Ordinal);
        Assert.StartsWith("Hi Carla Customer,", InvoiceDeliveryRules.DefaultMessage(" ", "Carla Customer", "INV-7", "Fix drain", "Acme"), StringComparison.Ordinal);
        Assert.StartsWith("Hi there,", InvoiceDeliveryRules.DefaultMessage(null, string.Empty, "INV-7", "Fix drain", "Acme"), StringComparison.Ordinal);
        Assert.EndsWith("Best regards,\nAcme", InvoiceDeliveryRules.DefaultMessage("Carla", "Carla Customer", "INV-7", "Fix drain", "Acme"), StringComparison.Ordinal);
        Assert.Equal(
            InvoiceDeliveryMessages.MessageMaxLength,
            InvoiceDeliveryRules.DefaultMessage("Carla", "Carla Customer", "INV-7", new string('t', 600), "Acme").Length);

        var issued = new DateOnly(2030, 1, 20);
        Assert.Equal(issued, InvoiceDeliveryRules.DueDate(issued, "due_upon_receipt"));
        Assert.Equal(new DateOnly(2030, 2, 4), InvoiceDeliveryRules.DueDate(issued, "net_15"));
        Assert.Equal(new DateOnly(2030, 2, 19), InvoiceDeliveryRules.DueDate(issued, "net_30"));
        Assert.Null(InvoiceDeliveryRules.DueDate(null, "net_30"));

        var input = new DeliveryInput("net_30", null, "Hello");
        Assert.Equal(["paymentTerms", "message"], InvoiceDeliveryRules.ChangedFields("net_15", null, "Bye", input));
        Assert.Empty(InvoiceDeliveryRules.ChangedFields("net_30", null, "Hello", input));
        Assert.Equal(["recipientEmail"], InvoiceDeliveryRules.ChangedFields("net_30", "a@b.co", "Hello", input));
    }
}
