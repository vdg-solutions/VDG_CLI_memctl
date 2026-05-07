# Design #43 — Auto-trigger distill via context-inject threshold counter

## Overview

Piggybacks on existing hook flow: capture → increment, context-inject → check + emit, distill → reset. No new infrastructure. Zero change to any interface, no new dependency.

## New File: `src/memctl/Operators/DistillStateStore.cs`

Static internal utility class. Same pattern as `EventLog` — `Operators/` layer, best-effort, never crash callers.

```csharp
namespace Memctl.Operators;

internal static class DistillStateStore
{
    private const int DefaultThreshold = 5;

    internal static void Increment(string vaultPath) { ... }
    internal static void Reset(string vaultPath) { ... }
    internal static bool ShouldRecommend(string vaultPath) { ... }
    internal static (int count, int threshold, DateTime? lastAt) GetState(string vaultPath) { ... }
}
```

**State file:** `{vaultPath}/.obsidian/memctl/distill-state.json`

```json
{
  "conversations_since_distill": 4,
  "last_distill_at": "2026-05-07T10:00:00Z",
  "threshold": 5
}
```

**Key implementation rules:**
- All 4 methods: wrap body in `try/catch { }` with comment `/* best-effort; never crash callers */`
- `GetState`: deserialize JSON, return defaults `(0, DefaultThreshold, null)` on any error (missing file, parse error, corrupt JSON)
- `Increment`: GetState → +1 → write (atomic: write to temp path, `File.Move(temp, dest, overwrite: true)`)
- `Reset`: write `conversations_since_distill=0`, `last_distill_at=DateTime.UtcNow`, preserve existing threshold
- `ShouldRecommend`: `GetState` → `count >= threshold`
- `Directory.CreateDirectory` before write (`.obsidian/memctl/` may not exist)

**Atomic write pattern (all write methods):**
```csharp
var dest = StateFilePath(vaultPath);
var temp = dest + ".tmp";
Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
File.WriteAllText(temp, json, Encoding.UTF8);
File.Move(temp, dest, overwrite: true);
```

## CaptureOperator.cs — `CreateNote` only

After `EventLog.Record(...)` on line 72, add:
```csharp
DistillStateStore.Increment(vaultPath);
```

`AppendNote`: no change — appending to the same conversation is not a new conversation.

## ContextInjectOperator.cs — `Execute` method

After the `if (results.Count == 0) return null;` block and `FormatContext(results)` call, apply recommendation:

```csharp
var context = results.Count == 0 ? null : FormatContext(results);

var rec = BuildRecommendation(vaultPath);  // returns null if not needed
if (rec is null) return context;           // no change needed

return context is null ? rec : context + rec;
```

`BuildRecommendation`:
```csharp
private static string? BuildRecommendation(string vaultPath)
{
    if (!DistillStateStore.ShouldRecommend(vaultPath)) return null;
    var (count, threshold, lastAt) = DistillStateStore.GetState(vaultPath);
    var lastStr = lastAt.HasValue ? lastAt.Value.ToString("O") : "never";
    return $"\n## Distill Recommendation\n{count}/{threshold} conversations since last distill (last: {lastStr}).\nRun `memctl distill` to consolidate L1 → L2 memory.\n";
}
```

**AC-5 compliance:** when vault is empty (`results.Count == 0`), return `rec` instead of null if threshold is met.

## DistillOperator.cs — `ExecuteAsync`

After the success path (non-dry-run, after the `pending` loop completes), before building `msg`:

```csharp
if (!dryRun && pending.Count > 0)
    DistillStateStore.Reset(vaultPath);
```

Condition: `!dryRun && pending.Count > 0` — reset only when actual work was done. Dry-run and "no conversations to distill" both skip reset.

## Program.cs — `memctl config set distill-threshold <n>`

Add a `config` command with a `set` subcommand. Narrow scope — only handles `distill-threshold`.

```
memctl config set distill-threshold <n>
```

Handler reads distill-state.json, updates only the `threshold` key, writes back. Validates `n > 0`.

```csharp
var configCmd  = new Command("config", "Manage memctl configuration");
var configSetCmd   = new Command("set", "Set a config value");
var cfgKeyArg  = new Argument<string>("key", "Config key (e.g. distill-threshold)");
var cfgValArg  = new Argument<string>("value", "Value to set");
configSetCmd.AddArgument(cfgKeyArg);
configSetCmd.AddArgument(cfgValArg);
configSetCmd.SetHandler(ctx => { ... });
configCmd.AddCommand(configSetCmd);
root.AddCommand(configCmd);
```

## Test File: `tests/memctl.Tests/Operators/DistillStateStoreTests.cs`

Tests covering all 10 ACs:

| Test | AC |
|------|----|
| `Increment_CreatesFileOnFirstCall` | AC-1 |
| `Increment_CreateNote_IncrementsCounter` | AC-2 |
| `ContextInject_AppendRecommendation_WhenThresholdMet` | AC-3 |
| `ContextInject_NoRecommendation_BelowThreshold` | AC-4 |
| `ContextInject_EmptyVault_ReturnsRecommendation` | AC-5 |
| `Distill_ResetsCounter_AfterSuccess` | AC-6 |
| `Distill_DryRun_DoesNotReset` | AC-7 |
| `ConfigSet_DistillThreshold_ChangesThreshold` | AC-8 |
| `Increment_IoError_DoesNotCrash` | AC-9 |
| `CorruptJson_FallsBackToDefaults` | AC-10 |

## Files Changed

| File | Change |
|------|--------|
| `src/memctl/Operators/DistillStateStore.cs` | NEW |
| `src/memctl/Operators/CaptureOperator.cs` | +1 line after EventLog.Record in CreateNote |
| `src/memctl/Operators/ContextInjectOperator.cs` | Refactor Execute + add BuildRecommendation |
| `src/memctl/Operators/DistillOperator.cs` | +3 lines after pending loop |
| `src/memctl/Bootstrap/Program.cs` | Add `config set` command |
| `tests/memctl.Tests/Operators/DistillStateStoreTests.cs` | NEW — 10 tests |
