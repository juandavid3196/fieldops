using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Features.Organizations;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Payments;

/// <summary>
/// AES-256-GCM encryption of the bank account number (customer-invoice-payments BR-24). The stored value is the random
/// 12-byte nonce, the 16-byte tag and the cipher text. The key is read from configuration only; without a valid key the
/// encryptor is unavailable and never encrypts with a fallback. Neither the key nor any plaintext is logged.
/// </summary>
public sealed class AesGcmBankDetailsEncryptor : IBankDetailsEncryptor
{
    private const int NonceSize = 12;

    private const int TagSize = 16;

    private readonly byte[]? key;

    public AesGcmBankDetailsEncryptor(IOptions<BankDetailsSettings> settings)
    {
        TryReadKey(settings.Value.EncryptionKey, out key);
    }

    public bool IsAvailable => key is not null;

    public byte[] Encrypt(string plaintext)
    {
        var secret = key ?? throw new BankDetailsUnavailableException();
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + TagSize + plain.Length];
        var nonce = result.AsSpan(0, NonceSize);
        var tag = result.AsSpan(NonceSize, TagSize);
        var cipher = result.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(secret, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return result;
    }

    /// <summary>Throws <see cref="CryptographicException"/> for a tampered or foreign value.</summary>
    public string Decrypt(byte[] protectedValue)
    {
        var secret = key ?? throw new BankDetailsUnavailableException();

        if (protectedValue.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The protected value is too short.");
        }

        var nonce = protectedValue.AsSpan(0, NonceSize);
        var tag = protectedValue.AsSpan(NonceSize, TagSize);
        var cipher = protectedValue.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(secret, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>True when the text is base64 of exactly 32 bytes.</summary>
    public static bool TryReadKey(string? text, out byte[]? bytes)
    {
        bytes = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var buffer = new byte[BankDetailsSettings.KeyBytes + 4];

        if (!Convert.TryFromBase64String(text.Trim(), buffer, out var written) || written != BankDetailsSettings.KeyBytes)
        {
            return false;
        }

        bytes = buffer.AsSpan(0, written).ToArray();

        return true;
    }
}
