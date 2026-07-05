# AGENTS.md

## Project Overview

OpenCodeCostMeter is a Windows 11 desktop widget that displays today's OpenCode LLM spend in real-time, broken down by model. It reads the opencode SQLite database directly (read-only) and refreshes every few seconds.

## Tech Stack

- **.NET 10** (WPF, Windows target)
- **CommunityToolkit.Mvvm** 8.4.2 - MVVM framework
- **Microsoft.Data.Sqlite** 10.0.9 - SQLite access
- **SQLitePCLRaw.lib.e_sqlite3** 2.1.11 - native SQLite

## Database

The widget reads from opencode's SQLite database at `%USERPROFILE%\.local\share\opencode\opencode.db`.

Key details about the schema:
- Uses the `message` table (not `session`) because `session` maintains cumulative tokens across the entire session lifetime, which would incorrectly attribute past tokens to today's date.
- Each assistant message has a `data` JSON column containing `$.time.completed` (Unix ms timestamp), `$.tokens.*`, `$.cost`, `$.providerID`, and `$.modelID`.
- The widget filters messages by `$.time.completed` to only count today's calls.

## Architecture

### Data Layer (`Data/`)
- **DbLocator** - Resolves the database path (default or from the `--db-path` command-line argument)
- **MessageTableRepository** - Primary repo that queries the `message` table for today's totals and per-model breakdowns
- **SessionTableSanityChecker** - Unused diagnostic cross-check (not wired up)
- **IUsageRepository** - Interface for repositories

### Services (`Services/`)
- **UsagePoller** - DispatcherTimer-based poller that calls the repo and fires Updated/Error events
- **SettingsStore** - Persists/loads WidgetSettings to JSON file next to the exe

### ViewModels (`ViewModels/`)
- **WidgetViewModel** - Main VM; binds to the UI, tracks cost deltas for highlighting, manages ModelRows collection
- **ModelRowViewModel** - One per breakdown row in the details section

### Models (`Models/`)
- **DayUsageSnapshot** - Today's aggregated data (input/output/reasoning/cache tokens, cost, calls, per-model breakdown, active session info)
- **ModelBreakdown** - Per-model cost and token counts
- **WidgetSettings** - Persisted JSON settings (window position, opacity, poll interval, always-on-top)

## Key Design Decisions

1. **Read-only SQLite** - Connection uses `SqliteOpenMode.ReadOnly` and `DefaultTimeout = 2` to survive database locks
2. **Today only** - Tokens are attributed to the day the message *completed*, not when the session started
3. **Non-blocking UI** - Slow queries don't freeze the UI; last known values stay on screen during refresh
4. **Cost delta highlighting** - New spend since last poll is highlighted briefly

## Building

```powershell
dotnet build src\OpenCodeCostMeter.csproj
```

Output: `src\bin\Debug\net10.0-windows\OpenCodeCostMeter.exe`

## Command-line options

- `--db-path <path>` - Use an alternative `opencode.db` location instead of the default `%USERPROFILE%\.local\share\opencode\opencode.db`.
- `--help` - Show help text and exit.

## Settings

Settings file: `OpenCodeCostMeter.settings.json` next to the exe. Delete to reset defaults.

## Project Structure

```
src/
├─ Data/                         # Database access
├─ Models/                       # Data models
├─ Services/                     # Polling and settings persistence
├─ ViewModels/                    # UI binding logic
├─ Converters/                   # WPF value converters (BoolToVisibility, BoolToDouble, BoolToBrush)
├─ ModelColorResolver.cs         # Provider/model → accent color
├─ FormatUtil.cs                 # Number/currency formatting
├─ MainWindow.xaml/.cs           # Borderless topmost widget window
├─ App.xaml/.cs                   # Application bootstrap
└─ OpenCodeCostMeter.csproj      # Project file
```
