using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.UI.Views;

namespace VyralBot.UI.ViewModels;

/// <summary>A storage location row in Settings → Data Management.</summary>
public partial class StorageLocationItem : ObservableObject
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }

    [ObservableProperty]
    private string _sizeText = "—";

    [ObservableProperty]
    private bool _exists;

    [RelayCommand]
    private void Open()
    {
        try
        {
            if (!Exists) return;

            if (IsDirectory)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", Path) { UseShellExecute = true });
            }
            else
            {
                // Select the file inside its folder
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening {Path}: {ex.Message}");
        }
    }
}

/// <summary>A data-category count row in Settings → Data Management.</summary>
public partial class DataInventoryItem : ObservableObject
{
    public string Name { get; init; } = "";
    [ObservableProperty]
    private int _count;
}

/// <summary>
/// Settings → Data Management: where the app stores data on disk (with sizes and
/// open-folder buttons), a row-count inventory of every data category, and
/// cleanup/backup/maintenance actions.
/// </summary>
public partial class DataManagementViewModel : ViewModelBase
{
    private readonly ILogger<DataManagementViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IProfileService _profileService;
    private readonly Core.Services.Audio.ISoundService _soundService;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _currentProfileName = "";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<StorageLocationItem> StorageLocations { get; } = new();

    public ObservableCollection<DataInventoryItem> DataInventory { get; } = new();

    public DataManagementViewModel(
        ILogger<DataManagementViewModel> logger,
        IDataService dataService,
        IProfileService profileService,
        Core.Services.Audio.ISoundService soundService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _soundService = soundService ?? throw new ArgumentNullException(nameof(soundService));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            CurrentProfileName = profile.DisplayName;

            LoadStorageLocations(profile.Name);
            await LoadDataInventoryAsync();

            _logger.LogInformation("Data management view initialized for profile {Profile}", profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing data management view");
            StatusMessage = $"Error loading data information: {ex.Message}";
        }
    }

    private void LoadStorageLocations(string profileName)
    {
        StorageLocations.Clear();

        var profileDataPath = _profileService.GetProfileDataPath(profileName);
        var profileRoot = Directory.GetParent(profileDataPath)?.FullName ?? profileDataPath;
        var credentialsPath = _profileService.GetProfileCredentialsPath(profileName);

        StorageLocations.Add(new StorageLocationItem
        {
            Name = "Profile data (database)",
            Description = "All profile data: platforms, commands, workflows, currency, events, chat history",
            Path = Path.Combine(profileDataPath, "vyralbot.db"),
            IsDirectory = false
        });

        StorageLocations.Add(new StorageLocationItem
        {
            Name = "Profile folder",
            Description = "This profile's database, credentials and settings",
            Path = profileRoot,
            IsDirectory = true
        });

        StorageLocations.Add(new StorageLocationItem
        {
            Name = "Twitch app credentials",
            Description = "Twitch OAuth client id/secret used for token refresh",
            Path = Path.Combine(credentialsPath, "twitch_credentials.json"),
            IsDirectory = false
        });

        StorageLocations.Add(new StorageLocationItem
        {
            Name = "ngrok credentials",
            Description = "ngrok authtoken for the public webhook URL",
            Path = Path.Combine(credentialsPath, "ngrok_credentials.json"),
            IsDirectory = false
        });

        var appdataRoot = FindAppdataRoot();
        if (appdataRoot != null)
        {
            StorageLocations.Add(new StorageLocationItem
            {
                Name = "App settings",
                Description = "Global UI preferences (sidebar, theme cache) shared by all profiles",
                Path = Path.Combine(appdataRoot, "ui-settings.json"),
                IsDirectory = false
            });
        }

        StorageLocations.Add(new StorageLocationItem
        {
            Name = "Sounds library",
            Description = "Custom sound files served to OBS/browser sources (shared by all profiles)",
            Path = _soundService.GetSoundsDirectory(),
            IsDirectory = true
        });

        StorageLocations.Add(new StorageLocationItem
        {
            Name = "Logs",
            Description = "Daily rolling log files (kept for 7 days)",
            Path = Path.Combine(AppContext.BaseDirectory, "logs"),
            IsDirectory = true
        });

        var webRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VyralBot", "WebView2Data");
        StorageLocations.Add(new StorageLocationItem
        {
            Name = "WebView2 browser cache",
            Description = "Embedded browser cache for OAuth sign-in windows (shared by all profiles)",
            Path = webRoot,
            IsDirectory = true
        });

        var flowRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vyral");
        StorageLocations.Add(new StorageLocationItem
        {
            Name = "Flow editor drafts & templates",
            Description = "Automation flow autosave drafts and block templates",
            Path = flowRoot,
            IsDirectory = true
        });

        foreach (var item in StorageLocations)
        {
            item.Exists = item.IsDirectory
                ? Directory.Exists(item.Path)
                : File.Exists(item.Path);
            item.SizeText = item.Exists ? FormatSize(GetSizeOnDisk(item.Path, item.IsDirectory)) : "—";
        }
    }

