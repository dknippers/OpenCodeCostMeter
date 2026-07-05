# Plan: OpenCode Token Usage Windows 11 Widget

## Full User Prompt

> We want to make a Windows 11 widget of some sort that displays a realtime (ish) number displaying the number of used tokens by OpenCode. It should be displayed in a way on the windows desktop, possibly above other apps and update the used input/output/cached read/cache write tokens every couple of seconds. It should read from the opencode database. Make a thorough plan to implement this. Analyze the opencode database first. The message or messages table should probably be the source of truth. The database is here: %USERPROFILE%\.local\share\opencode\opencode.db
>
> **Addendum 1:** The widget should **only ever display tokens used in the current calendar day** — not lifetime totals. The day boundary is local machine time. At/just after midnight, counters reset to the new day's totals.
>
> **Addendum 2:** We may want to show USD $ cost instead of (or alongside) token counts. Cost depends on the model used, and models are part of the per-message data — bake cost-by-model into v1, not as a deferred follow-up.
>
> **Addendum 3:** Resumed sessions must be handled correctly. A session created last week can be resumed today; its tokens used today must be attributed to today, not to the day the session was created. This rules out filtering by `session.time_created` or relying on `session.tokens_*` aggregates (which are cumulative across the whole session lifetime).
>
> **Addendum 4:** Performance is not a concern. Even a 500ms query is fine because we keep showing the last known value while a slow background refresh runs. Do not over-engineer the data layer for speed.

---

## 1. Database Analysis (findings)

Inspected `%USERPROFILE%\.local\share\opencode\opencode.db`. Findings and the chosen source of truth.

### 1.1 Tables overview

| Table | Rows | Relevance |
|---|---|---|
| `message` | 4331 | **PRIMARY SOURCE OF TRUTH.** Per-LLM-call rows; each assistant message's `data` JSON holds a `tokens` object AND a `cost` field AND `modelID`/`providerID`. The only table that can attribute tokens to a specific day and the only place per-model cost can be computed. |
| `session` | 148 | Aggregate per-session columns `tokens_input` etc., **cumulative across the whole session lifetime**. Useless for per-day attribution once a session spans multiple days (resumed sessions). Demoted to a sanity-check fallback for diagnostic purposes only. |
| `part` | 19330 | Sub-message parts (text/tool calls). Not used. |
| `event` | 10860 | Append-only event log. Not needed for totals; v1 reads the materialized `message` rows directly. |
| `event_sequence` | 21 | Per-aggregate last seq. Not needed in v1. |
| `session_message` | 6 | Only `model-switched`/`agent-switched` events. Not used. |
| `project` | 21 | For v2 per-project breakdown; not in v1 scope. |

### 1.2 `message` table — chosen source of truth

Schema:

```
id            TEXT PK
session_id    TEXT NOT NULL
time_created  INTEGER NOT NULL   -- ms epoch (insertion time of the row)
time_updated  INTEGER NOT NULL   -- ms epoch
data          TEXT NOT NULL       -- JSON
```

`data` JSON for an assistant message:

```json
{
  "role": "assistant",
  "tokens": { "total": 13975, "input": 4048, "output": 903, "reasoning": 0,
              "cache": { "write": 0, "read": 9024 } },
  "cost": 0.01198664,
  "modelID": "glm-5.2",
  "providerID": "opencode-go",
  "time": { "created": 1783237758064, "completed": 1783237785325 },
  "finish": "tool-calls"
}
```

Key fields used:
- `$.role` = `"assistant"` — filter to only count LLM responses (not user messages, which have no `tokens`).
- `$.time.completed` (ms epoch) — **the moment the LLM actually finished**. This is the correct timestamp to attribute a token spend to a calendar day, NOT `time_created` (which is when the row was inserted) and NOT `$.time.created` (which is when the request started, possibly before midnight).
- `$.tokens.input`, `$.tokens.output`, `$.tokens.reasoning`, `$.tokens.cache.read`, `$.tokens.cache.write` — the five counters.
- `$.cost` — USD spent on this single LLM call.
- `$.providerID` + `$.modelID` — which model; needed for cost breakdown and for any future per-model pricing verification.
- Rows where `$.time.completed` IS NULL are in-progress or aborted calls — excluded from totals (their tokens were not billed/used).

