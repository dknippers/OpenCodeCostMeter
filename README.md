# OpenCode Cost Meter

A small borderless Windows 11 desktop widget that shows today's OpenCode LLM
spend in real time-ish, broken down by model.

The widget reads the [opencode](https://opencode.ai) SQLite database
(`%USERPROFILE%\.local\share\opencode\opencode.db`) directly in read-only mode,
aggregates the `message` table for the current local calendar day, and refreshes
every couple of seconds.

## Features

- **Today only.** Counts reset at local midnight. Each LLM call is attributed to
  the day it *completed* (`json_extract(data,'$.time.completed')`), so resuming a
  session that started on a previous day only shows today's tokens — not the
  whole session's lifetime.
- **Cost-per-model.** Big cost figure at the top, with a small up-arrow delta
  when the spend increased since the last poll. Per-model breakdown rows below it.
  Models that rounded to `$0.00` are hidden.
- **Realtime-ish.** Polls the opencode database on a configurable interval
  (2 / 4 / 8 / 16 / 32 seconds; default 4s). Slow queries don't freeze the UI —
  the last known values stay on screen while a refresh runs in the background.
- **Always on top.** Borderless, transparent, topmost WPF window sitting quietly
  in the corner of your desktop.
- **No front-end writes.** Read-only SQLite connection; never mutates your
  opencode database. Survives the database being locked by opencode mid-call.

## Why the `message` table and not the `session` table?

The `session` table maintains cumulative `tokens_*` columns across the entire
session lifetime. A session created last week and resumed today would otherwise
attribute all of its lifetime tokens to today. Filtering sessions by
`session.time_updated` doesn't help — those tokens are still summed in the
aggregate.

The `message` table is the per-LLM-call source of truth; each assistant
message's `data` JSON carries a `tokens` object, a `cost`, and a
`$.time.completed` timestamp that we can attribute to a specific calendar day.
See the [plan](.opencode/plans/2026-07-05/opencode-token-widget.md) for the
full rationale and schema analysis.

## Build & run

Requires .NET 10.

```powershell
dotnet build src\OpenCodeCostMeter.csproj
.\src\bin\Debug\net10.0-windows\OpenCodeCostMeter.exe
```

## Settings

A `OpenCodeCostMeter.settings.json` file is written next to the exe and stores
window position/width, opacity, polling interval, and the always-on-top flag.
Delete it to reset to defaults.

## Project layout

```
src/
├─ App.xaml / App.xaml.cs           # bootstraps MainWindow and locates DB
├─ MainWindow.xaml / .xaml.cs       # borderless topmost widget + context menu
├─ Models/
│  ├─ DayUsageSnapshot.cs           # today's totals + per-model breakdown
│  └─ WidgetSettings.cs             # persisted JSON settings
├─ Data/
│  ├─ MessageTableRepository.cs     # primary source (today aggregates)
│  ├─ SessionTableSanityChecker.cs  # unused diagnostic cross-check
│  └─ DbLocator.cs                  # resolves DB path, local-day bounds
├─ Services/
│  ├─ UsagePoller.cs                # DispatcherTimer → repo → events
│  └─ SettingsStore.cs              # JSON settings persistence
├─ ViewModels/
│  ├─ WidgetViewModel.cs             # binds to the main card
│  └─ ModelRowViewModel.cs           # one per breakdown row
├─ Converters/                      # WPF value converters
├─ ModelColorResolver.cs            # provider/model → accent color
└─ FormatUtil.cs                    # number/currency formatting
```
