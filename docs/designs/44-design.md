# Design #44 — Contradiction resolution in memctl distill

## Overview

Opt-in `--resolve-contradictions` flag on `memctl distill`. When set, after each extraction LLM checks for contradictions against existing same-type vault notes. Resolutions: `KeepNew | KeepExisting | Merge`. Default distill behavior is unchanged.

## New File: `src/memctl/CoreAbstractions/Entities/ContradictionResult.cs`

```csharp
namespace Memctl.CoreAbstractions.Entities;

public enum ContradictionResolution { KeepNew, KeepExisting, Merge }

public sealed record ContradictionResult(
    bool                    Contradicts,
    string?                 ExistingId,
    ContradictionResolution Resolution,
    string?                 MergedContent,
    string                  Rationale);
```

Lives in `CoreAbstractions/Entities/` — same layer as `DistillResult`.

## ILlmClient — Add `CheckContradictionAsync`

```csharp
Task<ContradictionResult> CheckContradictionAsync(
    DistilledNote           newNote,
    IReadOnlyList<Note>     candidates,
    CancellationToken       ct = default);
```

**Breaking change:** all implementations and test stubs must implement this method.

## OpenAiLlmClient — Implement `CheckContradictionAsync`

LLM prompt: compare `newNote` content against each candidate, return JSON:
```json
{
  "contradicts": true,
  "existing_id": "abc123def456",
  "resolution": "keep_new",
  "merged_content": null,
  "rationale": "new note is more accurate and recent"
}
```

Parse `resolution` string → enum:
```csharp
var res = root.TryGetProperty("resolution", out var rv) ? rv.GetString() : null;
ContradictionResolution resolution = res switch {
    "keep_existing" => ContradictionResolution.KeepExisting,
    "merge"         => ContradictionResolution.Merge,
    _               => ContradictionResolution.KeepNew,  // default: keep_new
};
```

On parse error → return `new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, "")`.

## DistillOperator — Wire `--resolve-contradictions`

Add parameter `bool resolveContradictions` to `ExecuteAsync`.

After extracting each note (inside the `foreach (var ex in result.Extractions)` loop), before writing:

```csharp
if (resolveContradictions)
{
    var candidates = FindSameTypeCandidates(vaultPath, ex.Type);
    if (candidates.Count > 0)
    {
        ContradictionResult cr;
        try
        {
            cr = await llmClient.CheckContradictionAsync(ex, candidates, ct);
        }
        catch (Exception e)
        {
            EventLog.Record(vaultPath, "operator_run", "error", "distill", $"CheckContradiction failed: {e.Message}");
            cr = new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, "");
        }

        if (cr.Contradicts)
        {
            // Validate ExistingId
            var existingNote = cr.ExistingId is not null
                ? candidates.FirstOrDefault(c => c.Id == cr.ExistingId)
                : null;

            if (existingNote is not null)
            {
                Console.Error.WriteLine($"[distill] contradiction resolved: {cr.Resolution} — {cr.Rationale}");
                if (cr.Resolution == ContradictionResolution.KeepExisting)
                    continue;  // skip writing new note
                ArchiveNote(vaultPath, existingNote);  // KeepNew or Merge
            }
            // Invalid ExistingId → fall through (treat as no contradiction)

            if (cr.Resolution == ContradictionResolution.Merge && cr.MergedContent is not null)
                ex = ex with { Content = cr.MergedContent };
        }
    }
}
```

`FindSameTypeCandidates`: use `index.SearchBm25(ex.Title, 5, folderPrefix: MapFolder(ex.Type) + "/")`.

`ArchiveNote`:
```csharp
private void ArchiveNote(string vaultPath, Note note)
{
    var absPath = Path.Combine(vaultPath, note.FilePath);
    if (!File.Exists(absPath)) return;
    var archived = note with {
        Archived = true,
        Weight   = 0f,
        Tags     = [.. note.Tags.Append("superseded").Distinct()],
        Modified = DateTime.UtcNow,
    };
    vaultReader.WriteNote(archived, vaultPath, note.FilePath);
    index.Upsert(archived);
}
```

**Note:** `DistilledNote` is a record — to mutate `ex` with merged content, use local variable reassignment (`var ex` requires the outer variable to be reassignable — use a local copy inside the loop).

## Program.cs — `--resolve-contradictions` flag

```csharp
var distillResolveOpt = new Option<bool>("--resolve-contradictions", "Check and resolve contradictions with existing L2 notes");
distillCmd.AddOption(distillResolveOpt);
```

Pass to `ExecuteAsync`:
```csharp
var resolveContradictions = ctx.ParseResult.GetValueForOption(distillResolveOpt);
var outcome = await op.ExecuteAsync(vault, convId, since, dryRun, resolveContradictions, ctx.GetCancellationToken());
```

## DistillOperatorTests.cs — Update Stubs

`CountingLlmClient` and `FixedResultLlmClient` must implement `CheckContradictionAsync`:
```csharp
public Task<ContradictionResult> CheckContradictionAsync(
    DistilledNote newNote, IReadOnlyList<Note> candidates, CancellationToken ct = default)
    => Task.FromResult(new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, ""));
```

## Test File: `tests/memctl.Tests/Operators/DistillContradictionTests.cs` (NEW)

Tests for all 10 ACs, using dedicated LLM stubs:
- `NoContradictsLlmClient` — returns `Contradicts=false`
- `ContradictingLlmClient(resolution, existingId?, mergedContent?)` — returns specified resolution
- `ThrowingContradictLlmClient` — throws on `CheckContradictionAsync`

## Files Changed

| File | Change |
|------|--------|
| `src/memctl/CoreAbstractions/Entities/ContradictionResult.cs` | NEW |
| `src/memctl/CoreAbstractions/Ports/ILlmClient.cs` | +CheckContradictionAsync |
| `src/memctl/Implementations/Llm/OpenAiLlmClient.cs` | implement CheckContradictionAsync |
| `src/memctl/Operators/DistillOperator.cs` | wire --resolve-contradictions |
| `src/memctl/Bootstrap/Program.cs` | add --resolve-contradictions flag |
| `tests/memctl.Tests/Operators/DistillOperatorTests.cs` | update CountingLlmClient + FixedResultLlmClient stubs |
| `tests/memctl.Tests/Operators/DistillContradictionTests.cs` | NEW — 10 tests |
