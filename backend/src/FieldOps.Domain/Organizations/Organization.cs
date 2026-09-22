namespace FieldOps.Domain.Organizations;

public sealed class Organization
{
    private Organization()
    {
    }

    private Organization(Guid id, string name)
    {
        Id = id;
        Name = name;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? LegalName { get; private set; }

    public string? TaxId { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string Timezone { get; private set; } = "UTC";

    public string Currency { get; private set; } = "USD";

    public decimal DefaultTaxRate { get; private set; }

    public string QuotePrefix { get; private set; } = "Q";

    public string WorkOrderPrefix { get; private set; } = "WO";

    public string InvoicePrefix { get; private set; } = "INV";

    public long NextQuoteNumber { get; private set; } = 1;

    public long NextWorkOrderNumber { get; private set; } = 1;

    public long NextInvoiceNumber { get; private set; } = 1;

    public bool RequireCustomerSignature { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Organization Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Organization name is required.",
                nameof(name));
        }

        return new Organization(Guid.NewGuid(), name.Trim());
    }
}
