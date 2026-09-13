using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.Integrations.Restream;

namespace VyralBot.UI.ViewModels;

/// <summary>A displayable choice for ComboBox binding (renders <see cref="Label"/>).</summary>
public sealed record RestreamChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Serializable, non-secret source settings stored in the config KV (category "Restream").</summary>
public sealed record RestreamSettingsDto(
    RestreamOperationMode Mode,
    int IngestPort,
    string IngestStreamKey,
    string? FfmpegPathOverride)
{
    public RestreamSettingsDto() : this(
        RestreamOperationMode.SinglePC, DefaultPort, DefaultStreamKey, null) { }

    public const int DefaultPort = 1935;
    public const string DefaultStreamKey = "vyral";
}

/// <summary>One editable destination row. Maps to/from <see cref="RestreamDestination"/>.</summary>
public partial class RestreamDestinationItemViewModel : ObservableObject
{
    // The choice lists live as static data, but WPF's {Binding} cannot resolve
    // static properties against a row instance — the XAML binds these instance
    // getters, which just return the shared arrays.
    private static readonly IReadOnlyList<string> PlatformChoicesData = new[] { "Twitch", "YouTube", "Kick", "Custom" };

    private static readonly IReadOnlyList<RestreamChoice<RestreamResolution>> ResolutionChoicesData = new[]
    {
        new RestreamChoice<RestreamResolution>(RestreamResolution.Source, "Source (no re-encode)"),
        new RestreamChoice<RestreamResolution>(RestreamResolution.P1080, "1080p"),
        new RestreamChoice<RestreamResolution>(RestreamResolution.P936, "936p"),
        new RestreamChoice<RestreamResolution>(RestreamResolution.P720, "720p"),
        new RestreamChoice<RestreamResolution>(RestreamResolution.P540, "540p"),
        new RestreamChoice<RestreamResolution>(RestreamResolution.P360, "360p"),
    };

    private static readonly IReadOnlyList<string> PresetChoicesData = new[] { "ultrafast", "superfast", "veryfast", "faster" };

    private static readonly IReadOnlyList<RestreamChoice<RestreamAudioMode>> AudioChoicesData = new[]
    {
        new RestreamChoice<RestreamAudioMode>(RestreamAudioMode.Copy, "Copy (free)"),
        new RestreamChoice<RestreamAudioMode>(RestreamAudioMode.A128k, "AAC 128k"),
        new RestreamChoice<RestreamAudioMode>(RestreamAudioMode.A160k, "AAC 160k"),
    };

    public IReadOnlyList<string> PlatformChoices => PlatformChoicesData;

    public IReadOnlyList<RestreamChoice<RestreamResolution>> ResolutionChoices => ResolutionChoicesData;

    public IReadOnlyList<string> PresetChoices => PresetChoicesData;

    public IReadOnlyList<RestreamChoice<RestreamAudioMode>> AudioChoices => AudioChoicesData;

    private static string DefaultServer(string platform) => platform switch
    {
        "Twitch" => "rtmp://live.twitch.tv/app",
        "YouTube" => "rtmp://a.rtmp.youtube.com/live2",
        _ => "",
    };

    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "Destination";

    [ObservableProperty]
    private string _platform = "Twitch";

    [ObservableProperty]
    private string _serverUrl = "rtmp://live.twitch.tv/app";

