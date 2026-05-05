---
id: 35
type: task
title: Add memctl delete <id> command to remove note from vault and index
status: In Progress
priority: medium
tags:
- feature
- cli
- vault
- index
created: 2026-05-06
updated: 2026-05-06
---

## Description

`memctl` has no `delete` command. Removing a note requires manually deleting the `.md` file from disk then running `memctl ingest` to prune the stale index entry — a two-step workaround. This is error-prone: if the user forgets to re-ingest, the deleted note persists in search, `context-inject`, and `memctl list` output, polluting prompt context with dead data.

This task adds `memctl delete <id>` which atomically removes both the vault `.md` file and its SQLite index entry in a single operation. The command targets notes by their 16-char hex ID (as returned by `memctl list` / `memctl add`). Pattern follows `GetOperator` / `WeightOperator`: `IVaultReader + INoteIndex`, sync `MemctlOutcome` return, no new DTO layer needed.

## Implementation

### Step 0 — Prereq fail-fast
- Verify `dotnet test` passes baseline: `dotnet test tests/memctl.Tests/` → 61/61 pass

### Step 1 — Confirm `INoteIndex.Delete` + `GetById` exist
Both are already declared in `src/memctl/CoreAbstractions/Ports/INoteIndex.cs`:
- `void Delete(string noteId)` — line 9
- `Note? GetById(string noteId)` — line 10

No interface changes needed.

### Step 2 — Create `DeleteOperator`
- **File CREATE:** `src/memctl/Operators/DeleteOperator.cs`

```csharp
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class DeleteOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string noteId)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var note = index.GetById(noteId);
        if (note is null)
            return MemctlOutcome.Fail("delete", $"Note not found: {noteId}");

        var absPath = Path.Combine(vaultPath, note.FilePath);
        if (File.Exists(absPath))
            File.Delete(absPath);

        index.Delete(noteId);
        return MemctlOutcome.Ok("delete", $"Deleted: {note.FilePath}", note);
    }
}
```

### Step 3 — Register CLI command in `Program.cs`
- **File MODIFY:** `src/memctl/Bootstrap/Program.cs` — add after the `get` command block (~line 193):

```csharp
// --- delete ---
var deleteIdArg = new Argument<string>("id", "Note ID (16-char hex)");
var deleteCmd   = new Command("delete", "Delete a note from vault and index by ID");
deleteCmd.AddArgument(deleteIdArg);
deleteCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op      = new DeleteOperator(vaultReader, noteIndex);
    var outcome = op.Execute(vault, ctx.ParseResult.GetValueForArgument(deleteIdArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 2;
});
root.AddCommand(deleteCmd);
```

### Step 4 — Unit tests
- **File CREATE:** `tests/memctl.Tests/Operators/DeleteOperatorTests.cs`

  1. `Delete_RemovesFileAndIndexEntry`: add note via `AddOperator` → ingest → `DeleteOperator.Execute` → `index.GetById(id)` returns null + `File.Exists(absPath)` = false
  2. `Delete_ReturnsFailWhenIdNotFound`: `DeleteOperator.Execute` with unknown ID → `outcome.Success = false`, `outcome.Action = "delete"`
  3. `Delete_SucceedsWhenFileAlreadyMissing`: add note → ingest → `File.Delete(absPath)` manually → `DeleteOperator.Execute(id)` → `outcome.Success = true`, index entry gone

### Step 5 — Smoke test
```powershell
# Add a note
$addOut  = memctl add "smoke delete test" --title "smoke-delete-test" | ConvertFrom-Json
$noteId  = $addOut.data.id
$noteFile = (Join-Path (memctl status | ConvertFrom-Json).data.search_path $addOut.data.file)

# Verify present
$present = memctl get $noteId | ConvertFrom-Json
if (-not $present.success) { Write-Error "FAIL: note not found after add"; exit 1 }
Write-Host "PASS: note present"

# Delete
$del = memctl delete $noteId | ConvertFrom-Json
if (-not $del.success) { Write-Error "FAIL: delete returned error: $($del.message)"; exit 1 }
Write-Host "PASS: delete succeeded"

# Verify gone from index
$gone = memctl get $noteId | ConvertFrom-Json
if ($gone.success) { Write-Error "FAIL: note still in index after delete"; exit 1 }
Write-Host "PASS: note gone from index"

# Verify file gone from disk
if (Test-Path $noteFile) { Write-Error "FAIL: file still on disk"; exit 1 }
Write-Host "PASS: file deleted from disk"
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `memctl delete <id>` removes the `.md` file from vault | `Test-Path <vault>/<file>` → `False` after delete |
| FR-2 | `memctl delete <id>` removes index entry | `memctl get <id>` → `success: false` after delete |
| FR-3 | `memctl list` excludes deleted note immediately | `memctl list \| ConvertFrom-Json` → no entry with matching id |
| FR-4 | Deleting non-existent ID returns error JSON with `success: false`, exit code 2 | `memctl delete 0000000000000000; echo $LASTEXITCODE` → `2`; JSON `success: false, action: delete` |
| FR-5 | Delete tolerates missing file (file already deleted manually) | `Remove-Item <file>; memctl delete <id>` → `success: true`, `memctl get <id>` → `success: false` |
| FR-6 | Delete output contains note data (id, file, title) | JSON `data.id`, `data.file`, `data.title` populated (NoteDto via existing mapper) |
| NFR-1 | No regression in existing tests | `dotnet test tests/memctl.Tests/` → 61 baseline + ≥3 new = 64+ pass, 0 failures |
| NFR-2 | `memctl --help` lists `delete` command | `memctl --help` stdout contains `delete` |

## Out of Scope
- `memctl delete --title <title>` lookup by title — ID only for v1; use `memctl search` then delete by ID
- Batch delete / `--all` flag
- Confirmation prompt / `--force` — CLI is non-interactive by design
- Undo / recycle bin

## Dependencies
- Blocked by: none
- Soft depend: #34 (ingest prune) — merged; `INoteIndex.Delete` confirmed present at `INoteIndex.cs:9`

## Risk

| Risk | Mitigation |
|------|-----------|
| File deleted but `index.Delete` throws (partial state) | File gone + index stale → task 34 prune cleans on next `memctl ingest`. Acceptable trade-off; index-first would risk file leak instead |
| `Path.Combine(vaultPath, note.FilePath)` when `note.FilePath` is absolute | `Note.FilePath` is always relative (documented `// relative to vault` in `Note.cs:6`); `Path.Combine` safe |
| User passes file path instead of ID | `GetById` returns null → `MemctlOutcome.Fail` with clear message; no crash |

## Effort
~1.5h:
- `DeleteOperator.cs`: 0.25h
- `Program.cs` CLI registration: 0.25h
- Unit tests (3 cases): 0.75h
- Smoke test + verify: 0.25h

## User Actions Required
- none

## Comments

**2026-05-06 05:48 user:** Phase 0: baseline 61/61 — branch feature/35-delete-command
