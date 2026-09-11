using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Integrations.Obs.Engine;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>
/// One Decide verdict for the bundled Vyral starter collection.
/// <see cref="ImportedFileMissingFromObs"/> distinguishes the recovery case
/// (we imported this version before, but the file is gone from OBS) from a
/// plain no-op.
/// </summary>
public sealed record BundledImportState(
    bool ShouldImport,
    string Reason,
    string BundledPath,
    string ContentHash,
    bool ImportedFileMissingFromObs);

/// <summary>
/// Imports the bundled Vyral starter scene collection into the user's OBS
/// scene collections. Strictly ADDITIVE by contract: the only file this
/// service ever writes is <c>Vyral.json</c> inside the OBS scenes directory —
/// never a sibling, never a delete. Whether this version was already imported
/// is tracked via the config hash <c>Obs.BundledCollectionHash</c> (SHA256 of
/// the bundled file bytes at the last import), so the same version imports
/// exactly once and a changed bundle replaces only our previously imported
/// file. A Vyral.json the user created THEMSELVES (no stored hash) is never
/// touched automatically — overwriting it is a deliberate Settings action.
/// </summary>
public sealed class ObsBundledCollectionService
{
    /// <summary>File name inside the OBS scene collections directory — the only file this service writes.</summary>
    public const string BundledCollectionFileName = "Vyral.json";

    /// <summary>Collection name as OBS knows it (file name minus extension; also the --collection launch argument).</summary>
    public const string BundledCollectionName = "Vyral";

    private const string StoredHashKey = "Obs.BundledCollectionHash";
    private const string ConfigCategory = "Obs";

    private readonly ObsInstallLocator locator;
    private readonly IDataService data;
    private readonly ILogger<ObsBundledCollectionService> logger;
    private readonly string? bundledPathOverride;

    public ObsBundledCollectionService(
        ObsInstallLocator locator,
        IDataService data,
        ILogger<ObsBundledCollectionService>? logger = null,
        string? bundledPathOverride = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.logger = logger ?? NullLogger<ObsBundledCollectionService>.Instance;
        // Injectable so tests run against a temp file instead of the app's Content folder.
        this.bundledPathOverride = bundledPathOverride;
    }

    /// <summary>The bundled collection's on-disk path, or null when the build ships without it.</summary>
    public string? GetBundledPath()
    {
        string path = bundledPathOverride ?? GetDefaultBundledPath();
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Resolves the shipped Content\Vyral.json. The exe's OWN directory is
    /// checked first: in a single-file publish <see cref="AppContext.BaseDirectory"/>
    /// can resolve to the bundle extraction directory, while loose Content
    /// files always sit next to the exe (<see cref="Environment.ProcessPath"/>).
    /// </summary>
    private static string GetDefaultBundledPath()
    {
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir))
        {
            return Path.Combine(exeDir, "Content", BundledCollectionFileName);
        }

        return Path.Combine(AppContext.BaseDirectory, "Content", BundledCollectionFileName);
    }

    /// <summary>SHA256 of the file bytes as lowercase hex.</summary>
    public string ComputeHash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    /// <summary>
    /// Decides whether this bundled version still needs importing into the
    /// given OBS scenes directory (without writing anything). Async because
    /// the stored hash lives behind the config store.
    /// </summary>
    public async Task<BundledImportState> DecideAsync(string obsSceneCollectionsDir)
    {
        string? bundledPath = GetBundledPath();
        if (bundledPath is null)
        {
            return new BundledImportState(
                ShouldImport: false,
                Reason: "no bundled collection",
                BundledPath: "",
                ContentHash: "",
                ImportedFileMissingFromObs: false);
        }

        string contentHash = ComputeHash(bundledPath);
        string targetPath = Path.Combine(obsSceneCollectionsDir, BundledCollectionFileName);
        bool targetExists = File.Exists(targetPath);
        string? storedHash = await data.GetConfigValueAsync(StoredHashKey).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            if (!targetExists)
            {
                return new BundledImportState(true, "first import", bundledPath, contentHash, false);
            }

            // A Vyral.json we never imported — the user made it themselves.
            // Never overwritten behind their back; the Settings button is the
            // deliberate path.
            return new BundledImportState(
                false,
                "a Vyral.json already exists — use Advanced → Re-import to overwrite it deliberately",
                bundledPath, contentHash, false);
        }

        if (!string.Equals(storedHash, contentHash, StringComparison.Ordinal))
        {
            return new BundledImportState(
                true,
                "bundled collection updated — replaces the previously imported Vyral collection (only that file)",
                bundledPath, contentHash, false);
        }

        return targetExists
            ? new BundledImportState(false, "already imported this version", bundledPath, contentHash, false)
            : new BundledImportState(true, "re-import (deleted from OBS)", bundledPath, contentHash, true);
    }

    /// <summary>
    /// Copies the bundled file bytes into <paramref name="obsSceneCollectionsDir"/>
    /// as Vyral.json (created if needed, overwrite = true — replacing OUR
    /// previously imported file is the intended update path) and records the
    /// imported version's hash.
    /// </summary>
    public async Task ImportToObsAsync(string obsSceneCollectionsDir, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string? bundledPath = GetBundledPath()
            ?? throw new FileNotFoundException(
                $"No bundled {BundledCollectionFileName} ships with this build.");

        // The single write site of this service — targetPath is exactly
        // <obsSceneCollectionsDir>\Vyral.json; nothing else is created or removed.
        Directory.CreateDirectory(obsSceneCollectionsDir);
        string targetPath = Path.Combine(obsSceneCollectionsDir, BundledCollectionFileName);
        File.Copy(bundledPath, targetPath, overwrite: true);

        await MarkImportedAsync(targetPath).ConfigureAwait(false);
        logger.LogInformation(
            "Imported the bundled Vyral starter collection to {Target}", targetPath);
    }

    /// <summary>Stores the SHA256 of the given imported file as the imported version.</summary>
    public async Task MarkImportedAsync(string filePath)
    {
        await data.SetConfigValueAsync(StoredHashKey, ComputeHash(filePath), ConfigCategory)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// SHA256 of CONTENT (not a file) — the GitHub update path applies content
    /// that may never exist as a bundled file, and <see cref="GetStoredHashAsync"/>
    /// consumers compare against this exactly like an import hash.
    /// </summary>
    public string ComputeContentHash(string content)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    /// <summary>Stores a content hash directly as the applied version (GitHub update path).</summary>
    public async Task MarkContentImportedAsync(string contentHash)
    {
        await data.SetConfigValueAsync(StoredHashKey, contentHash, ConfigCategory)
            .ConfigureAwait(false);
    }

    /// <summary>The hash recorded at the last import, or null before the first one.</summary>
    public async Task<string?> GetStoredHashAsync()
    {
        return await data.GetConfigValueAsync(StoredHashKey).ConfigureAwait(false);
    }
}
