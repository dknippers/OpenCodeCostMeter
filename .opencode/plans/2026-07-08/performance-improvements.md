# Performance improvements

## Task

Reduce per-poll allocation and UI churn in the OpenCode Cost Meter widget. The
widget polls the opencode SQLite database every 5-10 seconds and rebuilds its
entire breakdown UI on every poll, even when nothing changed. Several hot paths
also re-do stable work (culture lookups, model display-name formatting, model-key
string concatenation) on every update. The goal is to eliminate this redundant
work so polling stays cheap as the `message` table grows and the number of
distinct models in use grows.

The opencode database is fully read-only and externally owned — no schema
changes, no indexes, no incremental aggregation that depends on append-only
behavior (forked messages share timestamps and would be missed by a
"only query newer than last seen" strategy). All improvements are confined to
the widget's own code.

## Out of scope

- **Database schema / indexes** — DB is read-only and externally managed.
- **Incremental aggregation** (tracking max `time.completed`) — forked messages
  reuse old timestamps, so a "only new rows" query would miss dedup of forked
  history. Too risky for the marginal gain on a local DB.
- **Synchronous query on the timer tick** — `Task.Run` + `await` is intentional
  per the "Non-blocking UI" design decision in AGENTS.md. Keeping last known
  values on screen during a slow query is a feature; dropping the async state
  machine is not worth losing that.

## Findings to implement

### 1. Cache `CultureInfo.GetCultureInfo("en-US")`

**Files:** `src/ViewModels/WidgetViewModel.cs` (lines 68, 84, 116),
`src/ViewModels/ModelRowViewModel.cs` (line 18).

`CultureInfo.GetCultureInfo("en-US")` does an internal lookup on every call.
It is invoked once per poll for the total cost and once per model row (twice:
once for highlight detection, once for `_lastModelCostTexts`). Replace with a
`static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US")` field
in each VM (or a shared internal helper).

### 2. Cache `ModelDisplayNameRules.Format()` results

**File:** `src/Services/ModelDisplayNameRules.cs`.

`Format()` runs `modelId.Replace('-', ' ')` (alloc) + `ToTitleCase` (alloc) +
a `Replace` loop over rules per call. The same model IDs are formatted on every
poll for the lifetime of the process. Add a
`ConcurrentDictionary<string, string>` cache keyed by `modelId`. The rules
file is loaded once into a `Lazy<>`, so cached names stay consistent for the
process. Memory growth is bounded by the number of distinct model IDs, which
is small.

### 3. Diff `ModelRows` instead of clear + rebuild

**File:** `src/ViewModels/WidgetViewModel.cs` (lines 94-105).

Every poll currently `ModelRows.Clear()`s the `ObservableCollection` and re-adds
every row, which:
- Fires a `CollectionChanged.Reset`, forcing WPF to re-bind and re-measure every
  row even when content is identical.
- Allocates new `ModelRowViewModel` instances (re-running `Format()` per #2).
- Discards per-row highlight state.

Replace with a diff keyed by model key (`Provider/Model`):
- Build a dictionary of new breakdowns by key.
- For rows already in `ModelRows`, update `IsCostHighlighted` in place; remove
  rows whose key is no longer present.
- Append rows for new keys in SQL order (the query is `ORDER BY SUM(cost) DESC,
  modelID ASC`).
- If the keyed sequence is identical to last poll and all cost texts match,
  skip the diff entirely (no `CollectionChanged` at all).

This is the highest-impact change: it removes per-poll layout churn and most
per-poll allocations once #2 lands.

### 4. Drop unused per-model token fields

**Files:** `src/Models/ModelBreakdown.cs`, `src/Data/MessageTableRepository.cs`
(lines 18-22 select, line 87-93 reader).

`ModelRowViewModel` only reads `b.Model` and `b.Cost`. `ModelBreakdown` also
carries `Input`, `Output`, `CacheRead` which are never surfaced per-model. The
SQL `SUM`s them, the reader reads them, and `WidgetViewModel` sums them into
`DayUsageSnapshot` totals — but the per-model copies are dead.

Slim `ModelBreakdown` to `(Provider, Model, Cost)`. Remove the per-model
`tokens_*` SUMs from `PerModelSql` and the corresponding reads in
`GetToday`. The `DayUsageSnapshot` totals (`Input`, `Output`, `Reasoning`,
`CacheRead`, `CacheWrite`) remain and are still computed by the SQL
(in the same query) — only the per-model breakdown columns are removed.

This shrinks the result set and removes work per poll. Reversible if per-model
token breakdown is ever needed in the UI.

### 5. Single-pass `OnUpdated`

**File:** `src/ViewModels/WidgetViewModel.cs` (lines 81-117).

`OnUpdated` loops over `snap.Models` three times: highlight detection (81-89),
row rebuild (94-105), `_lastModelCostTexts` rebuild (113-117). It also allocates
a `new HashSet<string>()` (line 79) every update. Fuse into a single loop that,
per breakdown:
- Computes the key once (see #6).
- Compares against `_lastModelCostTexts` for highlight detection.
- Diffs into `ModelRows` (see #3).
- Updates `_lastModelCostTexts` (reuse the dictionary; clear-once-at-end or
  rebuild cheaply since it's small).

Drop the throwaway `newlyHighlighted` `HashSet` by applying highlights directly
to the in-place rows.

### 6. Memoize `ModelKey`

**File:** `src/ViewModels/WidgetViewModel.cs` (line 125).

`ModelKey(b)` does `$"{b.Provider}/{b.Model}"` (or returns `b.Model`) and is
called multiple times per model per poll (lines 83, 91, 98, 116 in the current
code). Compute it once per breakdown in the single fused loop and pass it
through. No need to store it on `ModelBreakdown` itself (it's a pure function of
already-present fields) — a local in the loop is enough.

## Implementation order

1. #1 (culture cache) — trivial, isolated.
2. #2 (display-name cache) — isolated, unblocks #3's benefit.
3. #4 (slim `ModelBreakdown`) — model change; do before #3/#5 so the diff
   code is written against the final shape.
4. #6 (memoize `ModelKey`) — small helper change, lands inside #5.
5. #5 (single-pass `OnUpdated`) — the fused loop, including #6.
6. #3 (diff `ModelRows`) — builds on #5's single pass; the diff is the body
   of the fused loop.

## Verification

- `dotnet msbuild /t:Compile /p:WarningLevel=0` in `src\` to confirm no build
  errors / file locks.
- Manual run (`src\bin\Debug\net10.0-windows\OpenCodeCostMeter.exe`) to confirm:
  - Total cost still updates.
  - Breakdown rows appear, expand/collapse, and highlight on cost change.
  - Refresh button still forces an update.
  - No-row state (no spend today) still shows correctly.
- No automated tests exist in the repo; verification is build + manual.