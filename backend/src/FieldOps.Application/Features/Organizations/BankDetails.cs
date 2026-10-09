using System.Net;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>Machine codes and messages of the bank transfer details (customer-invoice-payments BR-24, BR-25).</summary>
public static class BankDetailsMessages
{
    public const string UnavailableCode = "bank_details_unavailable";

    public const string UnavailableTitle = "Bank details can't be saved right now.";

    public const string ChangedCode = "bank_details_changed";

    public const string ChangedTitle = "These details changed. Refresh to see the latest.";

    public const string BankNameInvalid = "Enter the bank name.";

    public const string AccountInvalid = "Enter a valid account number.";

    public const string RoutingInvalid = "Enter a 9-digit routing number.";

    public const int BankNameMaxLength = 120;

    public const int AccountMinLength = 4;

    public const int AccountMaxLength = 17;

    public const int RoutingLength = 9;
}

/// <summary>The AES-256-GCM seam of the account number (BR-24); the plaintext lives only in memory.</summary>
public interface IBankDetailsEncryptor
{
    /// <summary>False when no valid 32-byte key is configured: the bank endpoints answer 503 and the page offers no bank transfer.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns nonce (12 bytes), tag (16 bytes) and cipher text; throws <see cref="BankDetailsUnavailableException"/> without a key.</summary>
    byte[] Encrypt(string plaintext);

    string Decrypt(byte[] protectedValue);
}

public sealed class BankDetailsUnavailableException : Exception
{
    public BankDetailsUnavailableException()
        : base("Bank details encryption is not configured.")
    {
    }
}

/// <summary>The masked bank details of the internal endpoints (BR-25); the full account number is never returned.</summary>
public sealed record BankDetailsView(
    bool Configured, string? BankName, string? AccountNumberMasked, string? RoutingNumber, DateTimeOffset? UpdatedAt);

/// <summary>The stored bank columns of an organization.</summary>
public sealed record StoredBankDetails(
    string? BankName, byte[]? Ciphertext, string? AccountLast4, string? RoutingNumber, DateTimeOffset? UpdatedAt)
{
    public bool Configured => BankName is not null;
}

public sealed record UpdateBankDetailsCommand(
    Guid OrganizationId,
    Guid UserId,
    IPAddress? IpAddress,
    string? BankName,
    string? AccountNumber,
    string? RoutingNumber,
    string? UpdatedAt);

public abstract record BankDetailsResult
{
    private BankDetailsResult()
    {
    }

    public sealed record Succeeded(BankDetailsView Details) : BankDetailsResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : BankDetailsResult;

    public sealed record Stale : BankDetailsResult;

    public sealed record Unavailable : BankDetailsResult;
}

/// <summary>Persistence port of the bank details; the write is conditional on <c>bank_details_updated_at</c> and never touches <c>organizations.updated_at</c>.</summary>
public interface IBankDetailsStore
{
    Task<StoredBankDetails?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the details and the audit row in one transaction when the stored value equals <paramref name="expected"/>
    /// (null while not configured); false otherwise. A null ciphertext keeps the stored account number.
    /// </summary>
    Task<bool> TrySaveAsync(
        Guid organizationId,
        Guid userId,
        IPAddress? ipAddress,
        string bankName,
        byte[]? ciphertext,
        string? accountLast4,
        string routingNumber,
        DateTimeOffset? expected,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}

public static class BankDetailsRules
{
    public static string Mask(string last4) => "•••• " + last4;

    public static BankDetailsView ToView(StoredBankDetails stored) =>
        stored.Configured
            ? new BankDetailsView(true, stored.BankName, Mask(stored.AccountLast4 ?? string.Empty), stored.RoutingNumber, stored.UpdatedAt)
            : new BankDetailsView(false, null, null, null, null);

