# AGENTS.md

## Project Overview

OpenCode Cost Meter is a Windows 11 desktop widget that displays today's OpenCode LLM spend in real-time, broken down by model. It reads the opencode SQLite database directly (read-only) and refreshes every few seconds.

The UI is built with Avalonia UI v11 (ported from WPF) and the codebase is platform-clean: all non-UI logic lives in a platform-agnostic core project, so adding a macOS target is a trivial follow-up.

## Tech Stack

- **.NET 10** (plain `net10.0` for both projects — **no** `-windows` TFM)
- **Avalonia UI** 11.3.18 (`Avalonia.Desktop`, `Avalonia.Themes.Simple`) - cross-platform UI
- **Avalonia.Fonts.Inter** 11.3.18 + **Fonts.Avalonia.CascadiaCode** 0.14.0 - embedded fonts (no system-font dependencies)
- **CommunityToolkit.Mvvm** 8.4.2 - MVVM framework
- **Microsoft.Data.Sqlite** 10.0.9 - SQLite access
- **SQLitePCLRaw.lib.e_sqlite3** 2.1.11 - native SQLite
- **Avalonia `TrayIcon`** - system tray icon (cross-platform)

## Solution Layout

Two projects in `OpenCodeCostMeter.slnx`:

- **`src/OpenCodeCostMeter.Core/`** - `net10.0` class library, zero UI/platform dependencies. Contains all data access, models, view models, and non-UI services. No Windows-only APIs allowed here.
- **`src/OpenCodeCostMeter/`** - `net10.0` Avalonia desktop app (`WinExe`), references Core. Contains windows, tray icon, and platform glue. Any platform-specific code lives behind `OperatingSystem.IsWindows()` guards (currently only the `--help` console attach in `Platform/ConsoleHelper.cs`).

## Database

The widget reads from opencode's SQLite database at `%USERPROFILE%\.local\share\opencode\opencode.db`.

Key details about the schema:

- Uses the `message` table (not `session`) because `session` maintains cumulative tokens across the entire session lifetime, which would incorrectly attribute past tokens to today's date.
- Each assistant message has a `data` JSON column containing `$.time.completed` (Unix ms timestamp), `$.cost`, `$.providerID`, and `$.modelID`.
- The widget filters messages by `$.time.completed` to only count today's calls.

## Architecture

### Core: Data Layer (`src/OpenCodeCostMeter.Core/Data/`)

- **DbLocator** - Resolves the database path (default or from the `--db-path` command-line argument)
- **DayKey** - Static helper in `DbLocator.cs`; converts a Unix-ms timestamp to a `yyyy-MM-dd` string
- **MessageTableRepository** - Primary repo that queries the `message` table for today's per-model cost breakdowns. Single SQL query with inner `GROUP BY (time.created, time.completed)` to deduplicate forked messages before aggregating per provider/model. Selects only `providerID`, `modelID`, and `cost`.
- **IUsageRepository** - Interface for repositories

### Core: Services (`src/OpenCodeCostMeter.Core/Services/`)

- **UsagePoller** - `IUiTimer`-based poller that calls the repo and fires Updated/Error events. Implements `IDisposable`. Has an `_inFlight` guard to prevent overlapping queries if a poll takes longer than the interval.
- **SettingsStore** - Persists/loads `Settings` to JSON file next to the exe
- **ModelDisplayNameRules** - Formats raw model IDs (e.g. `claude-sonnet-4-20250514`) into human-readable display names. Applies title-case by default, then runs prefix-based replacement rules loaded from `model-display-names.txt` (next to the exe). Results are cached in a `ConcurrentDictionary`.

### Core: Platform seam (`src/OpenCodeCostMeter.Core/Platform/`)

- **IUiTimer** - Minimal UI-thread timer abstraction (`Interval`, `Tick`, `Start`, `Stop`) so core logic has no UI-framework dependency. Implemented in the app by `AvaloniaUiTimer` over `Avalonia.Threading.DispatcherTimer`, preserving the guarantee that poll results and `ObservableCollection` updates arrive on the UI thread.

