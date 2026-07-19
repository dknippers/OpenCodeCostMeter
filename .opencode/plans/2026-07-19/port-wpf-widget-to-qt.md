## Task

Port OpenCode Cost Meter from its Windows-only .NET 10 WPF/Windows Forms implementation to a Qt application that runs on Windows, macOS, and Linux while retaining the current widget's behavior and visual language. Bundle the required UI and monospace fonts where licensing permits; use suitable open-licensed replacements where it does not.

## Recommendation

Use **Qt 6.8 LTS, C++20, Qt Widgets, and CMake**. Qt Widgets is the closest practical equivalent to the present WPF implementation: the application is a compact, imperative, frameless desktop widget with a custom context menu, rather than a complex declarative UI. This keeps the port small and makes event-driven window dragging, size anchoring, tray integration, and custom menu controls straightforward.

Do not port the .NET/WPF project in place or attempt a partial cross-platform build. Create a clean native Qt application next to the existing `src/` implementation, then remove the WPF project only after feature-parity validation and release packaging are complete.

## Font Decision

Bundle these fonts as Qt resources:

| Existing role | Selected font | Decision |
|---|---|---|
| Monospace numbers and model names | Cascadia Mono | Bundle the unmodified font and its license notice. Cascadia Code is available under SIL OFL 1.1, which explicitly permits embedding and redistribution with software. |
| UI/menu text | Inter | Replace Segoe UI. Microsoft lists Segoe UI as exclusively included with Microsoft products/services and directs redistribution to separate licensing. It must not be copied from Windows into this application. Inter is a close, highly legible sans-serif alternative licensed under SIL OFL 1.1. |

GitHub Monaspace is not needed. It is a valid SIL OFL 1.1 fallback if Cascadia Mono proves unsuitable, but Cascadia already meets the visual requirement and can legally be bundled.

Ship the exact license texts and copyright notices for Cascadia and Inter in `THIRD-PARTY-NOTICES.md` and in every distributable package. Load fonts using `QFontDatabase::addApplicationFont()` before any widget is created. Fail loudly in development and fall back to the platform's fixed/system font in release builds only if a resource cannot load.

## Target Structure

Create a new `qt/` source root, retaining the current `src/` tree until cutover:

```text
qt/
  CMakeLists.txt
  cmake/                         # deployment and platform packaging helpers
  resources/
    app.qrc
    icons/icon.svg
    fonts/CascadiaMono-*.ttf
    fonts/Inter-*.ttf
    licenses/Cascadia-OFL-1.1.txt
    licenses/Inter-OFL-1.1.txt
  src/
    main.cpp
    app_controller.{h,cpp}
    data/db_locator.{h,cpp}
    data/message_table_repository.{h,cpp}
    models/day_usage_snapshot.h
    models/model_breakdown.h
    models/settings.h
    services/settings_store.{h,cpp}
    services/usage_poller.{h,cpp}
    services/model_display_name_rules.{h,cpp}
    services/tray_icon_service.{h,cpp}
    ui/widget_window.{h,cpp}
    ui/model_row_widget.{h,cpp}
    ui/widget_style.qss
  tests/
    data/
    services/
    ui/
```

Use CMake targets rather than qmake. Require the Qt `Core`, `Gui`, `Widgets`, `Sql`, and `Test` modules. Keep the application MIT-licensed; document Qt's LGPL obligations and deploy Qt shared libraries/plugins with the required license material unless a commercial Qt license is selected.

## Behavior Mapping

| Current implementation | Qt implementation | Parity requirement |
|---|---|---|
| `App.xaml.cs` startup/lifecycle | `AppController` owned by `main.cpp` | Parse arguments, load settings, validate DB, create services, wait for first result/error before showing the widget, and explicitly quit only from Exit. |
| WPF `MainWindow` | `WidgetWindow : QWidget` | Frameless, translucent, fixed-size-to-content, taskbar-hidden widget with the same dark card, colors, spacing, list max height, scroll bar, hover states, and context menu. |
| MVVM view models | `WidgetWindow` state plus small `ModelRowWidget` objects | Retain model-row objects across polls and update/reorder them in place rather than recreating the list every refresh. Qt signal/slot state is sufficient; adding a generic MVVM layer would be ceremony with no benefit. |
| `DispatcherTimer` poller/highlight/save timers | `QTimer` plus a background worker | Preserve the initial immediate poll, configurable interval, no-overlap guard, two-second highlight duration, and 500 ms settings-save debounce. |
| `Microsoft.Data.Sqlite` | `QSqlDatabase` using the deployed `QSQLITE` driver | Open read-only, set a 2-second busy timeout, preserve the exact JSON query and fork-deduplication grouping. Run database work off the GUI thread. |
| `NotifyIcon` | `QSystemTrayIcon` and `QMenu` | Tray icon toggles show/hide; closing/hiding does not terminate; both the widget and tray expose Exit. |
| Windows Forms monitor API | `QGuiApplication::screenAt()` / `QScreen::availableGeometry()` | Use the active screen's usable working area for centering, clamping, and quadrant anchoring. |

