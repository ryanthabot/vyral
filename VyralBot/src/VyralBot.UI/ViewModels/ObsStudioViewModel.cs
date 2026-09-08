using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Integrations.Obs.Engine;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Embedded OBS studio page: engine lifecycle, live scenes/sources of the
/// running libobs instance, stream/record/replay outputs and the 1 Hz stats
/// strip. Every service call is wrapped so a native fault degrades to a
/// status message instead of taking the app down.
/// </summary>
public partial class ObsStudioViewModel : ViewModelBase, IDisposable
{
    private const string AutoInitKey = "Obs.AutoInit";
    private const string LastCollectionPathKey = "Obs.LastCollectionPath";
    private const string ConfigCategory = "Obs";

    private readonly ObsEngine _engine;
    private readonly ObsStudioService _studio;
    private readonly ObsOutputService _outputs;
    private readonly ObsInstallLocator _locator;
    private readonly IDataService _dataService;
    private readonly ILogger<ObsStudioViewModel> _logger;

    // StatusChanged arrives on the threadpool — every handler marshals here.
    private readonly Dispatcher _dispatcher;

    private readonly DispatcherTimer _statsTimer;
    private bool _polling;

    private ObsEngineState _engineState = ObsEngineState.NotInitialized;
    private DateTime? _readyAtUtc;
    private bool _autoLoadAttempted;
    private string? _currentSceneName;

    [ObservableProperty]
    private string? _engineStatusText;

    [ObservableProperty]
    private bool _isEngineReady;

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isNotInstalled;

    [ObservableProperty]
    private ObservableCollection<ObsSceneRow> _scenes = new();

    [ObservableProperty]
    private ObsSceneRow? _selectedScene;

    [ObservableProperty]
    private ObservableCollection<ObsSourceRow> _sceneSources = new();

    [ObservableProperty]
    private bool _streamActive;

    [ObservableProperty]
    private bool _recordActive;

    [ObservableProperty]
    private bool _replayActive;

    [ObservableProperty]
    private bool _streamStarting;

    [ObservableProperty]
    private bool _recordStarting;

    [ObservableProperty]
    private bool _replayStarting;

    // Transient action-result line under the output buttons.
    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _replaySavedText;

    [ObservableProperty]
    private string? _fpsText;

    [ObservableProperty]
    private string? _resolutionText;

    [ObservableProperty]
    private string? _uptimeText;

    [ObservableProperty]
    private ObservableCollection<string> _missingSources = new();

    [ObservableProperty]
    private bool _hasMissingSources;

    [ObservableProperty]
    private string? _missingSourcesText;

    public ObsStudioViewModel(
        ObsEngine engine,
        ObsStudioService studio,
        ObsOutputService outputs,
        ObsInstallLocator locator,
        IDataService dataService,
        ILogger<ObsStudioViewModel> logger)
    {
        _engine = engine;
        _studio = studio;
        _outputs = outputs;
        _locator = locator;
        _dataService = dataService;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += OnStatsTick;

        // RaiseStatus dispatches on the threadpool — jump back before touching
        // observable state.
        _engine.StatusChanged += status => _dispatcher.InvokeAsync(() => ApplyEngineStatus(status));
    }

    /// <summary>The engine gate — the preview host binds its display to it.</summary>
    public ObsEngine Engine => _engine;

    /// <summary>Load-on-navigate hook — ObsViewModel awaits this on every OBS navigation.</summary>
    public async Task InitializeAsync()
    {
        // GetStatus blocks while init/shutdown holds the gate — keep that off
        // the UI thread (a background boot can outlast the navigation).
        ApplyEngineStatus(await Task.Run(_engine.GetStatus));
        if (_engineState == ObsEngineState.NotInitialized)
        {
            string? autoInit = await _dataService.GetConfigValueAsync(AutoInitKey);
            if (!string.Equals(autoInit, "false", StringComparison.OrdinalIgnoreCase))
            {
                await InitializeEngineAsync();
            }
        }
    }

    // ------------------------------------------------------------------
    // Engine lifecycle
    // ------------------------------------------------------------------