### 1.3 Why `session.tokens_*` is wrong for per-day

The `session` table maintains running totals:

```
tokens_input, tokens_output, tokens_reasoning,
tokens_cache_read, tokens_cache_write, cost
```

These are updated incrementally as the session runs but **never decremented and never partitioned by day**. A session created 2026-06-28 and resumed 2026-07-05 has all of its tokens — both weeks' worth — summed in those columns. Filtering `WHERE session.time_updated >= start_of_today` would catch the row but attribute its entire lifetime tokens to today. Unacceptable.

The only correct way to attribute tokens to "today" is to look at individual message completion timestamps. **→ message table is mandatory.**

### 1.4 Per-day aggregation queries (validated)

Local-day boundary (machine timezone):

```csharp
long startOfTodayMs = (long)DateTimeOffset.Now
    .Date.Subtract(DateTimeOffset.UnixEpoch).TotalMilliseconds;
```

(Use `DateTimeOffset.Now.Date` so the day boundary follows local time, not UTC.)

**Today totals — single query:**

```sql
SELECT
  COUNT(*),
  COALESCE(SUM(json_extract(data,'$.tokens.input')),0),
  COALESCE(SUM(json_extract(data,'$.tokens.output')),0),
  COALESCE(SUM(json_extract(data,'$.tokens.reasoning')),0),
  COALESCE(SUM(json_extract(data,'$.tokens.cache.read')),0),
  COALESCE(SUM(json_extract(data,'$.tokens.cache.write')),0),
  COALESCE(SUM(json_extract(data,'$.cost')),0)
FROM message
WHERE json_extract(data,'$.role')='assistant'
  AND json_extract(data,'$.time.completed') IS NOT NULL
  AND json_extract(data,'$.time.completed') >= :startOfTodayMs;
```

**Today per-model breakdown — single query (for cost view + tooltip):**

```sql
SELECT
  json_extract(data,'$.providerID') AS provider,
  json_extract(data,'$.modelID')    AS model,
  COUNT(*)                            AS messages,
  SUM(json_extract(data,'$.cost'))   AS cost,
  SUM(json_extract(data,'$.tokens.input'))        AS input,
  SUM(json_extract(data,'$.tokens.output'))       AS output,
  SUM(json_extract(data,'$.tokens.cache.read'))   AS cache_read
FROM message
WHERE json_extract(data,'$.role')='assistant'
  AND json_extract(data,'$.time.completed') IS NOT NULL
  AND json_extract(data,'$.time.completed') >= :startOfTodayMs
GROUP BY provider, model
ORDER BY cost DESC;
```

**Today's most recent session (for status line "current session"):**

```sql
SELECT s.id, s.title, s.model, s.time_updated
FROM session s
ORDER BY s.time_updated DESC
LIMIT 1;
```

(This is metadata only — title/model/age — not used for token totals.)

Validated against my live DB: 11 assistant messages today, all on glm-5.2, total $0.118, 35,577 input tokens, 9,071 output, 108,416 cache read. 20 iterations of the per-day aggregate took 528ms total → ~26ms median. Even at 50× the current row count it stays well under the "performance is not a concern" ceiling.

### 1.5 Read-only & concurrency

opencode writes to the DB from another process.

