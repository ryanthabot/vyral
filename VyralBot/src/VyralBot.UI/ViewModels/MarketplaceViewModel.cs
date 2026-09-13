using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.UI.Services;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.ViewModels;

/// <summary>One marketplace section (category) with its filtered command rows.</summary>
public class MarketplaceCategoryGroup : ObservableObject
{
    public string Category { get; }

    public ObservableCollection<MarketplaceEntry> Entries { get; } = new();

    public MarketplaceCategoryGroup(string category) => Category = category;

    public string Header => char.ToUpperInvariant(Category[0]) + Category[1..];
}

/// <summary>
/// View model of the command marketplace overlay: lists shared commands from
/// GitHub (grouped by category), supports 👍👎 voting via BotGhost, importing
/// by listing row or pasted share/offline code, and holds the BotGhost
/// connection settings.
/// </summary>
public partial class MarketplaceViewModel : ObservableObject
{
    private readonly MarketplaceService _marketplace;
    private readonly ILogger<MarketplaceViewModel> _logger;

    /// <summary>Raised when the overlay should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Raised when a shared command was fetched + parsed and should be loaded
    /// into the flow editor as a fresh working copy.
    /// </summary>
    public event EventHandler<(FlowEditorSnapshotDto Snapshot, string Label)>? ImportRequested;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool hasEntries;

    /// <summary>Editable marketplace connection settings (BotGhost webhook + repo).</summary>
    [ObservableProperty]
    private MarketplaceSettings settings = new();

    [ObservableProperty]
    private bool isSettingsOpen;

    public ObservableCollection<MarketplaceCategoryGroup> Groups { get; } = new();

    private List<MarketplaceEntry> _allEntries = new();

    public MarketplaceViewModel(MarketplaceService marketplace, ILogger<MarketplaceViewModel> logger)
    {
        _marketplace = marketplace;
        _logger = logger;
    }

    /// <summary>Initial load (settings + listing). Safe to call repeatedly.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Loading marketplace…";
        try
        {
            Settings = await _marketplace.LoadSettingsAsync();
            _allEntries = (await _marketplace.ListAsync()).ToList();
            RebuildGroups();
            StatusText = _allEntries.Count == 0
                ? "No shared commands yet — be the first to share one."
                : $"{_allEntries.Count} shared command{(_allEntries.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace refresh failed");
            StatusText = $"Could not load the marketplace: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    private void RebuildGroups()
    {
        string query = SearchText?.Trim() ?? string.Empty;
        IEnumerable<MarketplaceEntry> filtered = _allEntries;
        if (query.Length > 0)
        {
            filtered = filtered.Where(e =>
                (e.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Sharecode?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var group in Groups)
        {
            group.Entries.Clear();
        }

        foreach (var entry in filtered)
        {
            var group = Groups.FirstOrDefault(g => string.Equals(g.Category, entry.Category, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new MarketplaceCategoryGroup(entry.Category);
                Groups.Add(group);
            }

            group.Entries.Add(entry);
        }

        // Keep the sections in the canonical order, drop empty ones.
        foreach (var group in Groups.Where(g => g.Entries.Count == 0).ToList())
        {
            Groups.Remove(group);
        }

        var ordered = Groups.OrderBy(g => Array.IndexOf(MarketplaceCategories.All, g.Category)).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (Groups.IndexOf(ordered[i]) != i)
            {
                Groups.Move(Groups.IndexOf(ordered[i]), i);
            }
        }

        HasEntries = Groups.Count > 0;
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            await _marketplace.SaveSettingsAsync(Settings);
            IsSettingsOpen = false;
            StatusText = Settings.IsShareConfigured
                ? "Marketplace settings saved."
                : "Saved — sharing stays disabled until the BotGhost webhook URL and API key are set.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving marketplace settings failed");
            StatusText = $"Could not save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ImportEntryAsync(MarketplaceEntry? entry)
    {
        if (entry == null || IsLoading)
        {
            return;
        }

        IsLoading = true;
        StatusText = $"Loading {entry.Name}…";
        try
        {
            var document = await _marketplace.FetchCommandAsync(entry.Sharecode);
            var snapshot = document == null ? null : FlowSnapshotService.ParseDocument(document.Flow);
            if (snapshot == null)
            {
                StatusText = $"Import failed: {entry.Sharecode} could not be read.";
                return;
            }

            ImportRequested?.Invoke(this, (snapshot, $"{entry.Name} ({entry.Sharecode})"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace import failed for {Code}", entry.Sharecode);
            StatusText = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Resolves a pasted share code / offline code / link (or a picked file) and opens it in the editor.</summary>
    [RelayCommand]
    public async Task ImportCodeAsync()
    {
        var dialog = new Views.ImportCodeDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.FilePath != null)
        {
            try
            {
                var fileSnapshot = FlowSnapshotService.ParseDocument(System.IO.File.ReadAllText(dialog.FilePath));
                if (fileSnapshot == null)
                {
                    StatusText = "Import failed: that file is not a Vyral flow document.";
                    return;
                }

                ImportRequested?.Invoke(this, (fileSnapshot, System.IO.Path.GetFileName(dialog.FilePath)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketplace file import failed");
                StatusText = $"Import failed: {ex.Message}";
            }
            return;
        }

        if (dialog.Code == null)
        {
            return;
        }

        IsLoading = true;
        StatusText = "Resolving code…";
        try
        {
            var result = await _marketplace.ResolveImportInputAsync(dialog.Code!);
            if (!result.Success)
            {
                StatusText = result.Error ?? "Import failed.";
                return;
            }

            ImportRequested?.Invoke(this, (result.Snapshot!, result.Label));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task VoteUpAsync(MarketplaceEntry? entry) => await VoteAsync(entry, thumbsUp: true);

    [RelayCommand]
    private async Task VoteDownAsync(MarketplaceEntry? entry) => await VoteAsync(entry, thumbsUp: false);

    private async Task VoteAsync(MarketplaceEntry? entry, bool thumbsUp)
    {
        if (entry == null || !Settings.IsRatingConfigured)
        {
            StatusText = Settings.IsRatingConfigured
                ? string.Empty
                : "Voting needs the BotGhost rating webhook (⚙ settings).";
            return;
        }

        try
        {
            var (up, down) = MarketplaceService.ApplyVote(thumbsUp, entry.Up, entry.Down);
            string? sha = await _marketplace.FetchRatingShaAsync(entry.Sharecode);
            var (ok, error) = await _marketplace.SubmitRatingAsync(entry.Sharecode, up, down, sha);
            if (!ok)
            {
                StatusText = $"Vote failed: {error}";
                return;
            }

            entry.Up = up;
            entry.Down = down;
            StatusText = $"Voted for {entry.Name} — thanks!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace vote failed for {Code}", entry.Sharecode);
            StatusText = $"Vote failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
