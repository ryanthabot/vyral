using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Integrations.Obs.Engine;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>Outcome of <see cref="ObsCollectionUpdateService.UpdateFromGitHubAsync"/>.</summary>
public sealed record ObsUpdateResult(bool Success, string Message, int AddedScenes, int AddedSources, int AddedSceneItems);

/// <summary>
/// Updates the user's ACTIVE OBS scene collection from the Vyral GitHub
/// repository with a NON-DESTRUCTIVE merge (<see cref="ObsCollectionMerger"/>):
/// fetch upstream → merge into the active collection file (detected from
/// <c>global.ini</c>/<c>user.ini</c>) → write the merged document back to that
/// SAME file → record the fetched content's hash as the applied version so the
/// bundled auto-import logic treats the collection as current. When the active
/// collection file does not exist (never set up), upstream is written verbatim
/// as a fresh install. The user's file is only ever written AFTER a successful
/// merge — a fetch or parse failure leaves it byte-identical.
/// </summary>
public sealed class ObsCollectionUpdateService
{
    /// <summary>The bundled Vyral collection on the repository's main branch (public, no auth).</summary>
    public const string UpstreamCollectionUrl =
        "https://raw.githubusercontent.com/ryanthabot/vyral/main/VyralBot/src/VyralBot.UI/Content/Vyral.json";

    private static readonly HttpClient sharedClient = CreateSharedClient();

    private readonly ObsBundledCollectionService bundled;
    private readonly ObsInstallLocator locator;
    private readonly IDataService data;
    private readonly HttpClient http;
    private readonly ILogger<ObsCollectionUpdateService> logger;

    // Injectable so tests run against a temp <config>\basic\scenes directory
    // (the ConfigDir is derived as its grandparent) instead of the real
    // %APPDATA%\obs-studio — same isolation idea as the bundled service's
    // bundledPathOverride.
    private readonly string? scenesDirOverride;

    public ObsCollectionUpdateService(
        ObsBundledCollectionService bundled,
        ObsInstallLocator locator,
        IDataService data,
        HttpClient? http = null,
        ILogger<ObsCollectionUpdateService>? logger = null,
        string? scenesDirOverride = null)
    {
        this.bundled = bundled ?? throw new ArgumentNullException(nameof(bundled));
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.http = http ?? sharedClient;
        this.logger = logger ?? NullLogger<ObsCollectionUpdateService>.Instance;
        this.scenesDirOverride = scenesDirOverride;
    }

