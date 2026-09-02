using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VyralBot.UI.Services.Docs;

namespace VyralBot.UI.ViewModels;

/// <summary>Navigation section (a group of doc pages) shown in the documentation sidebar.</summary>
public partial class DocNavSection : ObservableObject
{
    public string Title { get; init; } = string.Empty;

    public ObservableCollection<DocNavPage> Pages { get; } = new();

    [ObservableProperty]
    private bool _isVisible = true;
}

/// <summary>One page entry in the documentation sidebar.</summary>
public partial class DocNavPage : ObservableObject
{
    public string Title { get; init; } = string.Empty;

    /// <summary>Virtual path relative to AppDocs, e.g. "stream-tools/giveaways.md".</summary>
    public string Path { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}

/// <summary>
/// ViewModel for the in-app documentation browser (Home → Documentation).
/// Renders the AppDocs markdown set GitBook-style: nav tree, search, back/forward
/// history, cross-links between pages, and read-only code views for source links.
/// </summary>
public partial class DocumentationViewModel : ViewModelBase
{
    private enum EntryKind { Page, Code }

    private sealed record HistoryEntry(EntryKind Kind, string VirtualPath, string Title);

    private readonly List<HistoryEntry> _history = new();
    private int _historyIndex = -1;
    private DocumentationService? _docs;
    private bool _initialized;
    private string _currentBaseVirtualPath = "index.md";
    private string? _pendingScrollFragment;

    public ObservableCollection<DocNavSection> Sections { get; } = new();

    [ObservableProperty]
    private string _currentPageTitle = "Documentation";

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>Complete HTML for the WebView2. Always notifies so back/forward re-navigates.</summary>
    public string CurrentHtml { get; private set; } = string.Empty;

