namespace FieldOps.Domain.Customers;

public sealed class CustomerContact
{
    private CustomerContact()
    {
    }

    private CustomerContact(
        Guid id,
        Guid organizationId,
        Guid customerId,
        string firstName)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        FirstName = firstName;
        IsPrimary = false;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string? LastName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Title { get; private set; }

    public bool IsPrimary { get; private set; }

    public bool PrefersEmail { get; private set; } = true;

    public bool PrefersSms { get; private set; }

    public Guid? PortalUserId { get; private set; }

    /// <summary>When the portal user was linked; set exactly when <see cref="PortalUserId"/> is (SA-19).</summary>
    public DateTimeOffset? PortalLinkedAt { get; private set; }

    /// <summary>The last time the contact opened the portal updates feed (customer portal BR-25).</summary>
    public DateTimeOffset? PortalUpdatesSeenAt { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CustomerContact Create(
        Guid organizationId,
        Guid customerId,
        string firstName)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer id is required.",
                nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "Contact first name is required.",
                nameof(firstName));
        }

        return new CustomerContact(
            Guid.NewGuid(),
            organizationId,
            customerId,
            firstName.Trim());
    }

    /// <summary>The customer's primary contact, with at least one preferred channel.</summary>
    public static CustomerContact CreatePrimary(
        Guid organizationId,
        Guid customerId,
        string firstName,
        string? lastName,
        string? email,
        string? phone,
        string? title,
        bool prefersEmail,
        bool prefersSms)
    {
        EnsurePreference(prefersEmail, prefersSms);

        var contact = Create(organizationId, customerId, firstName);

        contact.IsPrimary = true;
        contact.LastName = NullIfBlank(lastName);
        contact.Email = NullIfBlank(email);
        contact.Phone = NullIfBlank(phone);
        contact.Title = NullIfBlank(title);
        contact.PrefersEmail = prefersEmail;
        contact.PrefersSms = prefersSms;

        return contact;
    }

    /// <summary>Links the portal user (customer portal BR-13); the link time and the user are set together.</summary>
    public void LinkPortal(Guid userId, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        PortalUserId = userId;
        PortalLinkedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Removes the portal link (customer portal BR-14); returns false when there was none.</summary>
    public bool UnlinkPortal(DateTimeOffset now)
    {
        if (PortalUserId is null)
        {
            return false;
        }

        PortalUserId = null;
        PortalLinkedAt = null;
        UpdatedAt = now;

        return true;
    }

    public void MarkUpdatesSeen(DateTimeOffset now) => PortalUpdatesSeenAt = now;

    /// <summary>Stores the last name given at portal activation when the contact had none (customer portal BR-13).</summary>
    public void SetLastNameIfMissing(string lastName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(LastName) && !string.IsNullOrWhiteSpace(lastName))
        {
            LastName = lastName.Trim();
            UpdatedAt = now;
        }
    }

    /// <summary>Applies the editable fields. Returns true, and stamps <paramref name="now"/>, only when a value changed.</summary>
    public bool Update(
        string firstName,
        string? lastName,
        string? email,
        string? phone,
        string? title,
        bool prefersEmail,
        bool prefersSms,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "Contact first name is required.",
                nameof(firstName));
        }

        EnsurePreference(prefersEmail, prefersSms);

        var first = firstName.Trim();
        var last = NullIfBlank(lastName);
        var mail = NullIfBlank(email);
        var phoneValue = NullIfBlank(phone);
        var titleValue = NullIfBlank(title);

        if (string.Equals(FirstName, first, StringComparison.Ordinal)
            && string.Equals(LastName, last, StringComparison.Ordinal)
            && string.Equals(Email, mail, StringComparison.Ordinal)
            && string.Equals(Phone, phoneValue, StringComparison.Ordinal)
            && string.Equals(Title, titleValue, StringComparison.Ordinal)
            && PrefersEmail == prefersEmail
            && PrefersSms == prefersSms)
        {
            return false;
        }

        FirstName = first;
        LastName = last;
        Email = mail;
        Phone = phoneValue;
        Title = titleValue;
        PrefersEmail = prefersEmail;
        PrefersSms = prefersSms;
        UpdatedAt = now;

        return true;
    }

    private static void EnsurePreference(bool prefersEmail, bool prefersSms)
    {
        if (!prefersEmail && !prefersSms)
        {
            throw new ArgumentException(
                "At least one preferred communication method is required.",
                nameof(prefersEmail));
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