    /// <summary>Fetches the upstream collection JSON. Throws on network/HTTP failure.</summary>
    public async Task<string> FetchUpstreamAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpstreamCollectionUrl);
        // Per request, not on the client: an injected HttpClient (tests) would
        // otherwise go out headerless, and GitHub rejects headerless requests.
        request.Headers.UserAgent.ParseAdd("VyralBot");

        using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task<ObsUpdateResult> UpdateFromGitHubAsync(CancellationToken ct = default)
    {
        string upstreamJson;
        try
        {
            upstreamJson = await FetchUpstreamAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Vyral collection update: fetching {Url} failed", UpstreamCollectionUrl);
            return new ObsUpdateResult(false, $"Could not reach GitHub: {ex.Message}", 0, 0, 0);
        }

        if (ResolveDirs() is not (string scenesDir, string configDir))
        {
            return new ObsUpdateResult(false, "OBS Studio is not installed — nothing to update.", 0, 0, 0);
        }

        string activeName = DetectActiveCollectionName(configDir);
        string activePath = Path.Combine(scenesDir, activeName + ".json");
        string upstreamHash = bundled.ComputeContentHash(upstreamJson);

        if (!File.Exists(activePath))
        {
            // Fresh install: the user never had this collection — upstream goes
            // in verbatim, exactly like a first bundled import would.
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(scenesDir);
            await File.WriteAllTextAsync(activePath, upstreamJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct)
                .ConfigureAwait(false);
            await bundled.MarkContentImportedAsync(upstreamHash).ConfigureAwait(false);
            logger.LogInformation(
                "Vyral collection update: installed the GitHub collection as '{Name}' (fresh setup)", activeName);
            return new ObsUpdateResult(true, $"Installed '{activeName}' from GitHub (fresh setup).", 1, 0, 0);
        }

        string userJson = await File.ReadAllTextAsync(activePath, ct).ConfigureAwait(false);
        ObsMergeResult merge = ObsCollectionMerger.Merge(userJson, upstreamJson);
        if (!merge.Success)
        {
            // Safety contract: no write on merge failure — the user's file is
            // byte-identical to before this call.
            return new ObsUpdateResult(false, merge.Message, 0, 0, 0);
        }

        ct.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(activePath, merge.MergedJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct)
            .ConfigureAwait(false);
        // The fetched content is now the applied version — the auto-import
        // logic (stored hash) treats the collection as current.
        await bundled.MarkContentImportedAsync(upstreamHash).ConfigureAwait(false);

        logger.LogInformation(
            "Vyral collection update: merged GitHub updates into {Path} (+{Scenes} scenes, +{Sources} sources, +{Items} scene items)",
            activePath, merge.AddedScenes, merge.AddedSources, merge.AddedSceneItems);

        return new ObsUpdateResult(
            true, merge.Message, merge.AddedScenes, merge.AddedSources, merge.AddedSceneItems);
    }

    /// <summary>
    /// Path of the currently active scene-collection file, or null when OBS is
    /// missing or the name cannot be resolved. Used by the UI to reload the
    /// updated collection into the embedded studio engine.
    /// </summary>
    public string? TryGetActiveCollectionPath()
    {
        if (ResolveDirs() is not (string scenesDir, string configDir))
        {
            return null;
        }

        return Path.Combine(scenesDir, DetectActiveCollectionName(configDir) + ".json");
    }

    /// <summary>True while obs64.exe is running — OBS caches its collections in memory and rewrites them on exit.</summary>
    public static bool IsObsRunning()
    {
        try
        {
            Process[] candidates = Process.GetProcessesByName("obs64");
            bool running = false;
            foreach (Process candidate in candidates)
            {
                try
                {
                    running |= !candidate.HasExited;
                }
                catch (Exception)
                {
                    // Exited between listing and probing.
                }
                finally
                {
                    candidate.Dispose();
                }
            }

            return running;
        }
        catch (Exception)
        {
            return false; // probing must never break the update flow
        }
    }

    private (string ScenesDir, string ConfigDir)? ResolveDirs()
    {
        if (scenesDirOverride is not null)
        {
            // <configDir>\basic\scenes → the config dir is the grandparent.
            string overrideConfigDir = Path.GetDirectoryName(Path.GetDirectoryName(scenesDirOverride)) ?? scenesDirOverride;
            return (scenesDirOverride, overrideConfigDir);
        }

        if (locator.Locate() is not { } install)
        {
            return null;
        }

        return (install.SceneCollectionsDir, install.ConfigDir);
    }

    /// <summary>
    /// OBS records the active collection in global.ini under [Basic]:
    /// <c>SceneCollectionFile</c> (the file name without extension) with
    /// <c>SceneCollection</c> as its display name; user.ini is checked as a
    /// fallback. Falls back to "Vyral" when undetectable (fresh OBS, no
    /// [Basic] section yet — the collection VyralBot manages by name).
    /// </summary>
    private static string DetectActiveCollectionName(string configDir)
    {
        foreach (string iniName in (string[])["global.ini", "user.ini"])
        {
            string iniPath = Path.Combine(configDir, iniName);
            if (!File.Exists(iniPath))
            {
                continue;
            }

            try
            {
                ObsIniFile ini = ObsIniFile.Load(iniPath);
                if (ini.TryGetString("Basic", "SceneCollectionFile", out string? file) && !string.IsNullOrWhiteSpace(file))
                {
                    return file.Trim();
                }

                if (ini.TryGetString("Basic", "SceneCollection", out string? name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
            catch (Exception)
            {
                // Unreadable ini — fall through to the next candidate/default.
            }
        }

        return ObsBundledCollectionService.BundledCollectionName;
    }

    private static HttpClient CreateSharedClient() => new() { Timeout = TimeSpan.FromSeconds(30) };
}
