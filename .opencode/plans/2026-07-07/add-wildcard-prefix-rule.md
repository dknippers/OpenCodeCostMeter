# Plan: Add wildcard `*` prefix support to model display name rules

## Task

Add support for a `*` wildcard prefix in `model-display-names.txt` that applies replacements to all models, not just prefix-matched ones. Example: `*|V=v` would replace "V" with "v" in every model name.

## Files to modify

- `src/Services/ModelDisplayNameRules.cs` — `Format()` method needs two-pass logic
- `src/model-display-names.txt` — add a comment documenting the `*` syntax

## Changes

### `ModelDisplayNameRules.cs` — `Format()` method

Current single loop with `break` on first prefix match:
```csharp
foreach (var rule in Rules.Value)
{
    if (modelId.StartsWith(rule.Prefix, ...))
    {
        // apply replacements
        break;
    }
}
```

Change to two passes:
1. **Prefix-specific rules** — find first matching prefix, apply its replacements, break
2. **Wildcard rules** — apply all rules with `*` prefix (no break)

No changes to `LoadRules()` — `*` is already a valid prefix string. The `PrefixRule` record already stores it fine.

### `model-display-names.txt`

Add a comment line documenting the `*` syntax.

## Verification

- Build: `dotnet build src\OpenCodeCostMeter.csproj`
- Confirm config file still copies to output
