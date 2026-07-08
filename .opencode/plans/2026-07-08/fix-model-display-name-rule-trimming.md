## Task

Fix a bug in the model display name rule parser where the find/replace pairs portion of each rule line is trimmed, causing a leading space in the first find text to be silently stripped. This breaks rules like `*| Mini=mini` in `src/model-display-names.txt`, which should only match the literal substring " Mini" (space + Mini) and not "Mini" inside "Minimax".

The user's expectation: parse the rule exactly as written. The rule file documents that find/replace operations are applied as literal substring matches on the already-defaulted string, so the parser must preserve the find text verbatim other than splitting on `;`.

## Root cause

`src/Services/ModelDisplayNameRules.cs:53` called `.Trim()` on `pairsPart` after slicing off the prefix. For the line `*| Mini=mini; Nano=nano` this dropped the space after `|`, turning the first pair into `Mini=mini`. With that, `result.Replace("Mini", "mini", StringComparison.Ordinal)` matches "Mini" inside "Minimax" and rewrites it to "minimax".

## Fix

Remove `.Trim()` from the `pairsPart` assignment. `pairsPart.Split(';', StringSplitOptions.RemoveEmptyEntries)` then yields the pairs as written, with any leading spaces preserved as part of the find text.

## Verification

- `minimax` -> default -> `Minimax` -> no ` Mini` substring -> stays `Minimax` (was wrongly `minimax`).
- `minimax-M2` -> default -> `Minimax M2` -> no ` Mini` substring -> stays `Minimax M2`.
- `some-mini` -> default -> `Some Mini` -> matches ` Mini` -> `Somemini` (intended behavior for a space-bounded Mini).
- `*|V=v` (no leading space) is unaffected.

## Files

- `src/Services/ModelDisplayNameRules.cs` - drop `.Trim()` on `pairsPart`.
- `.opencode/plans/2026-07-08/fix-model-display-name-rule-trimming.md` - this plan.