    [ObservableProperty]
    private string _streamKey = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScaled))]
    private RestreamChoice<RestreamResolution> _resolution = ResolutionChoicesData[0];

    // Scaled rows show the bitrate/preset fields; Source rows hide them.
    public bool IsScaled => Resolution.Value != RestreamResolution.Source;

    [ObservableProperty]
    private string _bitrateText = "6000";

    [ObservableProperty]
    private string _preset = "veryfast";

    [ObservableProperty]
    private RestreamChoice<RestreamAudioMode> _audioMode = AudioChoicesData[0];

    [ObservableProperty]
    private bool _enabled = true;

    // Runtime health mirrored from the engine (set by the page ViewModel).
    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isStatusError;

    partial void OnPlatformChanged(string value)
    {
        // Only platforms with a well-known ingest get an autofill — Kick/Custom
        // paste a per-account URL from the platform dashboard.
        var server = DefaultServer(value);
        if (server.Length > 0)
        {
            ServerUrl = server;
        }
    }

    partial void OnResolutionChanged(RestreamChoice<RestreamResolution> value)
    {
        if (value.Value != RestreamResolution.Source)
        {
            BitrateText = RestreamArgumentBuilder.DefaultBitrateKbps(value.Value).ToString();
        }
    }

    public RestreamDestination ToModel() => new(
        Id: Id,
        Name: string.IsNullOrWhiteSpace(Name) ? Platform : Name.Trim(),
        Platform: Platform,
        ServerUrl: ServerUrl.Trim(),
        StreamKey: StreamKey.Trim(),
        Resolution: Resolution.Value,
        VideoBitrateKbps: int.TryParse(BitrateText.Trim(), out var kbps) && kbps > 0 ? kbps : 6000,
        EncoderPreset: string.IsNullOrWhiteSpace(Preset) ? "veryfast" : Preset,
        AudioMode: AudioMode.Value,
        Enabled: Enabled);

    public static RestreamDestinationItemViewModel FromModel(RestreamDestination model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Platform = model.Platform,
        ServerUrl = model.ServerUrl,
        StreamKey = model.StreamKey,
        Resolution = ResolutionChoicesData.First(c => c.Value == model.Resolution),
        BitrateText = model.VideoBitrateKbps.ToString(),
        Preset = PresetChoicesData.Contains(model.EncoderPreset) ? model.EncoderPreset : "veryfast",
        AudioMode = AudioChoicesData.First(c => c.Value == model.AudioMode),
        Enabled = model.Enabled,
    };
}

/// <summary>
/// Restream page: local FFmpeg ingest that fans one OBS output out to multiple
/// platforms, each at its own resolution/bitrate. Works single-PC (OBS on this
/// machine publishes to loopback) and two-PC (a gaming PC publishes over the
/// LAN; VyralBot only runs here). The engine keeps streaming when the page is
/// closed — only the uptime ticker stops.
/// </summary>
public partial class RestreamViewModel : ViewModelBase, IDisposable
{
    private const string SettingsConfigKey = "Restream.Settings";
    private const string DestinationsFileName = "restream_destinations.json";
    private const int MaxLogLines = 300;

    private static readonly JsonSerializerOptions PersistenceJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<RestreamViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IProfileService _profileService;
    private readonly RestreamEngine _engine;
    private readonly RestreamFfmpegLocator _ffmpegLocator;
    private readonly RestreamFfmpegAcquisitionService _ffmpegAcquisition;
    private readonly DispatcherTimer _uptimeTimer;

    [ObservableProperty]
    private bool _isTwoPcMode;

    [ObservableProperty]
    private string _ingestPortText = RestreamSettingsDto.DefaultPort.ToString();

    [ObservableProperty]
    private string _ingestStreamKey = RestreamSettingsDto.DefaultStreamKey;

    [ObservableProperty]
    private string _ffmpegPathOverride = "";

    [ObservableProperty]
    private string _ffmpegStatusText = "checking for FFmpeg…";

    [ObservableProperty]
    private bool _isFfmpegFound;

    [ObservableProperty]
    private bool _isAcquiringFfmpeg;

    [ObservableProperty]
    private string _acquireDetail = "";

    [ObservableProperty]
    private int _acquirePercent;

    [ObservableProperty]
    private bool _isEngineRunning;

    [ObservableProperty]
    private bool _isEngineStreaming;

    [ObservableProperty]
    private string _engineStateText = "Stopped";

    [ObservableProperty]
    private string _engineMessage = "";

    [ObservableProperty]
    private string _uptimeText = "—";

    [ObservableProperty]
    private string _bitrateText = "—";

    [ObservableProperty]
    private string _fpsText = "—";

    [ObservableProperty]
    private string _speedText = "—";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isDestinationsEmpty = true;

    [ObservableProperty]
    private bool _canDownloadFfmpeg = true;

    public ObservableCollection<RestreamDestinationItemViewModel> Destinations { get; } = new();

    public ObservableCollection<string> IngestUrls { get; } = new();

    public ObservableCollection<string> LogLines { get; } = new();

