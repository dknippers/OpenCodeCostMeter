# OpenCode Cost Meter

A borderless Windows 11 desktop widget that shows your [opencode](https://opencode.ai) LLM spend for today in real time, broken down by model.

## Usage

Requires .NET 10.

```powershell
git clone https://github.com/user/OpenCodeCostMeter.git
cd OpenCodeCostMeter
dotnet build src\OpenCodeCostMeter.csproj
.\src\bin\Debug\net10.0-windows\OpenCodeCostMeter.exe
```

By default the widget reads from `%USERPROFILE%\.local\share\opencode\opencode.db`. To use a different database path:

```powershell
.\OpenCodeCostMeter.exe --db-path "C:\path\to\opencode.db"
```