### Core: ViewModels (`src/OpenCodeCostMeter.Core/ViewModels/`)

- **MainWindowViewModel** - Main VM; binds to the UI, tracks cost deltas for highlighting, manages ModelRows collection, exposes `IsExpanded` for breakdown visibility and `ShowNoUsageHint` (= `IsExpanded && !HasModels`). The highlight-reset timer is an injected `IUiTimer`.
- **ModelRowViewModel** - One per breakdown row in the details section

### Core: Models (`src/OpenCodeCostMeter.Core/Models/`)

- **DayUsageSnapshot** - Today's aggregated data (`DayKey`, cost, per-model breakdown, taken-at timestamp)
- **ModelBreakdown** - Per-model cost
- **Settings** - Persisted JSON settings (window position in DIPs, opacity, poll interval, always-on-top, is-expanded)

### Core: other

- **LaunchOptions** - Command-line parsing for `--db-path` and `--help`.

### App (`src/OpenCodeCostMeter/`)

- **Program.cs** - Entry point; parses args, prints `--help` and exits early, otherwise starts Avalonia with `UsePlatformDetect().WithInterFont().WithCascadiaCodeFont()`.
- **App.axaml/.cs** - Application bootstrap: `SimpleTheme` (dark), `ShutdownMode.OnExplicitShutdown`, wires settings/repo/poller/VM/window/tray, delayed first show, save-on-exit.
- **MainWindow.axaml/.cs** - Borderless transparent always-on-top widget window (`SystemDecorations="None"`, `TransparencyLevelHint="Transparent"`). All styling via style classes bound to VM booleans (e.g. `Classes.highlight`, `Classes.expanded`); no value converters (Avalonia has no `Visibility` enum - `IsVisible` binds directly, with `!` negation).
- **Services/TrayIconService** - Wraps an Avalonia `TrayIcon`. Clicking the tray icon toggles widget visibility (note: `Clicked` is Win32/Linux only, not raised on macOS); the native menu has **Exit**. Closing the widget window hides it to the tray; only **Exit** terminates the application.
- **Services/AvaloniaUiTimer** - `IUiTimer` implementation over `DispatcherTimer`.
- **Platform/ConsoleHelper** - `--help` console output. kernel32 `AttachConsole`/`AllocConsole`/`FreeConsole` P/Invokes guarded by `OperatingSystem.IsWindows()`.
- **DatabaseNotFoundWindow** - Small code-built error dialog replacing the old WPF `MessageBox`.

## Key Design Decisions