    private bool CanInitializeEngine() => !IsEngineReady && !IsInitializing;

    [RelayCommand(CanExecute = nameof(CanInitializeEngine))]
    private async Task InitializeEngineAsync()
    {
        try
        {
            ApplyEngineStatus(await _engine.InitializeAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBS studio: engine initialization failed");
            StatusText = $"Engine initialization failed: {ex.Message}";
        }
    }

    /// <summary>Single funnel for engine state → UI text/flags. UI thread only.</summary>
    private void ApplyEngineStatus(ObsEngineStatus status)
    {
        _engineState = status.State;
        IsEngineReady = status.State == ObsEngineState.Ready;
        IsInitializing = status.State == ObsEngineState.Initializing;
        IsNotInstalled = status.State == ObsEngineState.NotInstalled;

        EngineStatusText = status.State switch
        {
            ObsEngineState.Ready => $"Ready — libobs {status.Version ?? "?"}",
            ObsEngineState.Initializing => "Initializing…",
            ObsEngineState.NotInitialized => "OBS engine not initialized.",
            ObsEngineState.NotInstalled => "OBS Studio not found on this machine.",
            ObsEngineState.Failed => $"Failed: {status.Error ?? "unknown error"}",
            ObsEngineState.Shutdown => "OBS engine shut down.",
            _ => status.State.ToString(),
        };

        if (IsEngineReady)
        {
            OnBecameReady();
        }
        else
        {
            _statsTimer.Stop();
        }
    }

    /// <summary>Ready transition: start polling, refresh scenes, auto-load the last collection once.</summary>
    private void OnBecameReady()
    {
        _readyAtUtc ??= DateTime.UtcNow;
        _statsTimer.Start();

        if (!_autoLoadAttempted)
        {
            _autoLoadAttempted = true;
            _ = OnBecameReadyAsync();
        }
        else
        {
            _ = RefreshScenesAsync();
        }
    }

    private async Task OnBecameReadyAsync()
    {
        LoadResolutionText();
        await RefreshScenesAsync();
        await AutoLoadLastCollectionAsync();
    }

    /// <summary>Profile video base→output, read once — no native calls, plain INI parsing.</summary>
    private void LoadResolutionText()
    {
        try
        {
            if (_locator.Locate() is not { } install)
            {
                ResolutionText = null;
                return;
            }

            var video = new ObsProfileReader(install).ReadCurrentProfile().Settings?.Video;
            ResolutionText = video is null
                ? null
                : $"{video.BaseWidth}x{video.BaseHeight} → {video.OutputWidth}x{video.OutputHeight}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: could not read the profile video settings");
            ResolutionText = null;
        }
    }

    /// <summary>Engine Ready: reload the collection imported last (config path), then select its program scene.</summary>
    private async Task AutoLoadLastCollectionAsync()
    {
        try
        {
            string? path = await _dataService.GetConfigValueAsync(LastCollectionPathKey);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            string json = await File.ReadAllTextAsync(path, Encoding.UTF8);
            ObsLoadResult result = await _studio.LoadCollectionAsync(json);
            ApplyLoadResult(result, $"Loaded '{Path.GetFileName(path)}'");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: auto-loading the last scene collection failed");
            StatusText = $"Auto-loading the last collection failed: {ex.Message}";
        }
    }

    /// <summary>Shared post-load bookkeeping: missing plugins, status line, program scene.</summary>
    private async Task ApplyLoadResult(ObsLoadResult result, string prefix)
    {
        MissingSources.Clear();
        foreach (string missing in result.MissingSources)
        {
            MissingSources.Add(missing);
        }

        HasMissingSources = MissingSources.Count > 0;
        MissingSourcesText = HasMissingSources
            ? $"Missing plugins: {string.Join(", ", MissingSources)}"
            : null;
        StatusText = result.Success ? $"{prefix} — {result.Message}" : result.Message;

        if (!result.Success)
        {
            return;
        }

        _currentSceneName = result.CurrentScene;
        await RefreshScenesAsync();
    }

