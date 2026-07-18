# OpenCode Cost Meter

A borderless desktop widget that shows your [opencode](https://opencode.ai) LLM spend for today in real time, broken down by model. Built with Avalonia UI on .NET 10 — runs on Windows 11, structured so a macOS target is a trivial follow-up.

## Usage

Requires .NET 10.

```powershell
git clone https://github.com/user/OpenCodeCostMeter.git
cd OpenCodeCostMeter
dotnet build OpenCodeCostMeter.slnx
.\src\OpenCodeCostMeter\bin\Debug\net10.0\OpenCodeCostMeter.exe
```

By default the widget reads from `%USERPROFILE%\.local\share\opencode\opencode.db`. To use a different database path:

```powershell
.\OpenCodeCostMeter.exe --db-path "C:\path\to\opencode.db"
```
