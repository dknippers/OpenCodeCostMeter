# OpenCode Cost Meter

A borderless desktop widget that shows your [opencode](https://opencode.ai) LLM spend for today in real time, broken down by model. The Qt implementation supports Windows, macOS, and Linux X11. Wayland compositors may restrict widget positioning, always-on-top behavior, and tray interaction.

## Usage

Requires Qt 6.8+ with the Widgets, SQL, and Test modules, CMake 3.25+, and a C++20 compiler.

On Windows, open a **Visual Studio Developer PowerShell** and run:

```powershell
cmake -S qt -B build/qt -DCMAKE_PREFIX_PATH="C:\Qt\6.11.1\msvc2022_64"
cmake --build build/qt
.\build\qt\OpenCodeCostMeter.exe
```

The widget stays hidden until its first successful database poll or error. On other platforms, replace the Qt path with the installed Qt kit:

```sh
cmake -S qt -B build/qt -DCMAKE_PREFIX_PATH=/path/to/Qt/6.x/<kit>
cmake --build build/qt
./build/qt/OpenCodeCostMeter
```

By default the widget reads from `~/.local/share/opencode/opencode.db`. To use a different database path:

```powershell
.\build\qt\OpenCodeCostMeter.exe --db-path "C:\path\to\opencode.db"
```

Settings are stored as `OpenCodeCostMeter.settings.json` in the platform application-config directory. The model display-name rules file remains next to the executable.

The application embeds Cascadia Mono (SIL OFL 1.1) and Inter (SIL OFL 1.1). Segoe UI is not bundled because Microsoft does not grant general font redistribution rights with Windows. See `THIRD-PARTY-NOTICES.md`.