    // ------------------------------------------------------------------
    // Scenes + sources
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task RefreshScenesAsync()
    {
        try
        {
            var scenes = await _studio.GetScenesAsync();
            Scenes.Clear();
            foreach (var scene in scenes)
            {
                Scenes.Add(new ObsSceneRow { Name = scene.Name });
            }

            // The running instance has no queryable "current scene" beyond what
            // we set/loaded — fall back to the first scene.
            ObsSceneRow? current =
                Scenes.FirstOrDefault(s => s.Name == _currentSceneName) ??
                Scenes.FirstOrDefault();
            if (current is not null)
            {
                current.IsCurrent = true;
                SelectedScene = current;
                _currentSceneName = current.Name;
                await RefreshItemsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: refreshing scenes failed");
            StatusText = $"Refreshing scenes failed: {ex.Message}";
        }
    }

    partial void OnSelectedSceneChanged(ObsSceneRow? value)
    {
        if (value is { IsCurrent: false })
        {
            _ = SelectSceneAsync(value);
        }
    }

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task SelectSceneAsync(ObsSceneRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            await _studio.SetCurrentSceneAsync(row.Name);
            _currentSceneName = row.Name;
            foreach (ObsSceneRow scene in Scenes)
            {
                scene.IsCurrent = scene == row;
            }

            await RefreshItemsAsync();
            StatusText = $"Scene '{row.Name}' is live.";
            _logger.LogInformation("OBS studio: program scene switched to {Scene}", row.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: switching to scene {Scene} failed", row.Name);
            StatusText = $"Switching to '{row.Name}' failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task RefreshItemsAsync()
    {
        if (_currentSceneName is null)
        {
            return;
        }

        try
        {
            var items = await _studio.GetSceneItemsAsync(_currentSceneName);
            // Defensive count — an empty native enumeration is invisible to the
            // user (blank Sources panel) otherwise.
            _logger.LogInformation("OBS studio: scene {Scene} exposes {Count} source(s)", _currentSceneName, items.Count);
            SceneSources.Clear();
            foreach (var item in items)
            {
                SceneSources.Add(new ObsSourceRow
                {
                    ItemPtr = item.Item,
                    SourceName = item.SourceName,
                    SourceId = item.SourceId,
                    Visible = item.Visible,
                    Locked = item.Locked,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: refreshing the items of {Scene} failed", _currentSceneName);
            StatusText = $"Refreshing the sources failed: {ex.Message}";
        }
    }

    // Checkbox toggles its row before the command runs (ChatView pattern) —
    // the row already carries the new value plus the native item pointer.
    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task SetItemVisibleAsync(ObsSourceRow? row)
    {
        if (row is null || _currentSceneName is null)
        {
            return;
        }

        try
        {
            if (!await _studio.SetItemVisibleAsync(_currentSceneName, row.SourceName, row.Visible))
            {
                StatusText = $"'{row.SourceName}' no longer exists in the running scene.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: toggling visibility of {Source} failed", row.SourceName);
            StatusText = $"Toggling '{row.SourceName}' failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task SetItemLockedAsync(ObsSourceRow? row)
    {
        if (row is null || _currentSceneName is null)
        {
            return;
        }

        try
        {
            if (!await _studio.SetItemLockedAsync(_currentSceneName, row.SourceName, row.Locked))
            {
                StatusText = $"'{row.SourceName}' no longer exists in the running scene.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: toggling the lock of {Source} failed", row.SourceName);
            StatusText = $"Locking '{row.SourceName}' failed: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // Scene collection from file
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task LoadCollectionFromFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load an OBS scene collection into the studio",
            Filter = "OBS scene collection (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
            ObsLoadResult result = await _studio.LoadCollectionAsync(json);
            await ApplyLoadResult(result, $"Loaded '{Path.GetFileName(dialog.FileName)}'");

            if (result.Success)
            {
                await _dataService.SetConfigValueAsync(LastCollectionPathKey, dialog.FileName, ConfigCategory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBS studio: loading {FilePath} failed", dialog.FileName);
            StatusText = $"Loading the collection failed: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // Outputs
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task ToggleStreamAsync()
    {
        var result = StreamActive
            ? await _outputs.StopStreamAsync()
            : await _outputs.StartStreamAsync();
        StatusText = result.Message;
        await PollOutputsAsync();
    }

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task ToggleRecordAsync()
    {
        var result = RecordActive
            ? await _outputs.StopRecordAsync()
            : await _outputs.StartRecordAsync();
        StatusText = result.Message;
        await PollOutputsAsync();
    }

    [RelayCommand(CanExecute = nameof(IsEngineReady))]
    private async Task ToggleReplayAsync()
    {
        var result = ReplayActive
            ? await _outputs.StopReplayBufferAsync()
            : await _outputs.StartReplayBufferAsync();
        StatusText = result.Message;
        await PollOutputsAsync();
    }

    private bool CanSaveReplay() => ReplayActive;

    [RelayCommand(CanExecute = nameof(CanSaveReplay))]
    private async Task SaveReplayAsync()
    {
        var result = await _outputs.SaveReplayAsync();
        ReplaySavedText = result.Message;
        StatusText = result.Message;
        await PollOutputsAsync();
    }

    // ------------------------------------------------------------------
    // 1 Hz stats polling (started when the engine reaches Ready)
    // ------------------------------------------------------------------

    private async void OnStatsTick(object? sender, EventArgs e)
    {
        // Ticks overlap when a stop drain (up to 7 s) outlasts the interval.
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            await PollOutputsAsync();
            await PollEngineStatsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: stats poll failed");
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task PollOutputsAsync()
    {
        try
        {
            foreach (var state in await _outputs.GetStatesAsync())
            {
                // Reconnecting folds into the same "coming up" flag — the
                // button must stay in its Stop state either way.
                switch (state.Kind)
                {
                    case ObsOutputKind.Stream:
                        StreamActive = state.Active;
                        StreamStarting = state.Starting || state.Reconnecting;
                        break;
                    case ObsOutputKind.Record:
                        RecordActive = state.Active;
                        RecordStarting = state.Starting || state.Reconnecting;
                        break;
                    case ObsOutputKind.ReplayBuffer:
                        ReplayActive = state.Active;
                        ReplayStarting = state.Starting || state.Reconnecting;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS studio: output states poll failed");
        }
    }

    private async Task PollEngineStatsAsync()
    {
        if (_readyAtUtc is { } readyAt)
        {
            UptimeText = (DateTime.UtcNow - readyAt).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        try
        {
            ulong frameTimeNs = await _engine.GetAverageFrameTimeNsAsync();
            FpsText = frameTimeNs > 0
                ? $"{1_000_000_000.0 / frameTimeNs:0} fps"
                : null;
        }
        catch (Exception ex)
        {
            // Stats are decorative — a native hiccup must not disturb the poll loop.
            _logger.LogDebug(ex, "OBS studio: fps probe failed");
        }
    }

    /// <summary>
    /// MainViewModel drops the OBS page on every navigation away. Without this
    /// the 1 Hz stats timer kept polling the native engine gate for the whole
    /// process lifetime. The Tick subscription stays (Dispose only stops the
    /// timer) so a later OBS navigation re-arms it via <see cref="OnBecameReady"/>.
    /// </summary>
    public void Dispose()
    {
        _statsTimer.Stop();
    }
}

/// <summary>One scene of the running libobs instance; IsCurrent drives the live highlight.</summary>
public partial class ObsSceneRow : ObservableObject
{
    public string Name { get; init; } = "";

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>
/// One scene item of the displayed scene. <see cref="ItemPtr"/> is the native
/// obs_sceneitem_t* captured at enumeration (the studio service re-resolves
/// through its cache, so the row only carries it for diagnostics).
/// </summary>
public partial class ObsSourceRow : ObservableObject
{
    public IntPtr ItemPtr { get; init; }
    public string SourceName { get; init; } = "";
    public string SourceId { get; init; } = "";

    [ObservableProperty]
    private bool _visible;

    [ObservableProperty]
    private bool _locked;
}
