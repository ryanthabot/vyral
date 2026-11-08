using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-8 INTEGRATION: the at-rest secret contract of the Platforms table —
/// callers always hold plaintext Platform entities while the persisted
/// ClientSecret column carries the SecretProtector "v1:" DPAPI envelope;
/// pre-v1 plaintext rows still read as-is (lazy re-encryption on next save).
///
/// Two layers:
///   1. REAL SQLite through <see cref="DapperDataService"/> (temp profile
///      database, schema created by EnsureDatabaseCreatedAsync, at-rest column
///      inspected through a raw connection) — the actual read/write seam
///      SecretProtectorTests (wave 3) deliberately left uncovered;
///   2. the wave-8 in-memory fake's Save/Get pair implementing the same
///      convention, proving the contract is fake-faithful for every other
///      wave-8 test that stores platforms.
/// </summary>
public class IntegrationWave8SecretsTests
{
    private const string Secret = "k-wave8-client-secret-0123456789";
    private const string LegacyPlaintext = "legacy-plaintext-client-secret";

    [Fact]
    public async Task Dapper_RoundTrip_SavesWrapped_ReadsUnwrapped_LegacyRowReadAsIs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-wave8-secrets-{Guid.NewGuid():N}.db");
        try
        {
            var data = new DapperDataService(
                $"Data Source={path}", NullLogger<DapperDataService>.Instance);
            await data.EnsureDatabaseCreatedAsync();

            // Save: caller keeps the plaintext secret (finally-restore contract).
            var platform = new Platform { Name = "Twitch", ClientSecret = Secret };
            await data.SavePlatformAsync(platform);
            Assert.Equal(Secret, platform.ClientSecret);

            // At rest: the column carries the DPAPI envelope, never plaintext.
            var atRest = await ReadClientSecretAsync(path, platform.Id);
            Assert.NotNull(atRest);
            Assert.StartsWith("v1:", atRest);
            Assert.NotEqual(Secret, atRest);
            Assert.True(SecretProtector.IsProtected(atRest));

            // Read back: every platform read path unwraps to plaintext.
            // (SQLite TEXT comparison is case-sensitive — query the stored name.)
            var reloaded = SinglePlatform(await data.GetPlatformsAsync(), "Twitch");
            Assert.Equal(Secret, reloaded.ClientSecret);
            Assert.Equal(Secret, (await data.GetPlatformByIdAsync(platform.Id))!.ClientSecret);
            Assert.Equal(Secret, (await data.GetPlatformByNameAsync("Twitch"))!.ClientSecret);

            // Legacy pre-v1 row written directly to the table (plaintext) is
            // returned as-is instead of crashing or being destroyed.
            var legacyId = Guid.NewGuid();
            await InsertLegacyRowAsync(path, legacyId, "Kick", LegacyPlaintext);
            var legacy = SinglePlatform(await data.GetPlatformsAsync(), "Kick");
            Assert.Equal(LegacyPlaintext, legacy.ClientSecret);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task FakeDataService_Mirrors_The_Dapper_AtRest_Convention()
    {
        var data = new IntegrationWave8DataService();

        var platform = new Platform { Name = "Twitch", ClientSecret = Secret };
        await data.SavePlatformAsync(platform);

        // Caller entity untouched; the stored row carries the envelope.
        Assert.Equal(Secret, platform.ClientSecret);
        var atRest = data.PlatformRowsAtRest[platform.Id].ClientSecret;
        Assert.StartsWith("v1:", atRest);
        Assert.NotEqual(Secret, atRest);

        // Reads unwrap.
        Assert.Equal(Secret, SinglePlatform(await data.GetPlatformsAsync(), "Twitch").ClientSecret);
        Assert.Equal(Secret, (await data.GetPlatformByIdAsync(platform.Id))!.ClientSecret);

        // A legacy plaintext row injected at rest reads as-is.
        var legacyId = Guid.NewGuid();
        data.PlatformRowsAtRest[legacyId] = new Platform { Name = "Kick", ClientSecret = LegacyPlaintext };
        Assert.Equal(LegacyPlaintext, SinglePlatform(await data.GetPlatformsAsync(), "Kick").ClientSecret);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static Platform SinglePlatform(IEnumerable<Platform> platforms, string name)
        => Assert.Single(platforms, p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> ReadClientSecretAsync(string path, Guid id)
    {
        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<string>(
            "SELECT ClientSecret FROM Platforms WHERE Id = @Id", new { Id = id });
    }

    private static async Task InsertLegacyRowAsync(string path, Guid id, string name, string secret)
    {
        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO Platforms (Id, Name, ClientSecret, CreatedAt, UpdatedAt) " +
            "VALUES (@Id, @Name, @Secret, @Now, @Now)",
            new { Id = id, Name = name, Secret = secret, Now = DateTime.UtcNow.ToString("o") });
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Temp-file cleanup is best-effort.
        }
    }
}