## Implementation Plan

### 1. Establish the native Qt build

- Add `qt/CMakeLists.txt` with `CMAKE_AUTOMOC`, `CMAKE_AUTORCC`, a C++20 requirement, explicit Qt 6 component discovery, warning settings, and an `OpenCodeCostMeter` executable target.
- Add a vcpkg manifest or another locked dependency mechanism for CI/developer reproducibility. Qt itself should be installed by the CI setup or Qt's official action; do not vendor the Qt SDK into the repository.
- Add `qt/resources/app.qrc` for the SVG icon, both font families, style sheet, and license resources. Use the SVG at runtime; generate platform-native package icons from the existing canonical `src/Assets/icon.svg` during packaging.
- Keep `model-display-names.txt` as an editable external file beside the executable/bundle resources, matching the current override behavior. Install a default copy during packaging.

### 2. Port pure data and configuration code first

- Port `ModelBreakdown`, `DayUsageSnapshot`, and `Settings` to simple C++ value types. Preserve JSON names and defaults exactly: `x`, `y`, `alwaysOnTop`, `pollIntervalSeconds`, `opacity`, and `isExpanded`.
- Implement `DbLocator` with `QStandardPaths::homePath()` and `QDir` joins. Preserve `--db-path <path>` as the explicit override and the existing default `~/.local/share/opencode/opencode.db`; report the resolved attempted path in an error dialog.
- Implement the local-date start boundary from `QDateTime::currentDateTime().date().startOfDay()` and convert to Unix milliseconds. Keep attribution based on `$.time.completed`.
- Implement `SettingsStore` with `QJsonDocument` and UTF-8. Store new settings in `QStandardPaths::AppConfigLocation/OpenCodeCostMeter.settings.json`, which is writable on installed macOS and Linux applications. On first run, import the existing adjacent-to-executable Windows settings file if present, then write future changes only to the per-user configuration directory. This is required migration support for existing persisted settings.
- Port `ModelDisplayNameRules` exactly: UTF-8 external file, comments/blank lines ignored, `prefix|find=replace;...` syntax, wildcard prefix, title-cased hyphen replacement, and a cached result per raw model ID.

### 3. Port the SQLite repository without changing accounting semantics

- Implement `MessageTableRepository::getToday(qint64 startOfTodayMs)` with a connection that is owned and used by the worker thread only. Configure the SQLite driver connection as read-only and set `QSQLITE_BUSY_TIMEOUT=2000` (or issue `PRAGMA busy_timeout = 2000` after opening if that driver option is unavailable in the selected Qt build).
- Preserve the existing single statement verbatim in meaning: assistant-role only, non-null completion time, completion after local midnight, inner grouping by created/completed timestamps to eliminate fork clones, then per-provider/model aggregation sorted by cost descending and model ascending.
- Bind the start timestamp; never concatenate paths or values into SQL. Sum costs in C++ exactly as the current repository does and retain empty strings for NULL provider/model values.
- Add an integration test SQLite fixture covering multiple models, calls crossing midnight, NULL fields, no usage, a locked/busy database, and forked messages that must not be double-counted. Assert totals, ordering, day key, and read-only behavior.
- Verify the chosen Qt SQLite plugin has JSON functions in every packaged build. If it does not, ship an SQLite build with JSON support and use its C API for this repository instead of silently changing the query or data semantics.

### 4. Port polling and presentation state

- Implement `UsagePoller` with a GUI-thread `QTimer` and a `QThread` worker object (or `QtConcurrent` with an equivalent atomic in-flight guard). The timer schedules a query only when no query is pending; worker completion returns to the GUI thread through queued signals.
- Start the first query immediately, then start regular polling. Changing the interval must stop/reconfigure/restart the timer without starting a second query.
- Preserve non-blocking behavior: retain last successful values while a query runs, surface query errors without crashing, and continue future polls after errors.
- In `WidgetWindow`, retain the current formatted values (`$0.00` total, `$0.000` row) using the `en_US` locale. Only highlight a value when its formatted display value changes after the first successful result. Clear all highlights after two seconds.
- Maintain a provider/model key and an ordered row-widget map. Apply only insert, move, update, and remove operations needed for each snapshot. Suppress rows below `$0.0005`, but retain their previous value in the comparison map exactly as the WPF implementation does.

