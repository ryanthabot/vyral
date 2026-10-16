---
name: vyralbot-feature
description: Step-by-step recipe for adding a new page/section/service to the VyralBot desktop app following its exact conventions — nav (sidebar or collapsible group), DI registration, ViewModel/View scaffolding, config persistence, and the docs-sync obligations. Use when asked to add a page, a settings section, a nav entry, or a new singleton service to the desktop app.
---

# Add a feature page to VyralBot desktop (recipe)

Template to copy: **Restream** (`RestreamView.xaml` / `RestreamViewModel.cs`)
for cards-style pages; the **Platforms** nav group in `MainWindow.xaml` for
collapsible sub-sections.

## 1. ViewModel — `VyralBot.UI/ViewModels/<Name>ViewModel.cs`

- `public partial class <Name>ViewModel : ViewModelBase` (+ `IDisposable` when
  it subscribes to events).
- `[ObservableProperty]` fields; `[RelayCommand]` methods (async ones named
  `XxxAsync` → command property `XxxCommand`).
- `Task InitializeAsync()` — called on every navigation; load persisted state
  here via `IDataService.GetConfigValueAsync(key)` (JSON, category = feature
  name).
- Unsubscribe in `Dispose()` from **named** handlers only (lambda handlers
  cannot be unsubscribed).
- Long-running engines/servers are separate singletons that outlive the page —
  never put a running process or socket in the VM itself.

## 2. View — `VyralBot.UI/Views/<Name>View.xaml` (+ trivial .xaml.cs)

- Copy `RestreamView.xaml` structure: `DockPanel` with header (BACK TO HOME +
  icon + title + subtitle), `ScrollViewer` of `materialDesign:Card`s.
- Only real resources: `BooleanToVisibilityConverter` as `BoolToVis`; the
  app-level `InvertBooleanToVisibilityConverter`; status dots = `Ellipse` +
  `DataTrigger`s.
- Commands inside row `DataTemplate`s reach the page VM via
  `RelativeSource AncestorType=UserControl`; textboxes use
  `UpdateSourceTrigger=PropertyChanged`.
- Brush names: `BackgroundPrimaryBrush`, `CardBackgroundBrush`,
  `PrimaryBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `BorderLightBrush`.

## 3. DI — `App.xaml.cs` `ConfigureServices`

`services.AddSingleton<<Name>ViewModel>();` near related registrations
(+ its service singletons before it).

## 4. Navigation — `MainViewModel.cs` + `MainWindow.xaml`

- Fields: `private IServiceScope? _<name>Scope;` + `private <Name>ViewModel? _<name>ViewModel;`
- `[RelayCommand] private async Task NavigateTo<Name>Async()` — guard
  `CheckSetupInProgress(out _)`, `CurrentViewModel = null`,
  `await DisposeNonEssentialViewModelsAsync(excludeViewModel: _<name>ViewModel)`,
  recreate scope, resolve VM, set `CurrentViewModel` + `Title`,
  `await InitializeAsync()`, wrap in try/catch + log.
- Disposal block in `DisposeNonEssentialViewModelsAsync` after the Restream one.
- XAML: flat sidebar button, or wrap Restream/other parent in the collapsible
  group pattern (header + chevron + `StackPanel` children). Add
  `<DataTemplate DataType="{x:Type vm:<Name>ViewModel}"><local:<Name>View/></DataTemplate>`
  next to the Restream template.

## 5. Docs-sync (project rule — `docs/copilot-instructions.md`)

- Create `VyralBot/docs/ui/<name>-viewmodel.md` / service doc; update
  `docs/README.md` and any cross-referenced docs.

## 6. Verify

`dotnet build VyralBot.sln -c Debug --nologo -v q` (0 errors) + `dotnet test`
green (see the `vyralbot-build` skill).
