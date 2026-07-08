## Task

Fix three performance issues and one doc error in OpenCodeCostMeter:

1. **Settings debounce** — `SettingsStore.Save()` writes to disk on every `Slider.ValueChanged` tick during drag (dozens of times/sec). Add a 500ms debounce timer so save fires only after the user stops dragging.
2. **ObservableCollection batch update** — `ModelRows.Clear()` + individual `Add()` calls fire `CollectionChanged` N+1 times, each triggering a layout pass due to `SizeToContent="WidthAndHeight"`. Batch updates by building a new list and swapping with a single `AddRange`.
3. **ConcurrentDictionary → Dictionary** — `ModelDisplayNameRules.Format()` is only called from the UI thread. Replace the `ConcurrentDictionary` cache with a plain `Dictionary<string, string>`.
4. **AGENTS.md cleanup** — Remove the stale reference to `Converters/FormatUtil.cs` which does not exist.