    /// <summary>The account number: digits only, 4 to 17; empty is allowed only while one is stored (null result = keep).</summary>
    public static bool TryValidate(
        UpdateBankDetailsCommand command,
        bool configured,
        Dictionary<string, string[]> errors,
        out string bankName,
        out string? account,
        out string routing)
    {
        bankName = command.BankName?.Trim() ?? string.Empty;
        account = string.IsNullOrWhiteSpace(command.AccountNumber) ? null : command.AccountNumber.Trim();
        routing = command.RoutingNumber?.Trim() ?? string.Empty;

        if (bankName.Length is 0 or > BankDetailsMessages.BankNameMaxLength)
        {
            errors["bankName"] = [BankDetailsMessages.BankNameInvalid];
        }

        if (account is null ? !configured : !IsDigits(account, BankDetailsMessages.AccountMinLength, BankDetailsMessages.AccountMaxLength))
        {
            errors["accountNumber"] = [BankDetailsMessages.AccountInvalid];
        }

        if (!IsDigits(routing, BankDetailsMessages.RoutingLength, BankDetailsMessages.RoutingLength))
        {
            errors["routingNumber"] = [BankDetailsMessages.RoutingInvalid];
        }

        return errors.Count == 0;
    }

    /// <summary>True when the client value equals the stored one: both null, or the same instant (a missing or unreadable text never matches a stored value).</summary>
    public static bool IsCurrent(string? clientUpdatedAt, DateTimeOffset? stored, out DateTimeOffset? expected)
    {
        expected = null;

        if (string.IsNullOrWhiteSpace(clientUpdatedAt))
        {
            return stored is null;
        }

        if (!UpdatedAtValidation.TryParse(clientUpdatedAt, out var parsed))
        {
            return false;
        }

        expected = parsed;

        return stored == parsed;
    }

    private static bool IsDigits(string value, int min, int max) =>
        value.Length >= min && value.Length <= max && value.All(char.IsAsciiDigit);
}

/// <summary>GET /organization-settings/bank-details (BR-25): Owner only, masked.</summary>
public sealed class GetBankDetailsHandler(IBankDetailsStore store, IBankDetailsEncryptor encryptor)
{
    public async Task<BankDetailsResult> HandleAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!encryptor.IsAvailable)
        {
            return new BankDetailsResult.Unavailable();
        }

        var stored = await store.GetAsync(organizationId, cancellationToken);

        return stored is null
            ? new BankDetailsResult.Unavailable()
            : new BankDetailsResult.Succeeded(BankDetailsRules.ToView(stored));
    }
}

/// <summary>PUT /organization-settings/bank-details (BR-24, BR-25): validation, stale check, encryption and the audited conditional write.</summary>
public sealed class UpdateBankDetailsHandler(IBankDetailsStore store, IBankDetailsEncryptor encryptor, TimeProvider timeProvider)
{
    public async Task<BankDetailsResult> HandleAsync(UpdateBankDetailsCommand command, CancellationToken cancellationToken)
    {
        if (!encryptor.IsAvailable)
        {
            return new BankDetailsResult.Unavailable();
        }

        var current = await store.GetAsync(command.OrganizationId, cancellationToken);

        if (current is null)
        {
            return new BankDetailsResult.Unavailable();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!BankDetailsRules.TryValidate(command, current.Configured, errors, out var bankName, out var account, out var routing))
        {
            return new BankDetailsResult.Invalid(errors);
        }

        if (!BankDetailsRules.IsCurrent(command.UpdatedAt, current.UpdatedAt, out var expected))
        {
            return new BankDetailsResult.Stale();
        }

        // Microsecond precision, always after the previous value, so the round trip through the API and the column is exact.
        var now = timeProvider.GetUtcNow();
        var next = new DateTimeOffset(now.UtcDateTime.Ticks - (now.UtcDateTime.Ticks % 10), TimeSpan.Zero);

        if (current.UpdatedAt is { } previous && next <= previous)
        {
            next = previous.AddTicks(10);
        }

        var saved = await store.TrySaveAsync(
            command.OrganizationId,
            command.UserId,
            command.IpAddress,
            bankName,
            account is null ? null : encryptor.Encrypt(account),
            account is null ? null : account[^4..],
            routing,
            expected,
            next,
            cancellationToken);

        if (!saved || await store.GetAsync(command.OrganizationId, cancellationToken) is not { } stored)
        {
            return new BankDetailsResult.Stale();
        }

        return new BankDetailsResult.Succeeded(BankDetailsRules.ToView(stored));
    }
}
