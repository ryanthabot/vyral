using VyralBot.Data;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-3 DPAPI round-trip tests for SecretProtector — the at-rest
/// wrapper DapperDataService applies to Platforms.ClientSecret and
/// DiscordBots.Token.
///
/// Runs real DPAPI (CurrentUser scope) because the suite executes on Windows
/// as the interactive user; that is exactly the deployment contract. NOT
/// COVERED HERE (by design): the DapperDataService read/write seam itself —
/// exercising it requires a SQLite profile database and schema seeding, and
/// the seam is a two-line wrap/unwrap around the methods tested here.
/// </summary>
public class SecretProtectorTests
{
    private const string FakeSecret = "k-test-secret-0123456789abcdef";

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsToOriginal()
    {
        var stored = SecretProtector.Protect(FakeSecret);

        Assert.NotNull(stored);
        Assert.StartsWith("v1:", stored);
        Assert.NotEqual(FakeSecret, stored); // never plaintext at rest
        Assert.Equal(FakeSecret, SecretProtector.Unprotect(stored));
    }

    [Fact]
    public void Protect_BlobsAreSalted_AllUnprotectToSamePlaintext()
    {
        // DPAPI salts per call — callers must compare Unprotect output,
        // never blob equality.
        var a = SecretProtector.Protect(FakeSecret);
        var b = SecretProtector.Protect(FakeSecret);

        Assert.NotEqual(a, b);
        Assert.Equal(FakeSecret, SecretProtector.Unprotect(a));
        Assert.Equal(FakeSecret, SecretProtector.Unprotect(b));
    }

    [Fact]
    public void Protect_AlreadyProtectedValue_IsIdempotent()
    {
        // Guards the save seam against double-wrapping when a caller passes
        // an entity whose secret was already protected (e.g. a failed
        // previous save attempt left the blob on the row).
        var once = SecretProtector.Protect(FakeSecret);
        var twice = SecretProtector.Protect(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Unprotect_LegacyPlaintextValue_ReturnedAsIs()
    {
        // Rows written before the seam have no "v1:" prefix and must keep
        // working; they get re-encrypted lazily on their next save.
        Assert.Equal("legacy-plaintext-secret", SecretProtector.Unprotect("legacy-plaintext-secret"));
    }

    [Fact]
    public void Unprotect_NullAndEmpty_PassThrough()
    {
        Assert.Null(SecretProtector.Unprotect(null));
        Assert.Equal(string.Empty, SecretProtector.Unprotect(string.Empty));
    }

    [Fact]
    public void Protect_NullAndEmpty_PassThrough()
    {
        Assert.Null(SecretProtector.Protect(null));
        Assert.Equal(string.Empty, SecretProtector.Protect(string.Empty));
    }

    [Fact]
    public void Unprotect_MalformedBase64Payload_ReturnsStoredValue()
    {
        var malformed = "v1:not!!valid!!base64!!!";
        Assert.Equal(malformed, SecretProtector.Unprotect(malformed));
    }

    [Fact]
    public void Unprotect_ForeignOrCorruptBlob_ReturnsStoredValue()
    {
        // Valid base64 but not a DPAPI blob for this user+entropy:
        // CryptographicException is swallowed and the stored value survives
        // so a bad row can never crash the data layer or silently lose data.
        var foreign = "v1:" + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Equal(foreign, SecretProtector.Unprotect(foreign));
    }

    [Fact]
    public void IsProtected_DetectsPrefixOnlyValues()
    {
        Assert.True(SecretProtector.IsProtected(SecretProtector.Protect(FakeSecret)));
        Assert.False(SecretProtector.IsProtected(FakeSecret));
        Assert.False(SecretProtector.IsProtected(null));
        Assert.False(SecretProtector.IsProtected(string.Empty));
    }
}