### 5. Recreate the widget UI and interaction model

- Build `WidgetWindow` from `QWidget`, `QFrame`, `QVBoxLayout`, labels, a scroll area, and reusable `ModelRowWidget` rows. Use a Qt style sheet and the existing color constants: background `#20201F`, stroke `#4A4A4A`, primary text `#E5E2E1`, secondary text `#ADABAA`, accent `#61DBB4`, hover `#2A2A2A`, and error `#E0B341`.
- Apply Cascadia Mono to cost and model labels and Inter to menus/configuration controls. Match the existing 24/14/12-point hierarchy, card padding changes when expanded, 320-pixel model-list cap, character ellipsis, and rounded border.
- Start hidden. Show only on the first successful snapshot or first error, then clamp to the current screen's `availableGeometry()`.
- Make a left click toggle the breakdown and treat a drag above `QApplication::startDragDistance()` as a window drag. Consume the click after a drag so it does not also expand/collapse. Right click opens the widget context menu at the pointer.
- Build the context menu with `QMenu`, checkable Always on top action, `QWidgetAction` sliders for 5-60 second poll interval and 5-100% opacity in 5-unit steps, horizontal/vertical center actions, Hide, and Exit. Debounce all slider writes by 500 ms while applying visual changes immediately. Retain `A`, `H`, `V`, and `T` keyboard shortcuts.
- Use `Qt::WindowStaysOnTopHint`, `Qt::FramelessWindowHint`, and `Qt::Tool`. Set translucent background only where the active platform supports it; the opaque dark card remains the reliable baseline.

### 6. Preserve monitor-aware position and resize behavior

- Store logical Qt screen coordinates in the unchanged `x`/`y` settings fields. On launch, restore them if valid; otherwise center on the selected/current screen.
- On a user drag, clear the cached resize anchor and clamp the widget to `QScreen::availableGeometry()` after release. Re-evaluate the target screen after moving across monitors.
- When expansion/collapse changes the window's size, calculate the size delta in `resizeEvent()`. Determine the active screen center and cache the current horizontal/vertical span and quadrant flags for exactly one subsequent resize. Offset the window left/up when it is on the right/bottom side, or by half the delta when it spans the screen center. Clear the cached anchor after that paired resize, a drag, or manual centering.
- Add focused tests for the pure anchor-flag calculation: four quadrants, each center-spanning axis, paired expand/collapse returning to the original position, and cache reset on drag/center. Keep platform window movement itself covered by manual smoke tests because compositor policy is outside Qt's control.

### 7. Implement cross-platform application lifecycle and tray behavior

- In `main.cpp`, parse `--db-path` and `--help`. Print help to standard output on all platforms; remove Windows console attachment P/Invokes entirely. Use `QMessageBox::critical` for a missing database and exit nonzero.
- Set `QApplication::setQuitOnLastWindowClosed(false)`. Intercept the widget close event and hide it unless Exit was explicitly requested.
- Implement `TrayIconService` with `QSystemTrayIcon`, the embedded SVG icon, an Exit action, and activation handling that toggles widget visibility. Use `Trigger` in addition to `DoubleClick`: macOS does not provide the same double-click behavior once a tray menu is attached.
- Check `QSystemTrayIcon::isSystemTrayAvailable()`. If absent, keep the visible widget usable and add Exit to its context menu; do not make the application unreachable by hiding it to a nonexistent tray.
- Persist position during explicit shutdown and dispose the poller/worker before destruction. Treat persistence failures as non-fatal, as today.

### 8. Define honest platform support boundaries

