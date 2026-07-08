# AGENTS.md

## Project Overview

OpenCode Cost Meter is a Windows 11 desktop widget that displays today's OpenCode LLM spend in real-time, broken down by model. It reads the opencode SQLite database directly (read-only) and refreshes every few seconds.

## Tech Stack

- **.NET 10** (WPF + Windows Forms, Windows target)
- **CommunityToolkit.Mvvm** 8.4.2 - MVVM framework
- **Microsoft.Data.Sqlite** 10.0.9 - SQLite access
- **SQLitePCLRaw.lib.e_sqlite3** 2.1.11 - native SQLite
- **System.Windows.Forms.NotifyIcon** - system tray icon

## Database

The widget reads from opencode's SQLite database at `%USERPROFILE%\.local\share\opencode\opencode.db`.

Key details about the schema:

- Uses the `message` table (not `session`) because `session` maintains cumulative tokens across the entire session lifetime, which would incorrectly attribute past tokens to today's date.
- Each assistant message has a `data` JSON column containing `$.time.completed` (Unix ms timestamp), `$.cost`, `$.providerID`, and `$.modelID`.
- The widget filters messages by `$.time.completed` to only count today's calls.

## Architecture

### Data Layer (`Data/`)

- **DbLocator** - Resolves the database path (default or from the `--db-path` command-line argument)
- **DayKey** - Static helper in `DbLocator.cs`; converts a Unix-ms timestamp to a `yyyy-MM-dd` string
- **MessageTableRepository** - Primary repo that queries the `message` table for today's per-model cost breakdowns. Single SQL query with inner `GROUP BY (time.created, time.completed)` to deduplicate forked messages before aggregating per provider/model. Selects only `providerID`, `modelID`, and `cost`.
- **IUsageRepository** - Interface for repositories

### Services (`Services/`)

- **UsagePoller** - DispatcherTimer-based poller that calls the repo and fires Updated/Error events. Implements `IDisposable`. Has an `_inFlight` guard to prevent overlapping queries if a poll takes longer than the interval.
- **SettingsStore** - Persists/loads WidgetSettings to JSON file next to the exe
- **TrayIconService** - Wraps a Windows Forms `NotifyIcon`. Double-clicking the tray icon toggles widget visibility; the context menu has **Exit**. Loads the embedded `Assets/icon.ico`. Closing the widget window hides it to the tray; only **Exit** terminates the application.

### ViewModels (`ViewModels/`)

- **WidgetViewModel** - Main VM; binds to the UI, tracks cost deltas for highlighting, manages ModelRows collection
- **ModelRowViewModel** - One per breakdown row in the details section

### Models (`Models/`)

- **DayUsageSnapshot** - Today's aggregated data (`DayKey`, cost, per-model breakdown, taken-at timestamp)
- **ModelBreakdown** - Per-model cost
- **WidgetSettings** - Persisted JSON settings (window position, opacity, poll interval, always-on-top)

## Key Design Decisions

1. **Read-only SQLite** - Connection uses `SqliteOpenMode.ReadOnly` and `DefaultTimeout = 2` to survive database locks
2. **Today only** - Tokens are attributed to the day the message _completed_, not when the session started
3. **Non-blocking UI** - Slow queries don't freeze the UI; last known values stay on screen during refresh
4. **Cost delta highlighting** - New spend since last poll is highlighted briefly
5. **System tray** - The widget lives in the system tray; closing the widget hides it to the tray. **Hide** and **Exit** are available in the widget's context menu; the tray menu has **Exit**.

## Building

```powershell
dotnet build src\OpenCodeCostMeter.csproj
```

Output: `src\bin\Debug\net10.0-windows\OpenCodeCostMeter.exe`

## Icon

The application and tray icon are generated from `src/Assets/icon.svg`. To regenerate `src/Assets/icon.ico` after editing the SVG:

```powershell
uv run --python 3.12 src/Assets/generate-icon.py
```

## Command-line options

- `--db-path <path>` - Use an alternative `opencode.db` location instead of the default `%USERPROFILE%\.local\share\opencode\opencode.db`.
- `--help` - Show help text and exit.

## Settings

Settings file: `OpenCodeCostMeter.settings.json` next to the exe. Delete to reset defaults.

## Project Structure

```
src/
├─ Assets/                        # Application icon source (SVG), ICO, and generation script
├─ Data/                          # Database access
├─ Models/                        # Data models
├─ Services/                      # Polling, settings persistence, and tray icon
├─ ViewModels/                    # UI binding logic
├─ Converters/                    # WPF value converters (BoolToVisibility, BoolToDouble, BoolToBrush)
├─ MainWindow.xaml/.cs            # Borderless topmost widget window
├─ App.xaml/.cs                   # Application bootstrap
└─ OpenCodeCostMeter.csproj       # Project file
```
