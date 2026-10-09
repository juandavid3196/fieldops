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

    /// <summary>SA-11: prefix of the payment numbers (invoices-payments-management).</summary>
    public string PaymentPrefix { get; private set; } = "PAY";

    /// <summary>SA-11: the next payment number; it moves only inside the transaction that records the payment.</summary>
    public long NextPaymentNumber { get; private set; } = 1;

    /// <summary>
    /// Immutable public identifier used in the anonymous request form URL
    /// (public service request BR-21). Set once at creation, never by
    /// <see cref="UpdateSettings"/>.
    /// </summary>
    public string PublicSlug { get; private set; } = string.Empty;

    public string RequestPrefix { get; private set; } = "REQ";

    public long NextRequestNumber { get; private set; } = 1;

    public bool RequireCustomerSignature { get; private set; }

    public string? Website { get; private set; }

    public string? AddressLine1 { get; private set; }

    public string? City { get; private set; }

    public string? StateRegion { get; private set; }

    public string? PostalCode { get; private set; }

    public string? CountryCode { get; private set; }

    public bool PricesIncludeTax { get; private set; }

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
        long nextInvoiceNumber,
        string publicSlug)
    {
        if (string.IsNullOrWhiteSpace(publicSlug))
        {
            throw new ArgumentException(
                "Organization public slug is required.",
                nameof(publicSlug));
        }

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
            PublicSlug = publicSlug.Trim(),
        };
    }

    /// <summary>
    /// Updates the organization's profile, taxes, currency and document
    /// numbering (BR-01, BR-02). Same invariants, trimming and
    /// <see cref="ArgumentException"/> pattern as <see cref="Create"/>.
    /// <paramref name="updatedAt"/> is the new <c>updated_at</c> value,
    /// supplied by the caller so the entity never reads the system clock.
    /// </summary>
    public void UpdateSettings(
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
        long nextInvoiceNumber,
        long nextQuoteNumber,
        long nextWorkOrderNumber,
        string? website,
        string addressLine1,
        string city,
        string? stateRegion,
        string postalCode,
        string countryCode,
        bool pricesIncludeTax,
        DateTimeOffset updatedAt)
    {
        if (nextQuoteNumber < 1)
        {
            throw new ArgumentException(
                "Next quote number must be at least 1.",
                nameof(nextQuoteNumber));
        }

        if (nextWorkOrderNumber < 1)
        {
            throw new ArgumentException(
                "Next work order number must be at least 1.",
                nameof(nextWorkOrderNumber));
        }

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

        Name = name.Trim();
        LegalName = legalName?.Trim();
        TaxId = taxId?.Trim();
        Email = email?.Trim();
        Phone = phone?.Trim();
        Timezone = timezone.Trim();
        Currency = currency.Trim();
        DefaultTaxRate = defaultTaxRate;
        QuotePrefix = quotePrefix.Trim();
        WorkOrderPrefix = workOrderPrefix.Trim();
        InvoicePrefix = invoicePrefix.Trim();
        NextInvoiceNumber = nextInvoiceNumber;
        NextQuoteNumber = nextQuoteNumber;
        NextWorkOrderNumber = nextWorkOrderNumber;
        Website = website?.Trim();
        AddressLine1 = addressLine1?.Trim();
        City = city?.Trim();
        StateRegion = stateRegion?.Trim();
        PostalCode = postalCode?.Trim();
        CountryCode = countryCode?.Trim();
        PricesIncludeTax = pricesIncludeTax;
        UpdatedAt = updatedAt;
    }
}
