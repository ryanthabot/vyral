using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Integrations.Obs;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// OBS page host: the embedded studio tab plus the original Collections tab
/// (imports OBS scene-collection JSON verbatim, browses the imported
/// documents as a tree and exports byte-identical copies).
/// </summary>
public partial class ObsViewModel : ViewModelBase, IDisposable
{
    private readonly ObsSceneCollectionStore _store;
    private readonly IDataService _dataService;
    private readonly ILogger<ObsViewModel> _logger;

    /// <summary>The embedded studio (own view model, resolved via DI).</summary>
    public ObsStudioViewModel Studio { get; }

    // Studio is the default tab.
    [ObservableProperty]
    private bool _isStudioTabSelected = true;

    [ObservableProperty]
    private bool _isCollectionsTabSelected;

    [ObservableProperty]
    private ObservableCollection<ObsCollectionRow> _collections = new();

    [ObservableProperty]
    private ObsCollectionRow? _selectedCollection;

    [ObservableProperty]
    private ObservableCollection<ObsTreeNode> _documentTree = new();

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ObsSceneCollectionDocument? _currentDocument;

    [ObservableProperty]
    private string? _sceneSummary;

    public ObsViewModel(ObsSceneCollectionStore store, ObsStudioViewModel studio, IDataService dataService, ILogger<ObsViewModel> logger)
    {
        _store = store;
        Studio = studio;
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>Load-on-navigate hook — MainViewModel awaits this on every OBS navigation.</summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing OBS view");
        await LoadAsync();
        await Studio.InitializeAsync();
    }

    /// <summary>
    /// MainViewModel drops the OBS page on navigation away — chain to the
    /// embedded studio so its 1 Hz stats timer stops polling the native gate.
    /// </summary>
    public void Dispose() => Studio.Dispose();

    /// <summary>Mutually exclusive tab switcher for the Studio | Collections pair.</summary>
    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsStudioTabSelected = tab == "Studio";
        IsCollectionsTabSelected = tab == "Collections";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var entities = await _dataService.GetObsSceneCollectionsAsync();
            Collections.Clear();
            foreach (var entity in entities.OrderByDescending(c => c.ImportedAtUtc))
            {
                Collections.Add(new ObsCollectionRow
                {
                    Id = entity.Id,
                    Name = entity.Name,
                    SceneCount = entity.SceneCount,
                    SourceCount = entity.SourceCount,
                    // Multi-arg StringFormat is not a valid single WPF Binding — precompute.
                    SummaryText = $"{entity.SceneCount} scenes • {entity.SourceCount} sources",
                    ImportedAtText = $"Imported {entity.ImportedAtUtc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)}",
                });
            }

            // Keep selection/tree only while the selected collection still exists.
            if (SelectedCollection != null && Collections.All(c => c.Id != SelectedCollection.Id))
            {
                SelectedCollection = null;
                DocumentTree.Clear();
                CurrentDocument = null;
                SceneSummary = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load OBS scene collections");
            StatusMessage = $"Failed to load collections: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import OBS scene collection",
            Filter = "OBS scene collection (*.json)|*.json|JSON (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var result = await _store.ImportFromFileAsync(dialog.FileName);
            StatusMessage = result.Message;
            if (result.Success)
            {
                await LoadAsync();
                SelectedCollection = Collections.FirstOrDefault(c => c.Id == result.Collection!.Id);
                await LoadCollectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import OBS scene collection from {FilePath}", dialog.FileName);
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadCollectionAsync()
    {
        if (SelectedCollection is null)
        {
            return;
        }

        try
        {
            var (doc, error) = await _store.LoadDocumentAsync(SelectedCollection.Id);
            if (error is not null || doc is null)
            {
                StatusMessage = error ?? "Failed to load the collection document.";
                DocumentTree.Clear();
                CurrentDocument = null;
                SceneSummary = null;
                return;
            }

            CurrentDocument = doc;
            SceneSummary = $"Scene: {doc.CurrentScene ?? "?"} • {doc.Resolution ?? "?"} • {doc.CurrentTransition ?? "?"} • {doc.SourceCount} sources";

            DocumentTree.Clear();
            foreach (var node in BuildDocumentTree(doc))
            {
                DocumentTree.Add(node);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to browse OBS scene collection {CollectionId}", SelectedCollection.Id);
            StatusMessage = $"Failed to browse the collection: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedCollection is null)
        {
            StatusMessage = "Select a collection to export.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SelectedCollection.Name + ".json",
            Filter = "JSON (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var (success, message) = await _store.ExportToFileAsync(SelectedCollection.Id, dialog.FileName);
            StatusMessage = success ? $"{message} (byte-identical)" : message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export OBS scene collection {CollectionId}", SelectedCollection.Id);
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedCollection is null)
        {
            StatusMessage = "Select a collection to delete.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete scene collection '{SelectedCollection.Name}' from VyralBot? The original OBS file is not touched.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var name = SelectedCollection.Name;
            await _dataService.DeleteObsSceneCollectionAsync(SelectedCollection.Id);
            StatusMessage = $"Deleted '{name}'.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete OBS scene collection {CollectionId}", SelectedCollection.Id);
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }

    private static List<ObsTreeNode> BuildDocumentTree(ObsSceneCollectionDocument doc)
    {
        var scenes = doc.Scenes.Select(scene => new ObsTreeNode(
            scene.Name,
            $" — {scene.Items.Count} items",
            scene.Items.Select(item =>
            {
                var detail = item.Visible ? " — visible" : " — hidden";
                if (Math.Abs(item.Rotation) >= 0.05)
                {
                    detail += $" • {item.Rotation:0.#}°";
                }
                detail += item.ScaleX == item.ScaleY
                    ? $" • x{item.ScaleX:0.##}"
                    : $" • x{item.ScaleX:0.##}/y{item.ScaleY:0.##}";
                detail += $" • pos ({item.PosX:0.#}, {item.PosY:0.#})";
                if (item.Locked)
                {
                    detail += " • locked";
                }
                return new ObsTreeNode(item.SourceName, detail, []);
            }).ToList())).ToList();

        var sources = doc.NonSceneSources.Select(source =>
        {
            var detail = $" — {source.Id}";
            if (source.FilterCount > 0)
            {
                detail += $", {source.FilterCount} filters";
            }
            if (source.Muted)
            {
                detail += ", muted";
            }
            detail += $", vol {source.Volume:0.##}";
            return new ObsTreeNode(source.Name, detail, []);
        }).ToList();

        var audio = doc.AudioDevices.Select(device =>
            new ObsTreeNode(device.Name, $" — {device.SourceId ?? "?"}", [])).ToList();

        return
        [
            new ObsTreeNode("Scenes", $" — {scenes.Count}", scenes),
            new ObsTreeNode("Sources", $" — {sources.Count}", sources),
            new ObsTreeNode("Audio", $" — {audio.Count}", audio),
        ];
    }
}

/// <summary>One imported OBS scene collection shown in the left list.</summary>
public partial class ObsCollectionRow : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int SceneCount { get; init; }
    public int SourceCount { get; init; }
    // Precomputed "N scenes • M sources" — multi-arg StringFormat is not a valid single Binding.
    public string SummaryText { get; init; } = "";
    public string ImportedAtText { get; init; } = "";
}

public sealed record ObsTreeNode(string Name, string Detail, IReadOnlyList<ObsTreeNode> Children);
