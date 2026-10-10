using System.Security.Cryptography;
using FieldOps.Application.Features.Organizations;
using FieldOps.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace FieldOps.UnitTests.OnlinePayments;

/// <summary>Bank account encryption and masking (customer-invoice-payments BR-24, BR-25).</summary>
public class AesGcmBankDetailsEncryptorTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    private static AesGcmBankDetailsEncryptor Create(string? key) =>
        new(Options.Create(new BankDetailsSettings { EncryptionKey = key }));

    // BR-24: nonce|tag|cipher, a fresh nonce per value, a ciphertext that is not the plaintext and a tamper-evident tag.
    [Fact]
    public void EncryptAndDecrypt_RoundTripWithAFreshNonceAndDetectTampering()
    {
        var encryptor = Create(Key);
        var first = encryptor.Encrypt("123456789012");
        var second = encryptor.Encrypt("123456789012");

        Assert.True(encryptor.IsAvailable);
        Assert.Equal(12 + 16 + 12, first.Length);
        Assert.NotEqual(first, second);
        Assert.False(first.AsSpan(28).SequenceEqual("123456789012"u8));
        Assert.Equal("123456789012", encryptor.Decrypt(first));
        Assert.Equal("123456789012", encryptor.Decrypt(second));

        var tampered = (byte[])first.Clone();
        tampered[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(tampered));
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(first[..20]));

        var other = Create(Convert.ToBase64String(new byte[32]));
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(first));
    }

    // BR-24: a missing or malformed key means unavailable, never a fallback key.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void WithoutAValidKey_TheEncryptorIsUnavailable(string? key)
    {
        var encryptor = Create(key);

        Assert.False(encryptor.IsAvailable);
        Assert.Throws<BankDetailsUnavailableException>(() => encryptor.Encrypt("1234"));
        Assert.Throws<BankDetailsUnavailableException>(() => encryptor.Decrypt(new byte[40]));
    }

    // BR-25: masked view, account number rules, required-when-not-configured and the optimistic updatedAt comparison.
    [Fact]
    public void BankDetailsRules_MaskValidateAndCompareTheStoredValue()
    {
        var stored = new StoredBankDetails("First Bank", [1, 2, 3], "9012", "021000021", DateTimeOffset.Parse("2026-10-09T10:00:00.123456Z"));
        var view = BankDetailsRules.ToView(stored);

        Assert.Equal(("•••• 9012", "021000021", true), (view.AccountNumberMasked, view.RoutingNumber, view.Configured));
        Assert.False(BankDetailsRules.ToView(new StoredBankDetails(null, null, null, null, null)).Configured);

        foreach (var (name, account, routing, configured, field) in new (string?, string?, string?, bool, string)[]
        {
            ("", "1234", "021000021", true, "bankName"),
            (new string('b', 121), "1234", "021000021", true, "bankName"),
            ("Bank", "123", "021000021", true, "accountNumber"),
            ("Bank", "123456789012345678", "021000021", true, "accountNumber"),
            ("Bank", "12-34", "021000021", true, "accountNumber"),
            ("Bank", null, "021000021", false, "accountNumber"),
            ("Bank", "1234", "12345678", true, "routingNumber"),
            ("Bank", "1234", "12345678a", true, "routingNumber"),
        })
        {
            var errors = new Dictionary<string, string[]>();
            Assert.False(BankDetailsRules.TryValidate(new UpdateBankDetailsCommand(Guid.NewGuid(), Guid.NewGuid(), null, name, account, routing, null), configured, errors, out _, out _, out _));
            Assert.Contains(field, errors.Keys);
        }

        var valid = new Dictionary<string, string[]>();
        Assert.True(BankDetailsRules.TryValidate(new UpdateBankDetailsCommand(Guid.NewGuid(), Guid.NewGuid(), null, " Bank ", "  ", "021000021", null), true, valid, out var bank, out var keep, out _));
        Assert.Equal(("Bank", (string?)null), (bank, keep));

        Assert.True(BankDetailsRules.IsCurrent(null, null, out var none));
        Assert.Null(none);
        Assert.False(BankDetailsRules.IsCurrent(null, stored.UpdatedAt, out _));
        Assert.True(BankDetailsRules.IsCurrent("2026-10-09T10:00:00.123456Z", stored.UpdatedAt, out var expected));
        Assert.Equal(stored.UpdatedAt, expected);
        Assert.False(BankDetailsRules.IsCurrent("2026-10-09T10:00:00.123457Z", stored.UpdatedAt, out _));
        Assert.False(BankDetailsRules.IsCurrent("garbage", stored.UpdatedAt, out _));
        Assert.False(BankDetailsRules.IsCurrent("2026-10-09T10:00:00Z", null, out _));
    }
}
