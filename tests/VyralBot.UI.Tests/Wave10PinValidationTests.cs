using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the PROFILE PIN GATE that backs the wave-9 profile
/// delete/switch dialogs (SettingsViewModel.ConfirmDeleteAsync and friends):
/// the PIN's PBKDF2-v2 verify + legacy-migration verify halves of
/// <see cref="VyralBot.Core.Services.Profiles.ProfileService"/>.
///
/// Seam note (deliberate scope): the full ValidatePinAsync integration
/// (wrong pin -> false, right pin -> true against a profile on disk) is NOT
/// driven here — ProfileService's constructor hard-wires the real
/// %LOCALAPPDATA%\VyralBot root AND runs the legacy-data migration against
/// it (AppDataRoot.GetRoot + MigrateLegacyData, no injectable path), so
/// constructing it in a test could MOVE real user profile data. The pure
/// static crypto verifiers are exercised directly via reflection (the
/// wave-8 pattern) with vectors built from the documented parameters
/// (PBKDF2-SHA256, 100k iterations, 16-byte salt, 32-byte key,
/// "v2.{salt}.{hash}").
/// </summary>
public class Wave10PinValidationTests
{
    private const int PinIterations = 100_000;
    private const int PinSaltSizeBytes = 16;
    private const int PinKeySizeBytes = 32;

    private static MethodInfo VerifyPinV2Method()
        => typeof(VyralBot.Core.Services.Profiles.ProfileService)
            .GetMethod("VerifyPinV2", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ProfileService.VerifyPinV2 moved — update the wave-10 tests");

    private static MethodInfo VerifyPinLegacyMethod()
        => typeof(VyralBot.Core.Services.Profiles.ProfileService)
            .GetMethod("VerifyPinLegacy", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ProfileService.VerifyPinLegacy moved — update the wave-10 tests");

    private static bool VerifyPinV2(string? pin, string stored)
        => (bool)VerifyPinV2Method().Invoke(null, new object?[] { pin, stored })!;

    private static bool VerifyPinLegacy(string? pin, string storedHash)
        => (bool)VerifyPinLegacyMethod().Invoke(null, new object?[] { pin, storedHash })!;

    /// <summary>Builds a "v2.{salt}.{hash}" row exactly like HashPinV2 does.</summary>
    private static string HashV2(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(PinSaltSizeBytes);
        using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, PinIterations, HashAlgorithmName.SHA256);
        return $"v2.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(pbkdf2.GetBytes(PinKeySizeBytes))}";
    }

    // ------------------------------------------------------------------
    // VerifyPinV2: the current format (constant-time PBKDF2)
    // ------------------------------------------------------------------

    [Fact]
    public void V2_RightPin_True()
    {
        var stored = HashV2("4821");
        Assert.True(VerifyPinV2("4821", stored));
    }

    [Fact]
    public void V2_WrongPin_False()
    {
        var stored = HashV2("4821");
        Assert.False(VerifyPinV2("4822", stored)); // off by one digit
        Assert.False(VerifyPinV2(string.Empty, stored));
        Assert.False(VerifyPinV2(null!, stored));
    }

    [Fact]
    public void V2_SaltedHashes_DifferPerSave_ButBothVerifyTheirOwnPin()
    {
        // A re-saved PIN gets a fresh salt — both stored rows are valid.
        var first = HashV2("9999");
        var second = HashV2("9999");
        Assert.NotEqual(first, second);
        Assert.True(VerifyPinV2("9999", first));
        Assert.True(VerifyPinV2("9999", second));
    }

    [Fact]
    public void V2_MalformedStoredValue_IsRejectedNeverThrown()
    {
        var valid = HashV2("1234");
        Assert.False(VerifyPinV2("1234", "v2"));                       // no salt/hash parts
        Assert.False(VerifyPinV2("1234", "v2.only-one-part"));         // wrong part count
        Assert.False(VerifyPinV2("1234", "v1.salt.hash"));             // not the v2 marker
        Assert.False(VerifyPinV2("1234", "v2.!!!not-base64!!!.abc"));  // undecodable salt
        Assert.False(VerifyPinV2("1234", string.Empty));
        Assert.False(VerifyPinV2("1234", valid + "corrupted"));        // trailing junk -> 4 parts

        // Sanity: the valid control vector passes on the same inputs.
        Assert.True(VerifyPinV2("1234", valid));
    }

    [Fact]
    public void V2_WrongSaltOrKeyLength_IsRejected()
    {
        // Derivation output lengths are part of the format contract.
        var salt15 = RandomNumberGenerator.GetBytes(15);
        using (var p = new Rfc2898DeriveBytes("1234", salt15, PinIterations, HashAlgorithmName.SHA256))
        {
            Assert.False(VerifyPinV2("1234",
                $"v2.{Convert.ToBase64String(salt15)}.{Convert.ToBase64String(p.GetBytes(PinKeySizeBytes))}"));
        }

        var salt = RandomNumberGenerator.GetBytes(PinSaltSizeBytes);
        using (var p = new Rfc2898DeriveBytes("1234", salt, PinIterations, HashAlgorithmName.SHA256))
        {
            Assert.False(VerifyPinV2("1234",
                $"v2.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(p.GetBytes(31))}"));
        }
    }

    // ------------------------------------------------------------------
    // VerifyPinLegacy: the unsalted SHA256 rows that transparently migrate
    // to v2 on first successful unlock (migration itself lives inside
    // ValidatePinAsync — see the class note for why it is not driven here)
    // ------------------------------------------------------------------

    private static string LegacyHash(string pin)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));

    [Fact]
    public void Legacy_RightPin_True_WrongPin_False()
    {
        var stored = LegacyHash("7777");
        Assert.True(VerifyPinLegacy("7777", stored));
        Assert.False(VerifyPinLegacy("7778", stored));
        Assert.False(VerifyPinLegacy(null!, stored));
        Assert.False(VerifyPinLegacy("7777", "not base64 @@@"));
    }
}