    private async Task LoadDataInventoryAsync()
    {
        try
        {
            var inventory = await _dataService.GetDataInventoryAsync();

            DataInventory.Clear();
            foreach (var (label, count) in inventory)
            {
                DataInventory.Add(new DataInventoryItem { Name = label, Count = count });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading data inventory");
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var profile = await _profileService.GetCurrentProfileAsync();
        LoadStorageLocations(profile.Name);
        await LoadDataInventoryAsync();
    }

    [RelayCommand]
    private async Task ClearChatHistoryAsync()
    {
        if (!Confirm("Delete all stored chat messages for this profile? Chat history is used by the Chat page's history view. This cannot be undone."))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _dataService.DeleteAllChatMessagesAsync();
            StatusMessage = "Chat message history cleared";
            await LoadDataInventoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing chat history");
            StatusMessage = $"Error clearing chat history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearEventHistoryAsync()
    {
        if (!Confirm("Delete all event history for this profile? Statistics charts and the Event History page will lose past events. This cannot be undone."))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _dataService.DeleteEventLogsAsync();
            StatusMessage = "Event history cleared";
            await LoadDataInventoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing event history");
            StatusMessage = $"Error clearing event history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearWorkflowExecutionsAsync()
    {
        if (!Confirm("Delete the workflow execution history? Workflow definitions are kept — only past run records are removed. This cannot be undone."))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _dataService.DeleteWorkflowExecutionsAsync();
            StatusMessage = "Workflow execution history cleared";
            await LoadDataInventoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing workflow execution history");
            StatusMessage = $"Error clearing workflow history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export profile backup",
                Filter = "Profile backup (*.zip)|*.zip",
                FileName = $"{profile.Name}_backup.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            var resultPath = await _profileService.ExportProfileAsync(profile.Name, dialog.FileName);
            StatusMessage = $"Profile exported to {resultPath}";
            _logger.LogInformation("Profile {Profile} exported from Data Management to {Path}", profile.Name, resultPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting profile backup");
            StatusMessage = $"Error exporting backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompactDatabaseAsync()
    {
        try
        {
            IsBusy = true;
            var profile = await _profileService.GetCurrentProfileAsync();
            var dataPath = _profileService.GetProfileDataPath(profile.Name);
            var dbPath = Path.Combine(dataPath, "vyralbot.db");
            var before = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;

            var success = await _dataService.CompactDatabaseAsync();

            var after = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;
            StatusMessage = success
                ? $"Database compacted ({FormatSize(before)} → {FormatSize(after)})"
                : "Database compaction failed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compacting database");
            StatusMessage = $"Error compacting database: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool Confirm(string message)
    {
        var dialog = new ConfirmDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dialog.SetMessage(message);
        return dialog.ShowDialog() == true;
    }

    private static string? FindAppdataRoot()
    {
        // Same discovery as UiPreferencesService: walk up from the exe until an
        // "appdata" folder appears (portable layout).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (string.Equals(dir.Name, "appdata", StringComparison.OrdinalIgnoreCase))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static long GetSizeOnDisk(string path, bool isDirectory)
    {
        try
        {
            if (!isDirectory)
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }

            if (!Directory.Exists(path))
            {
                return 0;
            }

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