    public RestreamViewModel(
        ILogger<RestreamViewModel> logger,
        IDataService dataService,
        IProfileService profileService,
        RestreamEngine engine,
        RestreamFfmpegLocator ffmpegLocator,
        RestreamFfmpegAcquisitionService ffmpegAcquisition)
    {
        _logger = logger;
        _dataService = dataService;
        _profileService = profileService;
        _engine = engine;
        _ffmpegLocator = ffmpegLocator;
        _ffmpegAcquisition = ffmpegAcquisition;

        _engine.StatusChanged += OnEngineStatusChanged;
        _engine.LogLine += OnEngineLogLine;

        Destinations.CollectionChanged += (_, _) => IsDestinationsEmpty = Destinations.Count == 0;

        _uptimeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _uptimeTimer.Tick += (_, _) => RefreshUptime();

        // Reflect a session that is already running when the page opens.
        if (_engine.IsRunning)
        {
            ApplyEngineStatus(_engine.Status);
        }
    }

    public async Task InitializeAsync()
    {
        _uptimeTimer.Start();

        try
        {
            var settingsJson = await _dataService.GetConfigValueAsync(SettingsConfigKey);
            if (!string.IsNullOrEmpty(settingsJson)
                && JsonSerializer.Deserialize<RestreamSettingsDto>(settingsJson, PersistenceJson) is { } settings)
            {
                IsTwoPcMode = settings.Mode == RestreamOperationMode.TwoPC;
                IngestPortText = settings.IngestPort > 0 ? settings.IngestPort.ToString() : RestreamSettingsDto.DefaultPort.ToString();
                IngestStreamKey = string.IsNullOrWhiteSpace(settings.IngestStreamKey)
                    ? RestreamSettingsDto.DefaultStreamKey
                    : settings.IngestStreamKey;
                FfmpegPathOverride = settings.FfmpegPathOverride ?? "";
            }

            await LoadDestinationsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading restream settings");
            StatusMessage = "Could not load saved restream settings.";
        }

        RefreshIngestUrls();
        await RefreshFfmpegStatusAsync();
    }

    private async Task LoadDestinationsAsync()
    {
        Destinations.Clear();

        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            if (profile is null)
            {
                return;
            }

            var path = Path.Combine(
                _profileService.GetProfileCredentialsPath(profile.Name),
                DestinationsFileName);

            if (!File.Exists(path))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            var destinations = JsonSerializer.Deserialize<List<RestreamDestination>>(json, PersistenceJson);
            if (destinations is null)
            {
                return;
            }

            foreach (var model in destinations)
            {
                Destinations.Add(RestreamDestinationItemViewModel.FromModel(model));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading restream destinations");
        }
    }

