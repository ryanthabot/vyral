using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.UI.Services.VyralDeck;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// StreamDeck page: a customizable button grid (like a hardware Stream Deck)
/// that triggers Vyral's existing stream tools, plus the LAN server that the
/// VyralDeck mobile companion app pairs with to press and edit the same deck.
/// The page VM is a singleton: the server keeps running when the page closes.
/// </summary>
public partial class StreamDeckViewModel : ViewModelBase, IDisposable
{
    private static readonly string[] ColorChoicesData =
    [
        "#14B8A6", // teal (app accent)
        "#2EA043", // green
        "#DA3633", // red
        "#F0883E", // orange
        "#9146FF", // purple (Twitch)
        "#388BFD", // blue
        "#DB2777", // pink
        "#EAC54F", // gold
        "#8B949E", // grey
    ];

    private readonly ILogger<StreamDeckViewModel> _logger;
    private readonly DeckLayoutStore _layoutStore;
    private readonly DeckPairingStore _pairingStore;
    private readonly DeckServer _server;
    private readonly DeckActionExecutor _executor;
    private readonly IStreamLiveRegistry _liveRegistry;

    /// <summary>Suppresses the LayoutChanged reload while we are the ones saving.</summary>
    private bool _suppressLayoutReload;

    private DeckButtonViewModel? _editingButton;
    private DeckPageViewModel? _editingPage;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private string _serverStatusText = "Server stopped";

    [ObservableProperty]
    private string _portText = DeckServer.DefaultPort.ToString();

    [ObservableProperty]
    private string _lanAddressText = "";

    [ObservableProperty]
    private string _pairingPin = "------";

    [ObservableProperty]
    private bool _autoStartOnLaunch;

    [ObservableProperty]
    private int _clientsCount;

    [ObservableProperty]
    private string _lastResultText = "";

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isTwitchLive;

    [ObservableProperty]
    private bool _isKickLive;

    [ObservableProperty]
    private bool _isYouTubeLive;

    // ── editor fields ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _editLabel = "";

    [ObservableProperty]
    private string _editIcon = "";

    [ObservableProperty]
    private string _editColorHex = ColorChoicesData[0];

    [ObservableProperty]
    private DeckActionInfo _editAction = DeckActionCatalog.Actions[0];

    [ObservableProperty]
    private string _editParam1 = "";

    [ObservableProperty]
    private string _editParam2 = "";

    public ObservableCollection<DeckPageViewModel> Pages { get; } = new();

    [ObservableProperty]
    private DeckPageViewModel? _selectedPage;

    public ObservableCollection<PairedDevice> PairedDevices { get; } = new();

    public IReadOnlyList<string> ColorChoices => ColorChoicesData;

    public IReadOnlyList<DeckActionInfo> ActionTypes => DeckActionCatalog.Actions;

    public StreamDeckViewModel(
        ILogger<StreamDeckViewModel> logger,
        DeckLayoutStore layoutStore,
        DeckPairingStore pairingStore,
        DeckServer server,
        DeckActionExecutor executor,
        IStreamLiveRegistry liveRegistry)
    {
        _logger = logger;
        _layoutStore = layoutStore;
        _pairingStore = pairingStore;
        _server = server;
        _executor = executor;
        _liveRegistry = liveRegistry;

        _server.IsRunningChanged += OnServerIsRunningChanged;
        _server.ClientsChanged += OnServerClientsChanged;
        _server.LogLine += OnServerLogLine;
        _layoutStore.LayoutChanged += OnLayoutChangedExternal;

        RefreshLiveState();
        _liveRegistry.StreamLiveChanged += OnLiveChanged;
    }

    public async Task InitializeAsync()
    {
        var settings = await _pairingStore.GetSettingsAsync().ConfigureAwait(true);
        PortText = settings.Port.ToString();
        AutoStartOnLaunch = settings.AutoStartOnLaunch;
        LanAddressText = _server.IsRunning ? $"{_server.LanAddress}:{_server.Port}" : _server.LanAddress;

        await ReloadPagesAsync().ConfigureAwait(true);
        await ReloadPairedDevicesAsync().ConfigureAwait(true);

        if (_server.IsRunning)
        {
            IsServerRunning = true;
            ServerStatusText = $"Server running on {LanAddressText} — {_server.ClientCount} client(s).";
            // A PIN from an earlier session (singleton store) may still be live.
            PairingPin = _pairingStore.CurrentPinDisplay;
        }
        else if (AutoStartOnLaunch)
        {
            await StartServerAsync().ConfigureAwait(true);
        }
        else
        {
            PairingPin = _pairingStore.CurrentPinDisplay;
        }
    }

    public void Dispose()
    {
        _server.IsRunningChanged -= OnServerIsRunningChanged;
        _server.ClientsChanged -= OnServerClientsChanged;
        _server.LogLine -= OnServerLogLine;
        _layoutStore.LayoutChanged -= OnLayoutChangedExternal;
        _liveRegistry.StreamLiveChanged -= OnLiveChanged;
    }

    // ── server commands ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (!int.TryParse(PortText, out var port) || port is < 1024 or > 65535)
        {
            ServerStatusText = "Port must be a number between 1024 and 65535.";
            return;
        }