    public DocumentationViewModel()
    {
        // Docs load lazily from the view so a missing AppDocs folder only degrades this page.
        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        await Task.Run(() =>
        {
            _docs = DocumentationService.Create();
        });

        if (_docs is null)
        {
            HasError = true;
            StatusMessage = "AppDocs folder not found. The documentation folder should sit next to the src folder (or next to the executable).";
            return;
        }

        try
        {
            Sections.Clear();
            foreach (var section in _docs.LoadManifest())
            {
                var navSection = new DocNavSection { Title = section.Title };
                foreach (var page in section.Pages)
                    navSection.Pages.Add(new DocNavPage { Title = page.Title, Path = page.Path });
                Sections.Add(navSection);
            }

            NavigateToPage("index.md");
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to load documentation: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenPage(DocNavPage? page)
    {
        if (page is not null)
            NavigateToPage(page.Path);
    }

    public void NavigateToPage(string virtualPath)
    {
        if (_docs is null)
            return;

        try
        {
            var html = _docs.RenderPage(virtualPath);
            var title = _docs.DocExists(virtualPath)
                ? Path.GetFileNameWithoutExtension(virtualPath)
                : virtualPath;

            PushHistory(new HistoryEntry(EntryKind.Page, virtualPath, title));
            ShowPage(virtualPath, html);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Could not open '{virtualPath}': {ex.Message}";
        }
    }

    /// <summary>Navigates to a doc page and scrolls to a heading fragment (e.g. "usage").</summary>
    public void NavigateToPage(string virtualPath, string? fragment)
    {
        _pendingScrollFragment = fragment;
        NavigateToPage(virtualPath);
    }

    /// <summary>Returns and clears the fragment the next rendered page should scroll to.</summary>
    public string? ConsumeScrollFragment()
    {
        var fragment = _pendingScrollFragment;
        _pendingScrollFragment = null;
        return fragment;
    }

    private void OpenCode(string repoRelativePath)
    {
        if (_docs is null)
            return;

        try
        {
            var html = _docs.RenderCode(repoRelativePath);
            PushHistory(new HistoryEntry(EntryKind.Code, repoRelativePath, Path.GetFileName(repoRelativePath)));
            CurrentPageTitle = Path.GetFileName(repoRelativePath);
            CurrentPath = repoRelativePath;
            UpdateNavSelection(null);
            SetHtml(html);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Could not open '{repoRelativePath}': {ex.Message}";
        }
    }

    /// <summary>
    /// Routes a clicked hyperlink from the rendered page. Doc links open as pages,
    /// source-file links open in the code viewer, external links open in the system browser.
    /// </summary>
    /// <param name="href">Raw href from the anchor (may be relative).</param>
    public void HandleLink(string href)
    {
        if (_docs is null || string.IsNullOrEmpty(href))
            return;

        var target = _docs.ResolveHref(_currentBaseVirtualPath, href);
        switch (target.Kind)
        {
            case DocumentationService.LinkKind.Doc:
                NavigateToPage(target.Value, target.Fragment);
                break;
            case DocumentationService.LinkKind.Code:
                OpenCode(target.Value);
                break;
            case DocumentationService.LinkKind.External:
                try
                {
                    Process.Start(new ProcessStartInfo(target.Value) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    HasError = true;
                    StatusMessage = $"Could not open link: {ex.Message}";
                }
                break;
            case DocumentationService.LinkKind.Invalid:
                HasError = true;
                StatusMessage = $"Link target not found: {href}";
                break;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            ShowEntry(_history[_historyIndex]);
        }
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            ShowEntry(_history[_historyIndex]);
        }
    }

    /// <summary>Reloads the manifest and re-renders the current page (useful while editing docs).</summary>
    [RelayCommand]
    private void RefreshDocs()
    {
        if (_docs is null || _historyIndex < 0)
            return;

        var current = _history[_historyIndex];
        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        try
        {
            Sections.Clear();
            foreach (var section in _docs.LoadManifest())
            {
                var navSection = new DocNavSection { Title = section.Title };
                foreach (var page in section.Pages)
                    navSection.Pages.Add(new DocNavPage { Title = page.Title, Path = page.Path });
                Sections.Add(navSection);
            }
            ApplyNavFilter();
            ShowEntry(current);
            StatusMessage = "Documentation reloaded.";
            HasError = false;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to reload documentation: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenDocsFolder()
    {
        if (_docs is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(_docs.DocsRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Could not open folder: {ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyNavFilter();

    private void ApplyNavFilter()
    {
        var filter = SearchText?.Trim() ?? string.Empty;

        foreach (var section in Sections)
        {
            var anyVisible = false;
            foreach (var page in section.Pages)
            {
                page.IsVisible = filter.Length == 0 ||
                    page.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    page.Path.Contains(filter, StringComparison.OrdinalIgnoreCase);
                anyVisible |= page.IsVisible;
            }
            section.IsVisible = anyVisible;
        }
    }

    private void PushHistory(HistoryEntry entry)
    {
        _historyIndex++;
        if (_historyIndex < _history.Count)
            _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
        _history.Add(entry);

        CanGoBack = _historyIndex > 0;
        CanGoForward = false;
        HasError = false;
        StatusMessage = string.Empty;
    }

    private void ShowEntry(HistoryEntry entry)
    {
        if (_docs is null)
            return;

        try
        {
            if (entry.Kind == EntryKind.Page)
                ShowPage(entry.VirtualPath, _docs.RenderPage(entry.VirtualPath));
            else
            {
                CurrentPageTitle = entry.Title;
                CurrentPath = entry.VirtualPath;
                UpdateNavSelection(null);
                SetHtml(_docs.RenderCode(entry.VirtualPath));
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Could not open '{entry.VirtualPath}': {ex.Message}";
        }

        CanGoBack = _historyIndex > 0;
        CanGoForward = _historyIndex < _history.Count - 1;
    }

    private void ShowPage(string virtualPath, string html)
    {
        CurrentPageTitle = TitleFor(virtualPath);
        CurrentPath = "AppDocs/" + virtualPath;
        _currentBaseVirtualPath = virtualPath;
        UpdateNavSelection(virtualPath);
        SetHtml(html);
    }

    private string TitleFor(string virtualPath)
    {
        foreach (var section in Sections)
            foreach (var page in section.Pages)
                if (string.Equals(page.Path, virtualPath, StringComparison.OrdinalIgnoreCase))
                    return page.Title;
        return Path.GetFileNameWithoutExtension(virtualPath);
    }

    private void UpdateNavSelection(string? virtualPath)
    {
        foreach (var section in Sections)
            foreach (var page in section.Pages)
                page.IsSelected = virtualPath is not null &&
                    string.Equals(page.Path, virtualPath, StringComparison.OrdinalIgnoreCase);
    }

    private void SetHtml(string html)
    {
        CurrentHtml = html;
        OnPropertyChanged(nameof(CurrentHtml));
    }
}