    private async Task SaveDestinationsAsync(IReadOnlyList<RestreamDestination> destinations)
    {
        var profile = await _profileService.GetCurrentProfileAsync();
        if (profile is null)
        {
            StatusMessage = "No profile is active — destinations could not be saved.";
            return;
        }

        var folder = _profileService.GetProfileCredentialsPath(profile.Name);
        Directory.CreateDirectory(folder);
        var json = JsonSerializer.Serialize(destinations, PersistenceJson);
        await File.WriteAllTextAsync(Path.Combine(folder, DestinationsFileName), json);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new RestreamSettingsDto(
                IsTwoPcMode ? RestreamOperationMode.TwoPC : RestreamOperationMode.SinglePC,
                ParsedPort(),
                string.IsNullOrWhiteSpace(IngestStreamKey) ? RestreamSettingsDto.DefaultStreamKey : IngestStreamKey.Trim(),
                string.IsNullOrWhiteSpace(FfmpegPathOverride) ? null : FfmpegPathOverride.Trim());

            await _dataService.SetConfigValueAsync(
                SettingsConfigKey,
                JsonSerializer.Serialize(settings, PersistenceJson),
                "Restream");

            await SaveDestinationsAsync(Destinations.Select(d => d.ToModel()).ToList());

            StatusMessage = "";
            _logger.LogInformation("Restream settings saved ({Count} destination(s))", Destinations.Count);
            MessageBox.Show(
                "Restream settings saved successfully!",
                "Settings Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving restream settings");
            MessageBox.Show(
                $"Failed to save restream settings: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void AddDestination(string platform)
    {
        var count = Destinations.Count(d => d.Platform == platform) + 1;
        var item = new RestreamDestinationItemViewModel
        {
            Platform = platform,
            Name = $"{platform} {count}",
        };
        Destinations.Add(item);
    }

    [RelayCommand]
    private void DeleteDestination(RestreamDestinationItemViewModel item)
    {
        Destinations.Remove(item);
    }

    [RelayCommand]
    private async Task StartRestreamAsync()
    {
        var install = await Task.Run(() => _ffmpegLocator.Locate(
            string.IsNullOrWhiteSpace(FfmpegPathOverride) ? null : FfmpegPathOverride.Trim()));
        if (install is null)
        {
            await RefreshFfmpegStatusAsync();
            StatusMessage = "FFmpeg was not found — download it below or set a custom path, then try again.";
            return;
        }

        var destinations = Destinations.Select(d => d.ToModel()).ToList();
        var active = destinations.Where(d => d.Enabled).ToList();
        if (active.Count == 0)
        {
            StatusMessage = "Add and enable at least one destination before starting.";
            return;
        }

        var invalid = active.FirstOrDefault(d => string.IsNullOrWhiteSpace(d.ServerUrl) || string.IsNullOrWhiteSpace(d.StreamKey));
        if (invalid is not null)
        {
            StatusMessage = $"'{invalid.Name}' needs both a server URL and a stream key.";
            return;
        }

        StatusMessage = "";
        var source = new RestreamSourceConfig(
            IsTwoPcMode ? RestreamOperationMode.TwoPC : RestreamOperationMode.SinglePC,
            ParsedPort(),
            string.IsNullOrWhiteSpace(IngestStreamKey) ? RestreamSettingsDto.DefaultStreamKey : IngestStreamKey.Trim());

        await _engine.StartAsync(new RestreamSessionConfig(install.FfmpegPath, source, destinations));
        _logger.LogInformation("Restream session started ({Mode}, {Count} destination(s))", source.Mode, active.Count);
    }

    [RelayCommand]
    private async Task StopRestreamAsync()
    {
        await _engine.StopAsync();
        _logger.LogInformation("Restream session stopped");
    }

    [RelayCommand]
    private async Task DownloadFfmpegAsync()
    {
        if (IsAcquiringFfmpeg)
        {
            return;
        }

        IsAcquiringFfmpeg = true;
        CanDownloadFfmpeg = false;
        AcquireDetail = "starting download…";
        AcquirePercent = 0;

        try
        {
            await foreach (var progress in _ffmpegAcquisition.DownloadAndInstallAsync())
            {
                AcquireDetail = progress.Detail;
                AcquirePercent = progress.Percent;
                if (progress.Phase == RestreamAcquirePhase.Failed)
                {
                    StatusMessage = progress.Detail;
                    _logger.LogError("FFmpeg download failed: {Detail}", progress.Detail);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg download failed");
            StatusMessage = $"FFmpeg download failed: {ex.Message}";
        }
        finally
        {
            IsAcquiringFfmpeg = false;
            CanDownloadFfmpeg = true;
            await RefreshFfmpegStatusAsync();
        }
    }

    [RelayCommand]
    private void CopyIngestUrl()
    {
        if (IngestUrls.Count > 0)
        {
            CopyToClipboard(IngestUrls[0]);
        }
    }

    [RelayCommand]
    private void CopyIngestKey()
    {
        CopyToClipboard(string.IsNullOrWhiteSpace(IngestStreamKey) ? RestreamSettingsDto.DefaultStreamKey : IngestStreamKey);
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // Clipboard can be locked by another process — non-fatal.
        }
    }

    private int ParsedPort() =>
        int.TryParse(IngestPortText.Trim(), out var port) && port is > 0 and < 65536
            ? port
            : RestreamSettingsDto.DefaultPort;

    partial void OnIsTwoPcModeChanged(bool value) => RefreshIngestUrls();

    partial void OnIngestPortTextChanged(string value) => RefreshIngestUrls();

    partial void OnIngestStreamKeyChanged(string value) => RefreshIngestUrls();

    private void RefreshIngestUrls()
    {
        IngestUrls.Clear();
        var key = string.IsNullOrWhiteSpace(IngestStreamKey) ? RestreamSettingsDto.DefaultStreamKey : IngestStreamKey.Trim();
        var port = ParsedPort();

        if (!IsTwoPcMode)
        {
            IngestUrls.Add($"rtmp://127.0.0.1:{port}/live/{key}");
            return;
        }

        foreach (var address in GetLanIPv4Addresses())
        {
            IngestUrls.Add($"rtmp://{address}:{port}/live/{key}");
        }

        if (IngestUrls.Count == 0)
        {
            // No LAN interface answered — still show the loopback form so the
            // field is never empty and the shape of the URL stays visible.
            IngestUrls.Add($"rtmp://<this-pc-ip>:{port}/live/{key}");
        }
    }

    private static List<string> GetLanIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString())
            .Distinct()
            .ToList();

    private async Task RefreshFfmpegStatusAsync()
    {
        // Locate() probes the binary with "ffmpeg -version" (up to a 5s hang
        // guard for a broken exe) — keep that off the UI thread.
        var install = await Task.Run(() => _ffmpegLocator.Locate(
            string.IsNullOrWhiteSpace(FfmpegPathOverride) ? null : FfmpegPathOverride.Trim()));
        if (install is null)
        {
            IsFfmpegFound = false;
            FfmpegStatusText = "FFmpeg not found";
            _logger.LogWarning(
                "FFmpeg not found (override: '{Override}', bundled path checked: {BundledPath})",
                string.IsNullOrWhiteSpace(FfmpegPathOverride) ? "<none>" : FfmpegPathOverride.Trim(),
                RestreamFfmpegLocator.BundledFfmpegPath);
            return;
        }

        IsFfmpegFound = true;
        var version = install.Version is { } line && line.StartsWith("ffmpeg version ", StringComparison.Ordinal)
            ? line["ffmpeg version ".Length..].Split(' ')[0]
            : null;
        FfmpegStatusText = version is null
            ? $"FFmpeg ready ({install.Source})"
            : $"FFmpeg ready — {version} ({install.Source})";
        _logger.LogInformation("FFmpeg located at {Path} ({Source})", install.FfmpegPath, install.Source);
    }

    partial void OnFfmpegPathOverrideChanged(string value) => _ = RefreshFfmpegStatusAsync();

    private void OnEngineStatusChanged(object? sender, RestreamEngineStatus status) =>
        Application.Current?.Dispatcher.InvokeAsync(() => ApplyEngineStatus(status));

    private void OnEngineLogLine(object? sender, string line) =>
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > MaxLogLines)
            {
                LogLines.RemoveAt(0);
            }
        });

    private void ApplyEngineStatus(RestreamEngineStatus status)
    {
        IsEngineRunning = status.State is RestreamSessionState.WaitingForSource or RestreamSessionState.Streaming;
        IsEngineStreaming = status.State == RestreamSessionState.Streaming;
        EngineStateText = status.State switch
        {
            RestreamSessionState.WaitingForSource => "Waiting for OBS",
            RestreamSessionState.Streaming => "Live",
            RestreamSessionState.Faulted => "Error",
            _ => "Stopped",
        };
        EngineMessage = status.Message;

        BitrateText = status.Stats is { } stats && stats.BitrateKbps > 0 ? $"{stats.BitrateKbps:0} kbps" : "—";
        FpsText = status.Stats is { } s2 && s2.Fps > 0 ? $"{s2.Fps:0.0} fps" : "—";
        SpeedText = status.Stats is { } s3 && s3.SpeedX > 0 ? $"{s3.SpeedX:0.00}×" : "—";
        RefreshUptime();

        var runtimeById = status.Destinations.ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var item in Destinations)
        {
            if (!runtimeById.TryGetValue(item.Id, out var runtime))
            {
                // Row is disabled or not part of this session — clear stale health.
                if (!IsEngineRunning)
                {
                    item.StatusText = null;
                    item.IsStatusError = false;
                }

                continue;
            }

            switch (runtime.Health)
            {
                case RestreamDestinationHealth.Active:
                    item.StatusText = "streaming";
                    item.IsStatusError = false;
                    break;

                case RestreamDestinationHealth.Error:
                    item.StatusText = $"error: {runtime.Detail}";
                    item.IsStatusError = true;
                    break;

                case RestreamDestinationHealth.AutoDisabled:
                    item.StatusText = "auto-disabled after repeated failures";
                    item.IsStatusError = true;
                    break;

                default:
                    item.StatusText = IsEngineStreaming ? "starting…" : null;
                    item.IsStatusError = false;
                    break;
            }
        }
    }

    private void RefreshUptime()
    {
        var status = _engine.Status;
        UptimeText = status.State == RestreamSessionState.Streaming && status.StreamingSince is { } since
            ? (DateTimeOffset.UtcNow - since).ToString(@"hh\:mm\:ss")
            : "—";
    }

    public void Dispose()
    {
        // Only page-scoped work stops here — the engine (and a live stream)
        // keeps running in the background by design.
        _uptimeTimer.Stop();
    }
}
