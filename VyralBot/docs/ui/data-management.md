# Data Management

## Overview
Settings → Data Management panel: where the app stores data on disk (with sizes
and open-folder buttons), a row-count inventory of every data category, and
cleanup/backup/maintenance actions for the current profile.

## Source Files
- **ViewModel:** `src/VyralBot.UI/ViewModels/DataManagementViewModel.cs`
  - **Class:** `DataManagementViewModel : ViewModelBase` (CommunityToolkit.Mvvm, `[RelayCommand]`)
  - **Constructor:** `ILogger<DataManagementViewModel>`, `IDataService`, `IProfileService`, `ISoundService`, `IProcessManager`
  - **State:** `StatusMessage`, `CurrentProfileName`, `IsBusy`, `StorageLocations`, `DataInventory`
- **View:** embedded in `src/VyralBot.UI/Views/SettingsView.xaml` (Data panel, `SelectedCategory=Data`)
- Reached via `SettingsViewModel.DataManagementViewModel`; initialized per navigation with `InitializeAsync()`

## Actions (Cleanup & Backup card)
- **Clear Chat History / Event History / Workflow Runs** — delete the current
  profile's history rows (confirm dialog first), then refresh the inventory.
- **Compact Database** — SQLite `VACUUM` via `IDataService.CompactDatabaseAsync()`.
- **Export Profile Backup** — `SaveFileDialog` (zip) → `IProfileService.ExportProfileAsync`.
  Warns first that DPAPI-wrapped logins in the backup only work on the exporting PC.
- **Import Profile Backup** — `OpenFileDialog` (zip) → `IProfileService.ImportProfileAsync`
  (wave-9: this was previously service-level only with no UI caller). The service
  reads the ZIP's `profile.json` and refuses with `InvalidOperationException`
  when a profile with that name already exists; the refusal surfaces as a status
  message. Imported profiles appear in the selector on next start / profile switch.

## Conventions
- Folder opens go through `IProcessManager` (zero-orphan rule) — see `StorageLocationItem.Open`.
- Confirmations use `ConfirmDialog` constructed from the VM (`Confirm` helper).
- Status messages appear under the card; `IsBusy` guards re-entrancy.
