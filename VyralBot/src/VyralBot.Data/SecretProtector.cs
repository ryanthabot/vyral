using System.Security.Cryptography;
using System.Text;

namespace VyralBot.Data;

/// <summary>
/// DPAPI protector for secrets persisted in the per-profile SQLite database
/// (Platforms.ClientSecret, DiscordBots.Token). Scope is CurrentUser plus an
/// app-specific entropy constant, so blobs can only be unwrapped by the same
/// Windows user running VyralBot — a copied/stolen profile DB is inert.
/// </summary>
/// <remarks>
/// Stored format is versioned: <c>"v1:" + base64(DPAPI blob)</c>. Values
/// without the prefix are pre-v1 plaintext rows: <see cref="Unprotect"/>
/// returns them unchanged so legacy installs keep working, and they are
/// re-encrypted lazily the next time the row is saved (no schema change, no
/// destructive migration). This helper never logs secret material.
/// </remarks>
public static class SecretProtector
{
    private const string Prefix = "v1:";

    // Not a secret — app-separated entropy so a blob can't be replayed
    // through another DPAPI consumer on the same user profile.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VyralBot.SecretProtector.v1");

    /// <summary>Returns true when the value carries the versioned DPAPI envelope.</summary>
    public static bool IsProtected(string? value)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Wraps a plaintext secret as <c>v1:</c> + base64 DPAPI blob. Idempotent:
    /// an already-protected value is returned unchanged (never double-wrapped).
    /// Null/empty passes through so callers don't need pre-checks.
    /// </summary>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || IsProtected(plain))
        {
            return plain;
        }

        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(blob);
    }

    /// <summary>
    /// Unwraps a <c>v1:</c> DPAPI blob back to plaintext. Values without the
    /// prefix (legacy plaintext rows) are returned as-is. If unprotection
    /// fails (blob from a different Windows user, corrupted row), the stored
    /// value is returned unchanged rather than destroyed — the caller sees an
    /// unusable secret instead of a crash or silent data loss, and the next
    /// save re-wraps it.
    /// </summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !IsProtected(stored))
        {
            return stored;
        }

        try
        {
            var blob = Convert.FromBase64String(stored.Substring(Prefix.Length));
            var plain = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return stored;
        }
        catch (FormatException)
        {
            // Malformed v1 payload — treat as opaque legacy data, never throw
            // a data-layer exception over a single bad column.
            return stored;
        }
    }
}