- Open connection with `Mode=ReadOnly` (Microsoft.Data.Sqlite connection string). This guarantees we never block writers and never accidentally mutate state.
- Set `Default Timeout=2` (seconds) — transient writer locks just retry rather than throw.
- Reuse one long-lived read-only connection per poll cycle, OR open a fresh one each tick. Reuse is simpler; v1 reuses one. If a poll ever throws `SQLITE_BUSY` past the timeout, swallow, keep last-known values on screen, and retry next tick. (Last-known-value persistence matters precisely because per-day aggregates can be slow under contention — see Addendum 4.)
- No writes; no schema changes; no triggers; no indexes added. (We could optionally add a covering expression index on `json_extract(data,'$.time.completed')` — NOT in v1; out of scope, would mutate the user's opencode DB.)

---

## 2. Architecture

```
TokenTrackerWidget/
├─ TokenTrackerWidget.csproj
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml / MainWindow.xaml.cs
├─ Models/
│  ├─ DayUsageSnapshot.cs        # today's totals + per-model breakdown + status
│  └─ WidgetSettings.cs          # position, always-on-top, interval, displayMode
├─ Data/
│  ├─ IUsageRepository.cs
│  ├─ MessageTableRepository.cs   # PRIMARY: today aggregates from message table
│  └─ ISanityChecker.cs          # (diagnostic only) cross-check vs session table
├─ Services/
│  ├─ UsagePoller.cs             # DispatcherTimer → repo → event
│  └─ SettingsStore.cs           # JSON next to exe
└─ Converters/                  # BigNumberConverter, CurrencyConverter, etc.
```

### 2.1 Technology choice

- **WPF on .NET 10** (per AGENTS.md preference). Gives us:
  - `WindowStyle=None` + `AllowsTransparency=True` + `Topmost=True` — borderless widget floating above other apps.
  - `ShowInTaskbar=False` so it doesn't pollute the taskbar.
  - `DispatcherTimer` for the polling loop.
- **Microsoft.Data.Sqlite** (NuGet) — bundled SQLite, zero native deps; connection string `Data Source=...;Mode=ReadOnly;Default Timeout=2`.
- **CommunityToolkit.Mvvm** — `ObservableObject` / `[ObservableProperty]` for VM.
- **No Win11 Widget Board integration** — that platform limits refresh to ~15 min and cannot do "above other apps". A bespoke topmost window delivers the realtime-ish UX requested. Dev Home widget provider stays out of scope.

### 2.2 Polling strategy

- `DispatcherTimer` at **2.5 s** default (configurable 1–30 s). Per Addendum 4, even a slow tick is fine because the UI keeps showing the last known snapshot while a refresh is in flight.
- Each tick (on a background `Task` to keep UI thread free; marshal back via `DispatcherQueue`/`Dispatcher.InvokeAsync`):
  1. Compute `startOfTodayMs` from `DateTimeOffset.Now.Date`. **Recompute every tick** so a midnight rollover is detected without restarting the widget.
  2. Query `MessageTableRepository.GetTodayAsync(startOfTodayMs)` returning `DayUsageSnapshot`.
  3. Detect day rollover: if `snapshot.DayKey` (yyyy-MM-dd local) differs from previous snapshot's, **reset the snapshot** (counters start fresh from 0 for the new day; we do NOT carry anything over).
  4. Update ViewModel; bindings refresh.
- The per-model breakdown query runs **on the same tick** (cheap; piggybacks on the same read). It feeds both the cost view and the tooltip.
- Diagnostic only (off the hot path, triggered by context menu): `session`-table cross-check comparing today-flagged sessions' cumulative tokens vs the message-table today total. Surfaces drift, never used as the displayed value.

### 2.3 What the widget shows

Compact vertical card (~240×220 px), translucent dark background, two switchable display modes via context menu / hotkey:

**Mode A — tokens (default):**
```
┌──────────────────────────────────┐
│ OpenCode · today            ⚙ ✕   │
│ glm-5.2 · live                  │
│ ──────────────────────────────── │
│   input        35.6k            │
│   output        9.1k            │
│   reasoning       0             │
│   cache ⟲     108.4k            │
│   cache ✎        0              │
│   ──────────────────────────    │
│   cost        $0.12  ⓘ          │
│ Tokens today across 11 calls    │
└──────────────────────────────────┘
```
Hover `ⓘ` (or switch to Mode B) → per-model breakdown tooltip / panel:
```
glm-5.2 (opencode-go)   11 calls   $0.118
   in 35.6k  out 9.1k  cache 108.4k
```

**Mode B — cost:**
```
┌──────────────────────────────────┐
│ OpenCode · today · $             │
│ ──────────────────────────────── │
│   $0.12                          │
│   ──────────────────────────    │
│ glm-5.2        11 calls  $0.118 │
│ Tokens: in 35.6k / out 9.1k     │
└──────────────────────────────────┘
```

Common chrome:
- Header line: "OpenCode · today" + the most recent session's `model` and a `live`/`idle` pill (live when `session.time_updated` within last 60s, idle otherwise). The session is informational — it labels which model is currently being used — but its tokens are NOT summed here (per §1.3).
- Footer: "Tokens today across N calls" using `COUNT(*)` from the today query.
- Number formatting: `>=1e6 → "M"`, `>=1e3 → "k"`, cost always `$0.00` with 2 decimals (or `$1.23` etc.).
- Subtle pulse animation on the cost line whenever it changes.

### 2.4 Interactions & context menu (right-click)

- Display mode — Tokens / Cost / Both (stacked)
- Always on top — toggle (default on)
- Poll interval — 1s / 2.5s / 5s / 10s / 30s
- Transparency — 60% / 80% / 100%
- Collapse to pill (double-click body also toggles)
- Run sanity check vs `session` table (diagnostic; shows diff for today-flagged sessions)
- Launch on Windows startup (create shortcut in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`)
- Reset window position
- About
- Quit

### 2.5 Persisted settings (JSON next to exe: `TokenTrackerWidget.settings.json`)

```json
{
  "x": 1840, "y": 40,
  "width": 240, "height": 220,
  "alwaysOnTop": true,
  "pollIntervalSeconds": 2.5,
  "opacity": 0.85,
  "displayMode": "Tokens",
  "collapsed": false
}
```

On close, write current bounds + options. On launch, restore.

### 2.6 Robustness

- DB path missing → show "Locating opencode.db…" with Browse… prompt; remember override in settings.
- DB locked past timeout → swallow, keep last-known snapshot on screen, log to debug, retry next tick. UI shows a small `(retrying)` chip on the cost line until the next successful read (Addendum 4 explicitly accepts this UX).
- opencode not running → fine, values just stop changing; idle pill stays.
- Schema drift: read all `json_extract` results defensively; if a column / JSON path returns NULL unexpectedly, treat as 0 for sums and surface a one-time warning toast rather than crashing.
- Day rollover: handled in §2.2 step 3 — counters naturally reset because the WHERE clause changes at midnight.
- Resumed-session correctness: handled structurally by always reading from `message` filtered by `$.time.completed`; no special case needed. (This is the load-bearing assumption from Addendum 3.)

### 2.7 Performance strategy (non-binding per Addendum 4)

- Two queries per tick + one metadata query for the active session. Worst-case observed ~26ms median; even 500ms is acceptable.
- No indexing of the user's opencode DB (out of scope; would mutate their data).
- UI thread never blocks — all DB work off the dispatcher.

**Decision: full-sweep per tick, no incremental in-memory cache.**

Considered alternative: cache the running per-model sum in memory, track `MAX($.time.completed)` seen so far, and on each tick query only `WHERE $.time.completed > lastSeen` then add the delta to the cached totals. Rejected for v1 because:

- The full sweep is already 26ms. Incremental would save single-digit ms — not meaningful against a 500ms ceiling.
- Incremental adds real bug surface: in-flight rows (`$.time.completed IS NULL`) need separate polling until they complete; day-rollover must reset the cache; any missed row causes permanent drift until a reconciliation sweep runs.
- The full sweep is **self-correcting**. If opencode ever mutates a row's `tokens`/`cost`, or we glitch and skip a poll, or a row completes out of order, the next tick recomputes from scratch and the displayed value is correct again. Incremental caching requires a periodic full-sweep reconciliation to gain that property — at which point you're running both strategies and have doubled the complexity.
- Defer until row counts actually grow into a regime where it matters (hundreds of thousands of assistant rows per day). If that ever happens, add a `since` cursor + a low-frequency (e.g. once per minute) reconciliation sweep, gated behind a row-count threshold.

The per-model breakdown is already held in `ViewModel` memory between ticks (as the last snapshot) — the "store current sum per model in memory" concern is addressed by the snapshot itself. Only the *query* recomputes from scratch each tick, which is exactly the property we want for correctness.

---

## 3. Implementation steps (ordered)

1. **Scaffold solution** — `dotnet new wpf -n TokenTrackerWidget -f net10.0` in repo root. Add NuGet: `Microsoft.Data.Sqlite`, `CommunityToolkit.Mvvm`. Confirm `dotnet msbuild /t:Compile` succeeds.
2. **Models** — `DayUsageSnapshot` (today's `DayKey`, totals for in/out/reason/cacheRead/cacheWrite/cost, `Calls` count, `List<ModelBreakdown> ModelBreakdowns`, plus `ActiveSessionTitle/Model/IsLive`), `WidgetSettings`. Pure DTOs.
3. **Repository** — `MessageTableRepository.GetTodayAsync(startOfTodayMs)` runs the two §1.4 queries + the active-session metadata query, returns `DayUsageSnapshot`. Read-only connection string, reuse one connection, `Default Timeout=2`.
4. **SanityChecker (diagnostic only)** — `SessionTableSanityChecker.CrossCheckTodayAsync(startOfTodayMs)` returns the diff between today-flagged sessions' cumulative token totals and the message-table today total. Used only by the context-menu action; surfaces drift, never alters displayed values.
5. **Poller service** — `UsagePoller` with `DispatcherTimer`, `Start/Stop`, `Interval`, `SnapshotUpdated` event, `ErrorOccurring` event (carrying the last good snapshot). Catches repo exceptions; never throws to the VM.
6. **ViewModel** — `WidgetViewModel : ObservableObject`. Exposes formatted strings: `TodayInputText`, `TodayOutputText`, `TodayCacheReadText`, `TodayCostText`, `ActiveModelText`, `IsLive`, `DayKey`, `ModelBreakdowns`. Subscribes to `UsagePoller`.
7. **MainWindow XAML** — borderless, transparent, topmost, single-grid layout matching §2.3. `Border CornerRadius="8"` with semi-transparent background. `Border.MouseLeftButtonDown → DragMove()` for drag. Double-click → toggle collapsed pill.
8. **MainWindow code-behind** — wire VM, load settings, size/position window, hook context menu, persist settings on close.
9. **Settings store** — JSON read/write with `System.Text.Json`; defaults applied if file missing.
10. **Converters** — `BigNumberConverter` (>=1e6 → "M", >=1e3 → "k"), `CurrencyConverter` (2 decimals, `$`), `AgeToLiveBrushConverter` (green ≤60s, gray otherwise).
11. **Startup integration** — menu toggle writes/removes a `.lnk` in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup` (via `WindowsShortcuts` NuGet or a tiny WshShell interop).
12. **Sanity-check menu action** — wire `SessionTableSanityChecker`, show diff in a small popup/footer, log to debug output.
13. **Polish** — collapse-to-pill double-click, hover opacity boost, subtle pulse animation on data refresh when cost changes, muted dark Material-style theme, tooltips showing per-model breakdown.

---

## 4. Validation

- `dotnet msbuild /t:Compile` after each meaningful change (avoids locking the EXE).
- **Correctness vs manual SQL**: run the widget, then independently run the §1.4 query (e.g. via `uv run python`) and confirm the on-screen numbers match exactly.
- **Resumed-session test** (the load-bearing case from Addendum 3): resume an old session created a previous day, generate a couple of LLM calls today, and confirm those tokens appear in today's widget but the previous days' tokens for that same session do NOT. Cross-check against `SELECT tokens_* FROM session WHERE id = '…'` — the session aggregate will be huge, but the widget's contribution from that session for today will only be today's calls.
- **Midnight rollover test**: with the widget running, simulate day change either by waiting (manual) or by injecting a fake "today" timestamp via a debug seam — confirm counters reset to the new day's totals at midnight and don't carry over.
- **Lock contention test**: hold the DB open in a writer (opencode mid-call) and confirm the widget keeps showing previously known values without throwing, then resumes when the writer releases.
- **Schema-shrink test**: temporarily rename `cost`/`tokens` paths in a copy of the DB and confirm the widget surfaces a warning and treats NULLs as 0 rather than crashing.
- **Position persistence test**: move window, quit, relaunch — position restored.
- **Cold-start test**: delete settings file — defaults applied, window appears in default corner.

---

## 5. Out of scope (deferred)

- Per-project / per-day historical view (`project` + `session.time_created` joins). Out of v1.
- Live event-stream tracking via the `event` table — v2 nicety, not needed for correct totals.
- System tray icon — v1 stays a plain topmost window.
- Adding expression indexes to the user's opencode DB — would mutate their data; out of v1.
- Custom pricing overrides per model — v1 trusts `$.cost` as opencode logged it; future follow-up could recompute from a per-model pricing table.