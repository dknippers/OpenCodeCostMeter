## Task

Re-write the OpenCode Cost Meter desktop widget's UI layer from WPF + Windows Forms (`net10.0-windows`) to Avalonia UI v11 on .NET 10. All core/data logic (SQLite repository, polling, view models, settings, display-name rules) must stay intact. The app must remain fully functional on Windows, and be structured so that adding a macOS target is a trivial follow-up: no Windows-only APIs may leak into core (non-platform-specific) logic; any platform-specific code lives behind `OperatingSystem.IsWindows()` guards or in platform-specific files.

## Current state: WPF/WinForms touchpoints

Everything under `src/` is one project (`src/OpenCodeCostMeter.csproj`, `UseWPF` + `UseWindowsForms`, `net10.0-windows`). The Windows-only seams are:

| File | Windows-only usage |
|---|---|
| `App.xaml` / `App.xaml.cs` | WPF `Application`, `MessageBox.Show`, kernel32 P/Invokes (`AttachConsole`/`AllocConsole`/`FreeConsole`) for `--help` |
| `MainWindow.xaml` / `.cs` | WPF `Window`, `DragMove()`, `SystemParameters`, `PresentationSource` DPI transforms, `WindowInteropHelper` + WinForms `Screen.FromHandle`, WPF `ContextMenu`/`MenuItem`/`Slider` |
| `Converters/BoolToBrushConverter.cs`, `Converters/BoolToVisibilityConverter.cs` | WPF `IValueConverter`, `Visibility` enum |
| `Services/TrayIconService.cs` | WinForms `NotifyIcon` |
| `Services/UsagePoller.cs` | `System.Windows.Threading.DispatcherTimer` (WPF-only, but this is **core** logic) |
| `ViewModels/MainWindowViewModel.cs` | `System.Windows.Threading.DispatcherTimer` for the highlight timer (WPF-only, but **core** logic) |

Truly portable as-is: `Data/` (DbLocator, DayKey, IUsageRepository, MessageTableRepository), `Models/`, `Services/SettingsStore.cs`, `Services/ModelDisplayNameRules.cs`, `ViewModels/ModelRowViewModel.cs`.

## Target architecture

Two projects in `OpenCodeCostMeter.slnx`:

```
src/
  OpenCodeCostMeter.Core/          net10.0 class library, zero UI/platform deps
    Data/  Models/  ViewModels/  Services/      (moved as-is, except timer seam below)
    Platform/IUiTimer.cs                          (new, tiny abstraction)
  OpenCodeCostMeter/               net10.0 Avalonia app (WinExe), references Core
    Program.cs  App.axaml(/.cs)
    MainWindow.axaml(/.cs)
    Platform/WindowsConsole.cs                    (kernel32 P/Invokes, IsWindows-guarded)
    Services/AvaloniaUiTimer.cs  Services/TrayIconService.cs
    Assets/icon.ico  model-display-names.txt
```

Key decisions:

