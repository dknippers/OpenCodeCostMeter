## Task

Upgrade the OpenCode Cost Meter desktop app from Avalonia **11.3.18** to **12.1.0** (latest stable on NuGet, matching the local v12 clone). The WPF→Avalonia port (see `2026-07-17/wpf-to-avalonia-port.md`) is complete; this is a package bump plus the minimal code/XAML adjustments forced by v12 breaking changes. `OpenCodeCostMeter.Core` has zero Avalonia dependencies and stays untouched. The widget must look and behave identically afterwards.

## Research summary

Verified against the local clones (v11 = 11.3.18, v12 = 12.1.0 per `build/SharedVersion.props`), the official breaking-changes list (<https://docs.avaloniaui.net/docs/avalonia12-breaking-changes>), and NuGet version listings. Every Avalonia API the app touches was checked individually in the v12 source.

### What actually affects this app

| # | v12 change | Impact on this app | Action |
|---|---|---|---|
| 1 | `SystemDecorations` enum removed → `WindowDecorations`; `Window.SystemDecorations` kept as `[Obsolete]` shim | `MainWindow.axaml` line 6 `SystemDecorations="None"` (would compile with an obsolete warning) | Rename to `WindowDecorations="None"` — the only XAML edit |
| 2 | `Avalonia.Diagnostics` package **removed**; replacement is the paid Avalonia Plus Dev Tools (`AvaloniaUI.DiagnosticsSupport`) | csproj has a Debug-only ref, but `AttachDevTools` is never called anywhere (rg-verified) — template leftover | Delete the package reference, no replacement |
| 3 | Compiled bindings now on by default (`AvaloniaUseCompiledBindingsByDefault=true`) | `MainWindow.axaml` needs statically typed binding contexts for the window and model-row template | Keep the Avalonia 12 default; declare `x:DataType="viewModels:MainWindowViewModel"` on the window and `x:DataType="viewModels:ModelRowViewModel"` on the `DataTemplate` |
| 4 | `DispatcherTimer`/`AvaloniaSynchronizationContext` bind to the *current* dispatcher instead of the UI-thread dispatcher | None — every timer is constructed on the UI thread (`MainWindow` ctor debounce timer, `AvaloniaUiTimer` instances in `App.OnFrameworkInitializationCompleted`) | None. Keep the rule: never construct `AvaloniaUiTimer` off the UI thread |
| 5 | `Fonts.Avalonia.CascadiaCode` has no v12 build (still 0.14.0, compiled against Avalonia 11.0.11) | Runtime risk if the APIs it calls broke in v12 | Verified compatible: its DLL references only `AppBuilder.ConfigureFonts`, `FontManager.AddFontCollection`, `EmbeddedFontCollection(Uri,Uri)`, and `IFontCollection` — all present and signature-compatible in v12 (v12's own `Avalonia.Fonts.Inter` uses the identical pattern). Keep 0.14.0; smoke-test font rendering; fallback in step 5 |
| 6 | Text stack rework: own font parser, HarfBuzz shaping; `UsePlatformDetect()` now registers HarfBuzz automatically | Should be transparent | None; visual check that Inter/Cascadia render correctly |
| 7 | `Screen` is now abstract | None — app only consumes instances via `Screens.ScreenFromWindow`/`Screens.Primary` | None |
| 8 | `TopLevel` no longer implements `IInputRoot`/`IRenderRoot`/`ILayoutRoot` (moved to internal `PresentationSource`) | None — rg-verified no such casts; `TopLevel.RenderScaling` remains public in v12 | None |
| 9 | `Window.WindowState` styled→direct property | Not used | None |
| 10 | Binding hierarchy rework (`IBinding`/`InstancedBinding` removed; `Binding` kept as compat alias for `ReflectionBinding`) | None — bindings exist only in XAML | None |
| 11 | Data-annotations validation plugin now disabled by default | Positive — removes the known CommunityToolkit.Mvvm conflict; VMs use no validation attributes | None |
| 12 | Clipboard `IDataObject` removed, gesture events moved off `Gestures`, `GotFocusEventArgs` removed, `TitleBar`/`CaptionButtons`/`ExtendClientAreaChromeHints` removed | None — all unused here (rg-verified) | None |
| 13 | v12 TFMs: net8.0 + net10.0 (netstandard2.0 dropped) | App already targets `net10.0` | None |

### Verified unchanged in v12 (the app's hot paths)

- **`TrayIcon`** is byte-for-byte identical (`Clicked`, `Menu`, `WindowIcon`, `ToolTipText`, `IsVisible`); `NativeMenu`/`NativeMenuItem` unaffected.
- **`Window`**: `Position` (PixelPoint), `Topmost`, `SizeToContent`, `TransparencyLevelHint`, `CanResize`, `ShowInTaskbar`, `Closing`, `BeginMoveDrag`, `Activate`, `Show`/`Hide`, `WindowStartupLocation`. `WindowTransparencyLevel` struct identical; Win32 transparency/corner-preference behavior unchanged for the `Transparent` hint.
- **Screens/geometry**: `Screens.ScreenFromWindow`, `Screens.Primary`, `Screen.WorkingArea`/`Scaling`, `RenderScaling` on TopLevel, `PixelPoint`/`PixelSize`/`PixelRect`, `SizeChangedEventArgs` (byte-identical), `VisualExtensions.PointToScreen` (moved file, now routes through `IPresentationSource` — same semantics).
- **Timers**: `DispatcherTimer(DispatcherPriority)` ctor intact; `Interval`/`Tick`/`Start`/`Stop` unchanged (plus new convenience ctors).
- **Input**: `PointerPressed`/`PointerMoved`/`PointerReleased`, `GetCurrentPoint`, `Pointer.Capture`, `InitialPressMouseButton`, `KeyDown`/`Key` — unchanged (`KeyEventArgs` gains an interface, harmless).
- **Flyout**: `ContextFlyout`, `Opening`, `Hide()`, `Content`, `FlyoutPresenter` styling. (`FlyoutBase.IsOpen` became a publicly-settable styled property — not bound here, irrelevant.)
- **App model**: `SimpleTheme` class unchanged, `RequestedThemeVariant="Dark"` unchanged, `ShutdownMode.OnExplicitShutdown`, `Shutdown()`, `Exit` event, `StartWithClassicDesktopLifetime`, `UsePlatformDetect()`, `LogToTrace()`, `WithInterFont()`, `AvaloniaXamlLoader.Load`, `AvaloniaResource` items, `fonts:Inter#Inter` / `fonts:CascadiaCode#Cascadia Code` URI syntax.
- **Package availability**: `Avalonia.Desktop`, `Avalonia.Themes.Simple`, `Avalonia.Fonts.Inter` all published at 12.1.0.

## Implementation steps

1. **Packages** (`src/OpenCodeCostMeter/OpenCodeCostMeter.csproj`):
   - `Avalonia.Desktop` 11.3.18 → **12.1.0**
   - `Avalonia.Themes.Simple` 11.3.18 → **12.1.0**
   - `Avalonia.Fonts.Inter` 11.3.18 → **12.1.0**
   - Delete the `Avalonia.Diagnostics` reference (package removed in v12; unused here).
   - Keep `Fonts.Avalonia.CascadiaCode` 0.14.0 (verified compatible; step 4 proves it).
   - Do not set `AvaloniaUseCompiledBindingsByDefault`; retain Avalonia 12's compiled-binding default.
   - Core project: no changes.
2. **XAML** (`src/OpenCodeCostMeter/MainWindow.axaml`): `SystemDecorations="None"` → `WindowDecorations="None"`; add the `OpenCodeCostMeter.ViewModels` namespace and typed contexts for the window (`MainWindowViewModel`) and item template (`ModelRowViewModel`).
3. **Build**: `dotnet msbuild /t:Compile` — expect zero errors; any obsolete-API warning means a missed call site, fix it.
4. **Manual smoke test (Windows)** — same checklist as the port plan:
   - Startup shows today's cost + breakdown; **mono font is Cascadia Code and UI font is Inter** (proves the 0.14.0 font package works against v12).
   - Expand/collapse with quadrant anchoring (expand→collapse returns to original X,Y), drag, snap-to-edge, Center H/V, hotkeys H/V/A/T.
   - Right-click flyout: always-on-top toggle, poll-interval + opacity sliders (500 ms debounced save), Hide, Exit.
   - Tray icon: click toggles, menu Show/Hide/Exit, closing the window hides to tray.
   - Cost-delta highlighting, error state (bogus `--db-path`), `--help` output.
   - Repeat at 100% and 150% DPI (v12 reworked window internals; the pixel/DIP math must still hold).
5. **Fallback only if Cascadia Code fails to render**: vendor the font — embed the Cascadia Code TTFs as `AvaloniaResource` and add a `CascadiaCodeFontCollection : EmbeddedFontCollection` + `WithCascadiaCodeFont()` extension copied from `D:\code\Avalonia-v12\src\Avalonia.Fonts.Inter` (~30 lines), then drop the 0.14.0 package.
6. **Docs + commit**: update `AGENTS.md` Tech Stack (Avalonia 12.1.0, compiled bindings, Diagnostics removal) and commit the plan together with the implementation.

## Out of scope

- Avalonia Plus Dev Tools (paid replacement for `Avalonia.Diagnostics`).
- macOS packaging follow-ups from the port plan (unchanged).

## Acceptance criteria

- [ ] Clean build on `net10.0`; no 11.x Avalonia packages remain (`rg "11\.3\.18" src` hits nothing); no obsolete-API warnings.
- [ ] Avalonia 12 compiled bindings remain enabled and the window/item-template contexts are typed with `x:DataType`.
- [ ] No `Avalonia.Diagnostics` reference; app starts fine without it.
- [ ] Widget renders identically to v11, with Inter + Cascadia Code embedded fonts working (confirms 0.14.0 package compatibility).
- [ ] All port-plan behaviors still pass: quadrant anchoring, drag, snap-to-edge, Center H/V, hotkeys, flyout, tray, highlighting, error dialog, `--help`/`--db-path`.
- [ ] Verified at 100% and 150% DPI.
- [ ] `AGENTS.md` updated; plan + implementation committed together.
