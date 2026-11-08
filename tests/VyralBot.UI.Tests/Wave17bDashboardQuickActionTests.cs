using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Models;
using VyralBot.Core.Services.Audio;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.Integrations.Restream;
using VyralBot.UI.Services;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-17b close-out for the Home Quick Actions bar's pure availability logic
/// (<see cref="DashboardViewModel.CanQuickTwitch"/> / <see cref="DashboardViewModel.CanQuickReplay"/> /
/// <see cref="DashboardViewModel.CanQuickRestream"/> / <see cref="DashboardViewModel.CanQuickTts"/>).
/// Every quick-action service dependency is nullable in the constructor BY
/// DESIGN: a missing registration must disable the matching button, never
/// break Home. These tests construct the real ViewModel with all-null and
/// partial service sets and assert the enable matrix (command bodies — clip,
/// replay, ad break, restream toggle, TTS mute — need live platform/OBS
/// backends and stay out of scope). The Restream stack is real but inert:
/// <see cref="RestreamEngine"/> + <see cref="RestreamFfmpegLocator"/> over a
/// do-nothing <see cref="IProcessManager"/> fake — nothing is ever started,
/// the engine is only constructed so the AND-combination is observable.
/// </summary>
public class Wave17bDashboardQuickActionTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "vyral-wave17b-home-" + Guid.NewGuid().ToString("N"));

    // ------------------------------------------------------------------
    // fakes
    // ------------------------------------------------------------------

    /// <summary>Process manager that starts nothing — the engines stay inert.</summary>
    private sealed class InertProcessManager : IProcessManager
    {
        public Task<Process> StartProcessAsync(System.Diagnostics.ProcessStartInfo startInfo, CancellationToken ct = default)
            => throw new InvalidOperationException("Wave-17b: no process may start.");

        public IReadOnlyList<ProcessInfo> GetActiveProcesses() => Array.Empty<ProcessInfo>();

        public Task KillProcessAsync(int processId, CancellationToken ct = default) => Task.CompletedTask;

        public Task KillAllProcessesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Profile seam that exists and does nothing — presence is all Home checks.</summary>
    private sealed class PresentProfileService : IProfileService
    {
        public string? GetCurrentProfileName() => "default";
        public Task<Profile> GetCurrentProfileAsync() => throw new NotImplementedException();
        public Task<IEnumerable<Profile>> GetProfilesAsync() => throw new NotImplementedException();
        public Task<Profile> CreateProfileAsync(string name, string? displayName = null, string? pinCode = null) => throw new NotImplementedException();
        public Task SwitchProfileAsync(string name, string? pinCode = null) => throw new NotImplementedException();
        public Task DeleteProfileAsync(string name) => throw new NotImplementedException();
        public string GetProfileDataPath(string profileName) => throw new NotImplementedException();
        public string GetProfileCredentialsPath(string profileName) => throw new NotImplementedException();
        public Task<bool> ValidatePinAsync(string profileName, string pinCode) => throw new NotImplementedException();
        public Task UpdateProfilePinAsync(string profileName, string? currentPin, string? newPin) => throw new NotImplementedException();
        public Task UpdateProfileAsync(Profile profile) => throw new NotImplementedException();
        public Task<string> ExportProfileAsync(string profileName, string destinationPath) => throw new NotImplementedException();
        public Task<Profile> ImportProfileAsync(string zipPath) => throw new NotImplementedException();
    }

    /// <summary>TTS seam with fixed settings — enough for CanQuickTts + the startup state load.</summary>
    private sealed class SettingsTtsService : ITtsService
    {
        public TtsSettings Settings { get; set; } = new();

        public Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
            => Task.FromResult<string?>(null);

        public Task SpeakAsync(string text, string? voice = null, int ratePercent = 0, int volumePercent = 100)
            => Task.CompletedTask;

        public IReadOnlyList<string> GetVoices() => Array.Empty<string>();

        public Task<TtsSettings> GetSettingsAsync() => Task.FromResult(Settings);

        public Task SaveSettingsAsync(TtsSettings settings)
        {
            Settings = settings;
            return Task.CompletedTask;
        }

        public void StopSpeaking()
        {
        }
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private UiPreferencesService Prefs()
    {
        Directory.CreateDirectory(_dir);
        return new UiPreferencesService(() => Path.Combine(_dir, "ui-settings.json"));
    }

    private static RestreamEngine Engine() =>
        new(new InertProcessManager(), NullLogger<RestreamEngine>.Instance);

    private static RestreamFfmpegLocator Locator() => new(new InertProcessManager());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------
    // availability matrix
    // ------------------------------------------------------------------

    [Fact]
    public void AllServicesNull_EveryQuickActionDisabled_HomeStillConstructs()
    {
        var vm = new DashboardViewModel(Prefs());

        Assert.False(vm.CanQuickTwitch);
        Assert.False(vm.CanQuickReplay);
        Assert.False(vm.CanQuickRestream);
        Assert.False(vm.CanQuickTts);

        // Sane, non-throwing initial state for the rest of the bar.
        Assert.Equal("Ready", vm.Status);
        Assert.Equal("📡 Start restream", vm.RestreamActionLabel);
        Assert.Equal("🔊 TTS: …", vm.TtsActionLabel);
        Assert.False(vm.IsObsEngineReady);
        Assert.False(vm.IsRestreamRunning);
        Assert.False(vm.IsTtsEnabled);
        Assert.False(vm.IsGettingStartedDismissed); // nothing persisted → default
    }

    [Fact]
    public void TwitchServicePresent_EnablesOnlyTheTwitchQuickActions()
    {
        var vm = new DashboardViewModel(Prefs(), twitch: new IntegrationWave8TwitchService());

        Assert.True(vm.CanQuickTwitch);   // clip + ad break ride the Twitch seam
        Assert.False(vm.CanQuickReplay);  // OBS outputs/engine still absent
        Assert.False(vm.CanQuickRestream);
        Assert.False(vm.CanQuickTts);
    }

    [Fact]
    public void RestreamStack_MissingAnyOfItsFourDeps_LeavesTheToggleDisabled()
    {
        var engine = Engine();

        // Engine only — locator, data and profile seams absent.
        Assert.False(new DashboardViewModel(Prefs(), restreamEngine: engine).CanQuickRestream);

        // Engine + locator — persistence seams still absent.
        Assert.False(new DashboardViewModel(Prefs(), restreamEngine: engine, ffmpegLocator: Locator())
            .CanQuickRestream);

        // Engine + locator + data service — profile seam still absent.
        Assert.False(new DashboardViewModel(
            Prefs(),
            restreamEngine: engine,
            ffmpegLocator: Locator(),
            dataService: new IntegrationWave8DataService()).CanQuickRestream);
    }

    [Fact]
    public void RestreamStack_AllFourDepsPresent_EnablesTheToggle()
    {
        var vm = new DashboardViewModel(
            Prefs(),
            restreamEngine: Engine(),
            ffmpegLocator: Locator(),
            dataService: new IntegrationWave8DataService(),
            profileService: new PresentProfileService());

        Assert.True(vm.CanQuickRestream);
        Assert.False(vm.CanQuickTwitch); // Twitch seam still absent — no bleed-over
        Assert.False(vm.CanQuickReplay); // OBS outputs/engine are a separate pair
        Assert.False(vm.CanQuickTts);
    }

    [Fact]
    public async Task TtsServicePresent_EnablesTtsAndLoadsThePersistedEnabledState()
    {
        var tts = new SettingsTtsService { Settings = new TtsSettings { Enabled = true } };
        var vm = new DashboardViewModel(Prefs(), tts: tts);

        Assert.True(vm.CanQuickTts);
        Assert.False(vm.CanQuickTwitch);

        // The ctor kicks an async load of Tts.Enabled — poll briefly for it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!vm.IsTtsEnabled && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(vm.IsTtsEnabled);
    }

    [Fact]
    public void GettingStartedDismissed_LoadsFromPersistedUiSettings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "ui-settings.json"),
            "{\"HomeGettingStartedDismissed\":true}");

        var vm = new DashboardViewModel(Prefs());
        Assert.True(vm.IsGettingStartedDismissed);
    }
}