1. **Plain `net10.0` TFM for both projects** (no `-windows`). One binary runs on Windows today and macOS later; `UsePlatformDetect()` picks the windowing backend. `WinExe` output type is harmless cross-platform (suppresses the console window on Windows only).
2. **`IUiTimer` abstraction in Core** — the only WPF dependency inside core logic is `DispatcherTimer` (used by `UsagePoller` and `MainWindowViewModel`'s highlight timer). Introduce a minimal interface (`Interval`, `Tick`, `Start()`, `Stop()`), constructor-injected. The app project implements it over `Avalonia.Threading.DispatcherTimer`, which ticks on the UI thread — preserving the current guarantee that `Updated`/`Error` events and `ObservableCollection` mutations happen on the UI thread (the `await Task.Run` continuation captures the Avalonia synchronization context, same as WPF today).
3. **Tray icon: Avalonia `TrayIcon`** (cross-platform, built in) replaces WinForms `NotifyIcon`. Menu via `NativeMenu` ("Exit"). Current double-click-to-toggle maps to `TrayIcon.Clicked` (single activation click) — a deliberate, minor behavioral simplification. Icon reuses `Assets/icon.ico` as an `AvaloniaResource` (`WindowIcon`).
4. **Right-click menu: custom `Flyout`, not `ContextMenu`.** The current menu hosts interactive sliders; Avalonia `MenuItem` has no `StaysOpenOnClick`, and fighting menu-close semantics is fragile. A themed `Flyout` hosting plain controls (CheckBox "Always on top", poll-interval Slider + label, opacity Slider + label, buttons: Center horizontally / Center vertically / Hide / Exit, gesture hints A/H/V as dimmed text) reproduces the exact feature set with robust interaction. Styling matches the current dark card look.
5. **No converters project.** Avalonia has no `Visibility` enum — `IsVisible` is a bool, and bindings support negation (`{Binding !HasError}`). Highlight coloring uses style classes bound in XAML (`Classes.highlight="{Binding IsTodayCostHighlighted}"`) instead of `BoolToBrushConverter`. The one `MultiDataTrigger` ("no usage yet" = `IsExpanded && !HasModels`) becomes a computed `ShowNoUsageHint` property on `MainWindowViewModel` (raised when either source changes). Both WPF converters are deleted.
6. **`--help` console attach stays Windows-only**, guarded: `Platform/WindowsConsole.cs` holds the kernel32 P/Invokes behind `OperatingSystem.IsWindows()`; other platforms just `Console.WriteLine` (invoked from a terminal, stdout works natively).
7. **DB-not-found message** replaces WPF `MessageBox.Show` with a minimal Avalonia dialog window (text + OK button) — cross-platform, no new dependency.
8. **Settings schema unchanged** (`OpenCodeCostMeter.settings.json` next to the exe, `x`/`y` kept as DIP doubles — on 100% scaling identical to today's persisted values).

## Avalonia specifics to get right

- **Window**: `SystemDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Background="Transparent"`, `CanResize="False"`, `ShowInTaskbar="False"`, `Topmost` from settings, `SizeToContent="WidthAndHeight"`.
- **Coordinate spaces differ from WPF**: Avalonia `Window.Position` is a `PixelPoint` (physical pixels) while sizes/`ClientSize` are DIPs. The quadrant resize-anchoring math, `SnapToEdgeIfOutOfBounds()`, and Center H/V port verbatim in structure, but all screen/window comparisons must be normalized through `RenderScaling` (replaces the `PresentationSource`/`Screen.FromHandle` dance). Screen lookup: `Screens.ScreenFromWindow(this)` → `WorkingArea`. Get this right and test at 100%/125%/150% DPI.
- **Drag vs click**: pointer-pressed records position; after a 4px threshold (hardcoded — `SystemParameters` is WPF-only) call `BeginMoveDrag(e)`; pointer-released without drag toggles expand. Identical UX to today.
- **Resize event**: Avalonia `Control.SizeChanged` provides `PreviousSize`/`NewSize` like WPF — the cached `ResizeAnchorFlags` logic (compute once, reuse for exactly one subsequent resize, clear on drag/center) ports directly.
- **Hide-on-close**: `Window.Closing` is cancelable (`Cancel = true` + `Hide()`), same as WPF. Only the tray/flyout **Exit** sets `IsExitRequested`.
- **Delayed first show** (no "$0.00" flash), settings-save debounce (500 ms), hotkeys H/V/A/T (`KeyDown`), and in-place `ModelRows` diff all port unchanged.
- **Fonts: embedded, not system fonts.** Replace the Windows-only `"Segoe UI Variable, Segoe UI"` / `"Cascadia Mono, Consolas"` references with bundled fonts via `Avalonia.Fonts.Inter` **11.3.18** (the v11 line — 12.x builds require Avalonia 12) and `Fonts.Avalonia.CascadiaCode` **0.14.0** (depends on `Avalonia >= 11.0.11`, v11-compatible). Registered in `BuildAvaloniaApp()` via `.WithInterFont().WithCascadiaCodeFont()`, referenced in XAML as `fonts:Inter#Inter` and `fonts:CascadiaCode#Cascadia Code`. This renders identically on Windows and macOS (and on Windows machines lacking Segoe UI Variable / Cascadia Mono), eliminating per-platform font fallback work.

## Implementation steps

1. **Core project**: create `src/OpenCodeCostMeter.Core/OpenCodeCostMeter.Core.csproj` (`net10.0`); move `Data/`, `Models/`, `ViewModels/`, `Services/UsagePoller.cs`, `Services/SettingsStore.cs`, `Services/ModelDisplayNameRules.cs`; keep package refs (CommunityToolkit.Mvvm 8.4.2, Microsoft.Data.Sqlite 10.0.9, SQLitePCLRaw.lib.e_sqlite3 2.1.11). Add `Platform/IUiTimer.cs`; change `UsagePoller` and `MainWindowViewModel` to take an injected `IUiTimer`; add `ShowNoUsageHint` computed property.
2. **App skeleton**: new Avalonia csproj (`net10.0`, `WinExe`, Avalonia/Avalonia.Desktop/Avalonia.Themes.Simple 11.3.18 — Simple theme since every control is custom-styled anyway — Avalonia.Fonts.Inter 11.3.18, Fonts.Avalonia.CascadiaCode 0.14.0, Avalonia.Diagnostics for Debug only); `Program.cs` with `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().WithCascadiaCodeFont().LogToTrace()`; `App.axaml` with `ShutdownMode.OnExplicitShutdown` equivalent and `UiFont`/`MonoFont` resources pointing at the embedded font families; add both projects to `OpenCodeCostMeter.slnx`.
3. **Startup port** (`App.axaml.cs`): arg parsing (`--db-path`, `--help`), guarded console helper, settings load, DB resolve + error dialog, repo/poller/VM wiring, delayed first show, save-position-on-exit.
4. **MainWindow.axaml**: port the card UI (colors/brushes as resources, `UiFont` = `fonts:Inter#Inter` and `MonoFont` = `fonts:CascadiaCode#Cascadia Code`, total cost, "no usage yet", breakdown list with thin scrollbar, error state, expanded padding, highlight style classes).
5. **MainWindow code-behind**: drag/click-toggle, quadrant resize anchoring, snap-to-edge, Center H/V, hotkeys, settings debounce, hide-on-close, `IsExpanded` persistence.
6. **Settings flyout** (replaces context menu) with Always-on-top, poll interval, opacity, center actions, Hide, Exit.
7. **TrayIconService** on Avalonia `TrayIcon` (click toggles visibility, Exit menu item).
8. **DB-missing dialog** window.
9. **Cleanup**: delete WPF files (`App.xaml`, `MainWindow.xaml(.cs)`, `Converters/`, old `TrayIconService`, WPF csproj settings, `UseWPF`/`UseWindowsForms`); move `Assets/`, `model-display-names.txt` into the app project (`PreserveNewest` copy for the txt).
10. **Verify**: `dotnet msbuild /t:Compile` both projects; manual Windows checklist (below).
11. **Docs**: update `AGENTS.md` (tech stack, project structure, tray/context-menu design decisions) and `README.md` if it references WPF.

## Explicitly out of scope (macOS follow-up notes, not this task)

- `.app` bundle packaging / `dotnet publish -r osx-*` / signing.
- Settings + `model-display-names.txt` location (inside a read-only `.app` bundle this must move to a per-user config dir — the `AppContext.BaseDirectory` lookups are the only two call sites to touch).
- macOS menu-bar-icon template image, dock-icon policy.

## Acceptance criteria

- [ ] `net10.0` (non-Windows TFM) builds clean for both projects; no `UseWPF`/`UseWindowsForms` anywhere.
- [ ] `rg "System.Windows|System.Windows.Forms|DllImport" src/OpenCodeCostMeter.Core` returns nothing; the only P/Invokes in the app are `OperatingSystem.IsWindows()`-guarded.
- [ ] Widget shows today's cost, per-model breakdown, cost-delta highlighting, and error state exactly as the WPF version.
- [ ] Expand/collapse quadrant anchoring (incl. expand→collapse returning to original X,Y), drag, snap-to-edge, Center H/V work at 100% and 150% DPI.
- [ ] Flyout: always-on-top toggle, poll-interval and opacity sliders (with 500 ms debounced save), center actions, Hide, Exit; hotkeys H/V/A/T work.
- [ ] Tray icon: click toggles widget, Exit terminates; closing the window hides to tray.
- [ ] Settings persist across restarts (position, opacity, interval, always-on-top, expanded); existing settings JSON still loads.
- [ ] `--help` prints help (console attach on Windows); `--db-path` works; missing DB shows the Avalonia error dialog and exits with code 1.
- [ ] `AGENTS.md` updated to match the new structure.
