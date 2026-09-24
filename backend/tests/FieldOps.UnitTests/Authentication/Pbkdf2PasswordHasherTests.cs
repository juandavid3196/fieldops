using FieldOps.Infrastructure.Authentication;

namespace FieldOps.UnitTests.Authentication;

public class Pbkdf2PasswordHasherTests
{
    private const string Password = "Correct horse battery staple";

    // Produced independently with Python hashlib.pbkdf2_hmac("sha256",
    // password, bytes(range(16)), 600000, 32) in the onboarding BR-20 format.
    private const string ReferenceHash =
        "pbkdf2-sha256$600000$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=";

    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ReferenceHashWithOriginalPassword_ReturnsTrue()
    {
        Assert.True(_hasher.Verify(Password, ReferenceHash));
    }

    [Fact]
    public void Verify_ReferenceHashWithDifferentPassword_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("correct horse battery staple", ReferenceHash));
    }

    [Fact]
    public void Hash_WithPassword_ProducesVerifiableBr20Format()
    {
        var hash = _hasher.Hash(Password);

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("600000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.True(_hasher.Verify(Password, hash));
        Assert.False(_hasher.Verify(Password + " ", hash));
    }

    [Fact]
    public void Hash_SamePasswordTwice_UsesDifferentSalts()
    {
        Assert.NotEqual(_hasher.Hash(Password), _hasher.Hash(Password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$600000$AAECAwQFBgcICQoLDA0ODw==")]
    [InlineData("pbkdf2-sha512$600000$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=")]
    [InlineData("pbkdf2-sha256$599999$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=")]
    [InlineData("pbkdf2-sha256$-600000$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=")]
    [InlineData("pbkdf2-sha256$abc$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=")]
    [InlineData("pbkdf2-sha256$600000$AAECAwQFBgcICQoLDA0O$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=")]
    [InlineData("pbkdf2-sha256$600000$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNt")]
    [InlineData("pbkdf2-sha256$600000$not base64!$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=")]
    [InlineData("pbkdf2-sha256$600000$AAECAwQFBgcICQoLDA0ODw==$XVPIZu6aatORmGVRZIKvwjX6L4poQg3b0JB8MRNtnxs=$extra")]
    public void Verify_UnreadableHash_ReturnsFalse(string storedHash)
    {
        Assert.False(_hasher.Verify(Password, storedHash));
    }

    [Fact]
    public void Verify_NullStoredHash_ReturnsFalse()
    {
        Assert.False(_hasher.Verify(Password, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(Password)]
    public void Verify_UnreadableHashWithAnyPassword_ReturnsFalseWithoutThrowing(string password)
    {
        // The unreadable path derives against the dummy hash; no password,
        // including the empty one, may turn that fallback into a success.
        Assert.False(_hasher.Verify(password, "pbkdf2-sha256$600000$broken"));
    }

    [Fact]
    public void DummyHash_IsInSupportedFormatWith600000Iterations()
    {
        var parts = _hasher.DummyHash.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("600000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.Equal(_hasher.DummyHash, new Pbkdf2PasswordHasher().DummyHash);
        Assert.False(_hasher.Verify(Password, _hasher.DummyHash));
    }
}