1. **Read-only SQLite** - Connection uses `SqliteOpenMode.ReadOnly` and `DefaultTimeout = 2` to survive database locks
2. **Today only** - Tokens are attributed to the day the message _completed_, not when the session started
3. **Non-blocking UI** - Slow queries don't freeze the UI; last known values stay on screen during refresh
4. **Cost delta highlighting** - New spend since last poll is highlighted briefly
5. **System tray** - The widget lives in the system tray; closing the widget hides it to the tray. **Hide** and **Exit** are available in the widget's right-click flyout; the tray menu has **Exit**.
6. **Settings debounce** - Slider drags (poll interval, opacity) update visuals immediately but debounce the disk write by 500ms via a `DispatcherTimer` in `MainWindow`, so `SettingsStore.Save()` fires once after the user stops dragging.
7. **Delayed window show** - The window stays hidden until the first poll result arrives, avoiding a flash of "$0.00".
8. **Quadrant-based resize anchoring** - When the widget resizes (e.g. expanding the breakdown list), the window anchors from the corner closest to the screen center so it expands "inward" rather than flying off-screen. The computed quadrant/span flags are cached and reused for exactly one subsequent resize, so expanding then collapsing returns the widget to its original (X,Y) even when the expanded size crosses a screen axis. The cached anchor is cleared when the window is dragged or centered. Coordinate gotcha: Avalonia `Window.Position` is **physical pixels** while sizes/`Bounds` are **DIPs** - all math is normalized through `RenderScaling` (see `SetPositionDips`/`GetPositionDips`/`GetWorkingAreaDips` in `MainWindow`). Pixel positions are integers, which keeps expand→collapse round-trips exact. `SnapToEdgeIfOutOfBounds()` uses the screen `WorkingArea` (excludes taskbar). The first `SizeChanged` after initial show is skipped (`_skipInitialSizeChange`) because its `PreviousSize` is unreliable and would move the window away from its restored position.
9. **In-place ModelRows diff** - `MainWindowViewModel` performs a minimal Move/Insert diff on the `ObservableCollection<ModelRowViewModel>` so containers are reused instead of rebuilding the entire list each poll.
10. **Right-click settings flyout** - Right-clicking the widget opens a custom `Flyout` (not a `ContextMenu`) hosting plain controls - Always-on-top CheckBox, Poll interval and Opacity sliders, Center horizontally/vertically, Hide, Exit - because Avalonia `MenuItem` has no `StaysOpenOnClick` and interactive sliders inside menus are fragile. Control state is re-synced on every `Opening` (guard flag `_syncingFlyout` prevents feedback into settings).
11. **Embedded fonts** - UI font is Inter, monospace is Cascadia Code, both bundled via `Avalonia.Fonts.Inter` / `Fonts.Avalonia.CascadiaCode` and referenced as `fonts:Inter#Inter` / `fonts:CascadiaCode#Cascadia Code`. No system-font dependencies, identical rendering across platforms.
12. **Cross-platform structure** - Both projects target plain `net10.0`; the only P/Invokes in the app are `OperatingSystem.IsWindows()`-guarded. macOS follow-ups (not done): `.app` packaging, moving the settings/display-names files out of `AppContext.BaseDirectory` (read-only inside a bundle), menu-bar-icon/dock policy.

## Building

```powershell
dotnet build OpenCodeCostMeter.slnx
```

Output: `src\OpenCodeCostMeter\bin\Debug\net10.0\OpenCodeCostMeter.exe`

## Icon

The application and tray icon are generated from `src/OpenCodeCostMeter/Assets/icon.svg`. To regenerate `src/OpenCodeCostMeter/Assets/icon.ico` after editing the SVG:

```powershell
uv run --python 3.12 src/OpenCodeCostMeter/Assets/generate-icon.py
```

## Command-line options

- `--db-path <path>` - Use an alternative `opencode.db` location instead of the default `%USERPROFILE%\.local\share\opencode\opencode.db`.
- `--help` - Show help text and exit.

## Settings

Settings file: `OpenCodeCostMeter.settings.json` next to the exe. Delete to reset defaults. Includes window position (DIPs), opacity, poll interval, always-on-top, and whether the model breakdown list is expanded.

## Model Display Names

Rules file: `model-display-names.txt` next to the exe. Each line is `prefix|find1=replace1;find2=replace2`. Prefix `*` matches all models. Lines starting with `#` are comments.

## Project Structure

```
src/
├─ OpenCodeCostMeter.Core/          # net10.0 class library, platform-agnostic
│  ├─ Data/                         # Database access
│  ├─ Models/                       # Data models
│  ├─ Services/                     # Polling, settings persistence, display-name rules
│  ├─ ViewModels/                   # UI binding logic
│  ├─ Platform/IUiTimer.cs          # UI-thread timer seam
│  └─ LaunchOptions.cs              # Command-line parsing
└─ OpenCodeCostMeter/               # net10.0 Avalonia desktop app
   ├─ Assets/                       # Application icon source (SVG), ICO, and generation script
   ├─ Platform/ConsoleHelper.cs     # --help console output (IsWindows-guarded P/Invokes)
   ├─ Services/                     # TrayIconService, AvaloniaUiTimer
   ├─ App.axaml/.cs                 # Application bootstrap
   ├─ MainWindow.axaml/.cs          # Borderless topmost widget window
   ├─ DatabaseNotFoundWindow.cs     # DB-missing error dialog
   ├─ Program.cs                    # Entry point
   ├─ model-display-names.txt       # Display name formatting rules (copied to output)
   └─ OpenCodeCostMeter.csproj      # Project file
```
