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

    /// <summary>
    /// Creates a new organization for registration (BR-03 to BR-12).
    /// <paramref name="legalName"/>, <paramref name="taxId"/>,
    /// <paramref name="email"/> and <paramref name="phone"/> are nullable
    /// columns; business-required-ness for them is enforced by the caller's
    /// validator, not here.
    /// </summary>
    public static Organization Create(
        string name,
        string? legalName,
        string? taxId,
        string? email,
        string? phone,
        string timezone,
        string currency,
        decimal defaultTaxRate,
        string quotePrefix,
        string workOrderPrefix,
        string invoicePrefix,
        long nextInvoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Organization name is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(timezone))
        {
            throw new ArgumentException(
                "Organization time zone is required.",
                nameof(timezone));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException(
                "Organization currency is required.",
                nameof(currency));
        }

        if (string.IsNullOrWhiteSpace(quotePrefix))
        {
            throw new ArgumentException(
                "Quote prefix is required.",
                nameof(quotePrefix));
        }

        if (string.IsNullOrWhiteSpace(workOrderPrefix))
        {
            throw new ArgumentException(
                "Work order prefix is required.",
                nameof(workOrderPrefix));
        }

        if (string.IsNullOrWhiteSpace(invoicePrefix))
        {
            throw new ArgumentException(
                "Invoice prefix is required.",
                nameof(invoicePrefix));
        }

        if (nextInvoiceNumber < 1)
        {
            throw new ArgumentException(
                "Next invoice number must be at least 1.",
                nameof(nextInvoiceNumber));
        }

        return new Organization(Guid.NewGuid(), name.Trim())
        {
            LegalName = legalName?.Trim(),
            TaxId = taxId?.Trim(),
            Email = email?.Trim(),
            Phone = phone?.Trim(),
            Timezone = timezone.Trim(),
            Currency = currency.Trim(),
            DefaultTaxRate = defaultTaxRate,
            QuotePrefix = quotePrefix.Trim(),
            WorkOrderPrefix = workOrderPrefix.Trim(),
            InvoicePrefix = invoicePrefix.Trim(),
            NextInvoiceNumber = nextInvoiceNumber,
        };
    }
}