- **Windows 10/11:** support the complete feature set, including native tray, frameless translucent window, always-on-top, multi-monitor positioning, and packaged `.exe`/MSIX releases.
- **macOS 13+:** support the complete functional feature set through the menu bar and a standard Qt window. Package as a signed, notarized `.app` inside a DMG; use a generated `.icns` icon. Test on both Intel and Apple Silicon builds or distribute a universal binary.
- **Linux:** support X11 as the full desktop-widget target. Ship AppImage and a `.desktop`/PNG icon integration package; additionally document required distribution libraries and test a current Ubuntu LTS and Fedora release.
- **Wayland:** treat absolute position, programmatic movement after resize, translucency, always-on-top, and tray activation as compositor-controlled best effort. Qt cannot guarantee WPF-like widget placement under Wayland. Detect Wayland at runtime, keep the widget functional, avoid repeated move attempts when denied, and show this limitation in the README. Do not claim identical desktop-widget behavior on GNOME Wayland.
- Verify tray behavior on KDE Plasma, GNOME (with a supported StatusNotifier extension where required), and one X11 environment. The application must remain usable if no tray implementation is present.

### 9. Package, license, and automate releases

- Add package configuration for Windows (`windeployqt`, ICO, installer/MSIX), macOS (`macdeployqt`, ICNS, bundle signing/notarization variables), and Linux (`linuxdeployqt` or CMake deployment, AppImage, desktop file, SVG/PNG icons). Keep signing secrets exclusively in CI secret storage.
- Package Qt platform and SQL driver plugins so a clean machine can launch the application and open SQLite. Add a release smoke check that starts the built artifact with `--help` and a test database path.
- Include `LICENSE`, `THIRD-PARTY-NOTICES.md`, the Cascadia and Inter OFL license texts, and the applicable Qt LGPL notices in all source and binary distributions. Confirm the Qt licensing/distribution method with the project owner before publishing binaries.
- Add CI builds for Windows, macOS, and Linux, with CMake configure/build/test steps, dependency caching, and artifact upload. Run unit/data tests on all three operating systems; run platform packaging jobs only for tagged releases.
- Update `README.md` with the Qt prerequisite/build instructions, supported package formats, platform database path/default, command-line options, font attribution, tray/Wayland caveats, and reset/configuration-file location.

### 10. Cut over only after parity validation

- Create a feature-parity checklist from the current application and execute it on Windows, macOS, Linux X11, and Linux Wayland. Include first-run behavior, missing DB, custom DB path, mid-poll database lock, no usage, multiple model changes, highlight expiration, row ordering, settings persistence, drag, edge clamp, resize anchoring, opacity, topmost, hide/restore, and explicit exit.
- Compare WPF and Qt output against the same database fixture, including total and per-model formatting, to ensure accounting has not changed.
- Keep the WPF source during one release cycle as a reference and fallback. Remove `src/OpenCodeCostMeter.csproj`, WPF sources, and Windows-only build instructions only after all release artifacts have passed validation and the Qt release is accepted.

## Files To Retain, Add, And Retire

- Retain as canonical input/assets: `src/Assets/icon.svg`, `src/model-display-names.txt`, `LICENSE`, and the existing SQLite query behavior.
- Add the Qt tree and legal/package/CI files described above.
- Update `README.md`, `.gitignore`, and the repository-level build instructions to make the Qt application primary after cutover.
- Retire only after the acceptance phase: `src/OpenCodeCostMeter.csproj`, WPF XAML/code-behind, Windows Forms tray implementation, and the `.slnx` solution. Do not delete them during initial implementation.

## Acceptance Criteria

- [ ] The same database produces the same today's total and provider/model breakdown as the WPF implementation, including fork deduplication and local-midnight attribution.
- [ ] The application never writes to `opencode.db`, remains responsive during a locked/slow database, and does not overlap polls.
- [ ] Windows, macOS, and Linux X11 ship and run native packages with an embedded icon, bundled Cascadia Mono and Inter fonts, and complete required license notices.
- [ ] Segoe UI is not redistributed; Cascadia Mono and Inter load from application resources on a clean machine.
- [ ] The widget preserves its current visual hierarchy, highlight behavior, context menu, settings schema/migration, tray-first close behavior, drag behavior, and quadrant-based paired resize anchoring.
- [ ] Multiple-monitor placement and clamping use each platform's usable screen area rather than full display geometry.
- [ ] Linux Wayland limitations are tested, handled without crashes or runaway repositioning, and documented without overstating support.
- [ ] Automated data/service tests pass on Windows, macOS, and Linux; manual platform smoke checks and release-package launch checks are recorded before cutover.

## Open Decision Before Implementation

Confirm the intended Qt license and release channel before implementation begins: **LGPL Qt with dynamically deployed Qt libraries/plugins** is the practical default for this MIT project, but it imposes redistribution/notice obligations. A commercial Qt license is only necessary if those obligations are unacceptable.