        ServerStatusText = "Starting server…";
        var ok = await _server.StartAsync(port).ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        await SaveServerSettingsAsync().ConfigureAwait(true);
        PairingPin = _pairingStore.NewPin();
        LanAddressText = $"{_server.LanAddress}:{_server.Port}";
        OnServerIsRunningChanged(this, true);
        LastResultText = $"Phone setup: connect to {LanAddressText}, then pair with PIN {PairingPin}.";
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        await _server.StopAsync().ConfigureAwait(true);
        PairingPin = "------";
        OnServerIsRunningChanged(this, false);
    }

    [RelayCommand]
    private void RegeneratePin()
    {
        if (!IsServerRunning)
        {
            ServerStatusText = "Start the server first — the PIN is only needed while a phone can reach it.";
            return;
        }

        PairingPin = _pairingStore.NewPin();
        LastResultText = $"New pairing PIN: {PairingPin}";
    }

    [RelayCommand]
    private async Task RevokeDeviceAsync(PairedDevice device)
    {
        await _pairingStore.RemoveDeviceAsync(device.Token).ConfigureAwait(true);
        await ReloadPairedDevicesAsync().ConfigureAwait(true);
        LastResultText = $"Unpaired '{device.Name}'.";
    }

    [RelayCommand]
    private async Task ToggleAutoStartAsync()
    {
        await SaveServerSettingsAsync().ConfigureAwait(true);
    }

    private async Task SaveServerSettingsAsync()
    {
        var settings = new DeckServerSettings
        {
            Port = int.TryParse(PortText, out var port) ? port : DeckServer.DefaultPort,
            AutoStartOnLaunch = AutoStartOnLaunch,
        };
        await _pairingStore.SaveSettingsAsync(settings).ConfigureAwait(true);
    }

    private void OnServerIsRunningChanged(object? sender, bool running)
    {
        IsServerRunning = running;
        ServerStatusText = running
            ? $"Server running on {LanAddressText} — {_server.ClientCount} client(s)."
            : "Server stopped";
    }

    private void OnServerClientsChanged(object? sender, int count)
    {
        ClientsCount = count;

        // Sessions changed — refresh the paired-device list so each row's
        // "connected" badge reflects reality. (Dispatched internally.)
        _ = ReloadPairedDevicesAsync();
    }

    private void OnServerLogLine(string message) => ServerStatusText = message;

    private async void OnLayoutChangedExternal(DeckLayout layout) =>
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (_suppressLayoutReload)
            {
                return;
            }

            // A phone saved a new layout — refresh the grid in place.
            await ReloadPagesFromAsync(layout);
        });

    // ── deck commands ────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectPage(DeckPageViewModel page)
    {
        SelectedPage = page;
    }

    [RelayCommand]
    private async Task AddPageAsync()
    {
        var layout = _layoutStore.Cached;
        var page = DeckPage.CreateDefault($"Page {layout.Pages.Count + 1}");
        layout.Pages.Add(page);
        await PersistLayoutAsync(layout).ConfigureAwait(true);
        await ReloadPagesFromAsync(layout);
        SelectedPage = Pages.LastOrDefault();
    }

    [RelayCommand]
    private async Task DeletePageAsync(DeckPageViewModel? page)
    {
        if (page is null || Pages.Count <= 1)
        {
            LastResultText = Pages.Count <= 1
                ? "The deck needs at least one page."
                : "Select a page first.";
            return;
        }

        var layout = _layoutStore.Cached;
        var model = layout.Pages.FirstOrDefault(p => p.Id == page.PageId);
        if (model is not null)
        {
            layout.Pages.Remove(model);
            await PersistLayoutAsync(layout).ConfigureAwait(true);
            await ReloadPagesFromAsync(layout);
            SelectedPage = Pages.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task MovePageLeftAsync() => await MovePageAsync(-1).ConfigureAwait(true);

    [RelayCommand]
    private async Task MovePageRightAsync() => await MovePageAsync(1).ConfigureAwait(true);

    /// <summary>Reorders pages (persisted + rebroadcast to phones like every edit).</summary>
    private async Task MovePageAsync(int delta)
    {
        var layout = _layoutStore.Cached;
        var index = layout.Pages.FindIndex(p => p.Id == SelectedPage?.PageId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= layout.Pages.Count)
        {
            return;
        }

        var page = layout.Pages[index];
        layout.Pages.RemoveAt(index);
        layout.Pages.Insert(target, page);
        await PersistLayoutAsync(layout).ConfigureAwait(true);
        RebuildPages(layout);
        SelectedPage = Pages.FirstOrDefault(p => p.PageId == page.Id);
    }

    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
        if (!IsEditMode)
        {
            CancelEdit();
        }

        LastResultText = IsEditMode
            ? "Edit mode: tap a slot to configure it."
            : "";
    }

    /// <summary>The grid tap: runs the button, or opens the editor in edit mode.</summary>
    [RelayCommand]
    private async Task TapButtonAsync(DeckButtonViewModel button)
    {
        if (IsEditMode)
        {
            BeginEdit(button);
            return;
        }

        if (button.IsEmpty)
        {
            LastResultText = $"Slot {button.Slot + 1} is empty — turn on Edit Mode to configure it.";
            return;
        }

        var result = await _executor.ExecuteAsync(button.Model).ConfigureAwait(true);
        LastResultText = result.Message;
    }

    [RelayCommand]
    private void PickColor(string colorHex)
    {
        EditColorHex = colorHex;
    }

    partial void OnEditActionChanged(DeckActionInfo value)
    {
        // Param boxes only make sense for the selected action's parameters.
        if (value.Param1Label is null)
        {
            EditParam1 = "";
        }

        if (value.Param2Label is null)
        {
            EditParam2 = "";
        }

        OnPropertyChanged(nameof(HasParam1));
        OnPropertyChanged(nameof(HasParam2));
    }

    public bool HasParam1 => EditAction.Param1Label is not null;

    public bool HasParam2 => EditAction.Param2Label is not null;

    private void BeginEdit(DeckButtonViewModel button)
    {
        _editingButton = button;
        _editingPage = SelectedPage;
        var model = button.Model;

        EditLabel = model.Label;
        EditIcon = model.Icon;
        EditColorHex = model.IsEmpty ? ColorChoicesData[0] : model.ColorHex;
        EditAction = DeckActionCatalog.Info(model.ActionType);
        EditParam1 = model.Param1;
        EditParam2 = model.Param2;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveButtonAsync()
    {
        if (_editingButton?.Model is not { } model || _editingPage is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EditLabel))
        {
            LastResultText = "Give the button a label (or clear the slot instead).";
            return;
        }

        model.Label = EditLabel.Trim();
        model.Icon = EditIcon.Trim();
        model.ColorHex = EditColorHex;
        model.ActionType = EditAction.Type;
        model.Param1 = HasParam1 ? EditParam1.Trim() : "";
        model.Param2 = HasParam2 ? EditParam2.Trim() : "";

        await PersistLayoutAsync(_layoutStore.Cached).ConfigureAwait(true);
        RefreshRow(_editingPage, model);
        CancelEdit();
        LastResultText = $"Saved '{model.Label}'.";
    }

    [RelayCommand]
    private async Task ClearSlotAsync()
    {
        if (_editingButton?.Model is not { } model || _editingPage is null)
        {
            return;
        }

        var fresh = new DeckButton();
        model.Id = fresh.Id;
        model.Label = "";
        model.Icon = "";
        model.ColorHex = fresh.ColorHex;
        model.ActionType = fresh.ActionType;
        model.Param1 = "";
        model.Param2 = "";

        await PersistLayoutAsync(_layoutStore.Cached).ConfigureAwait(true);
        RefreshRow(_editingPage, model);
        CancelEdit();
        LastResultText = $"Slot {_editingButton.Slot + 1} cleared.";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        _editingButton = null;
        _editingPage = null;
    }

    // ── data loading ─────────────────────────────────────────────────────

    private async Task ReloadPagesAsync()
    {
        var layout = await _layoutStore.LoadAsync().ConfigureAwait(true);
        await Application.Current.Dispatcher.InvokeAsync(() => RebuildPages(layout));
    }

    private async Task ReloadPagesFromAsync(DeckLayout layout)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Pages.Count != layout.Pages.Count)
            {
                RebuildPages(layout);
                return;
            }

            for (var i = 0; i < Pages.Count; i++)
            {
                Pages[i].RefreshFrom(layout.Pages[i]);
            }
        });
    }

    private void RebuildPages(DeckLayout layout)
    {
        var selectedId = SelectedPage?.PageId;
        Pages.Clear();
        foreach (var page in layout.Pages)
        {
            Pages.Add(new DeckPageViewModel(page));
        }

        SelectedPage = Pages.FirstOrDefault(p => p.PageId == selectedId) ?? Pages.FirstOrDefault();
    }

    private async Task ReloadPairedDevicesAsync()
    {
        var devices = await _pairingStore.GetDevicesAsync().ConfigureAwait(true);
        foreach (var device in devices)
        {
            device.IsConnected = _server.IsDeviceConnected(device.Token);
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            PairedDevices.Clear();
            foreach (var device in devices)
            {
                PairedDevices.Add(device);
            }
        });
    }

    private async Task PersistLayoutAsync(DeckLayout layout)
    {
        _suppressLayoutReload = true;
        try
        {
            await _layoutStore.SaveAsync(layout).ConfigureAwait(true);
        }
        finally
        {
            _suppressLayoutReload = false;
        }
    }

    private static void RefreshRow(DeckPageViewModel page, DeckButton model)
    {
        var row = page.Buttons.FirstOrDefault(b => b.ButtonId == model.Id);
        row?.RefreshFrom(model);
    }

    private void OnLiveChanged(object? sender, string platform) => RefreshLiveState();

    private void RefreshLiveState()
    {
        IsTwitchLive = _liveRegistry.IsLive("Twitch");
        IsKickLive = _liveRegistry.IsLive("Kick");
        IsYouTubeLive = _liveRegistry.IsLive("YouTube");
    }
}
