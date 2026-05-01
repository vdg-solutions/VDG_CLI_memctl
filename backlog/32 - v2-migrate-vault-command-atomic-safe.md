---
id: 32
type: task
title: 'V2 migration — memctl migrate-vault reads V1 notes, copies to V2, leaves V1 untouched'
status: Archived
archived_reason: 'Dropped per anh directive — V2.1 hard cutover, no legacy V1 back-compat needed. Dev V1 vault relocated to .archived-v1-vault/ (gitignored).'
priority: high
parent: 30
tags:
  - vault
  - layout-v2
  - migration
  - cli
  - safety
created: 2026-05-01
updated: 2026-05-01
---

## Description

Second child of epic #30. Build on #31's V2.1 resolver + InitVaultStructure. Add `memctl migrate-vault --vault <V1-path>` CLI command that creates V2.1 layout by **reading each .md note from V1 vault and writing to V2 destination** — V1 source untouched, no atomic-rename complexity, naturally cross-volume safe, reversible by definition.

Default destination: `<V1-path>/.memctl-v2/` (new sibling under V1 anchor). After migrate succeeds + user verifies notes intact in V2, user manually:
1. Deletes V1 sibling artifacts (`.obsidian/`, `.memctl/`, root `*.md` notes, `chats/`, `claude-memory/`).
2. Renames `.memctl-v2/` → `.memctl/` (becomes the new vault root).

### V1 → V2.1 mapping rules

| V1 source | V2.1 destination | Reason |
|-----------|------------------|--------|
| `<V1>/chats/*.md` | `<V2>/chats/*.md` | session captures preserved |
| `<V1>/claude-memory/**/*.md` | `<V2>/claude-memory/**/*.md` | hierarchical memory preserved with relative path |
| `<V1>/*.md` (root) | `<V2>/*.md` (root) | ad-hoc user notes preserved at root |
| `<V1>/.memctl/index.db` | SKIP — rebuild via `memctl ingest` post-migrate | runtime, derivative |
| `<V1>/.memctl/models/` | SKIP — re-download via embedding engine if needed | binary, large |
| `<V1>/.obsidian/*.json` | SKIP — fresh V2 `.obsidian/` created by Init | Obsidian config; workspace state lost (acceptable) |
| (V2 new dirs) `tasks/`, `patterns/`, `lessons/`, `decisions/`, `attachments/` | created empty by InitVaultStructure | semantic dirs introduced by V2.1 |

Migration scope: copy `.md` files preserving relative paths. Skip runtime + Obsidian config. User loses Obsidian workspace UI state (tabs, last-open file) — acceptable trade-off vs V1 corruption risk.

Non-destructive design: V1 vault is **READ-ONLY** during migration. Failure mid-migration leaves V1 fully intact + partial V2 → user can `rm -rf <V1>/.memctl-v2/ && memctl migrate-vault ...` retry. No corruption window.

## Implementation

### Step 0 — Prereq fail-fast
- Verify `#31` Done: `bl show 31 | grep -q '^status: Done'` || exit "Blocked by #31 (V2 resolver + Init refactor)"
- Verify build clean: `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q` || exit "Fix build first"
- Verify legacy detection works (from #31): create V1 fixture, `memctl status --vault <fixture> --json | python -c "import sys, json; d=json.load(sys.stdin); assert d['data'].get('legacy_v1') is True"` || exit "#31 legacy detection broken — fix first"

### Step 1 — MigrateVaultOperator: read-and-copy

- **File CREATE:** `src/memctl/Operators/MigrateVaultOperator.cs`:

```csharp
namespace Memctl.Operators;

using Memctl.Boundary;
using Memctl.CoreAbstractions.Ports;

public sealed class MigrateVaultOperator
{
    private readonly IVaultReader _vault;

    public MigrateVaultOperator(IVaultReader vault) => _vault = vault;

    public MemctlOutcome Execute(string v1Path, string? destPath, bool dryRun)
    {
        if (!Directory.Exists(v1Path))
            return MemctlOutcome.Fail("migrate-vault", $"V1 vault path does not exist: {v1Path}");

        // Detect V1 layout: sibling .obsidian/ + .memctl/
        var hasV1Obsidian = Directory.Exists(Path.Combine(v1Path, ".obsidian"));
        var hasV1Memctl   = Directory.Exists(Path.Combine(v1Path, ".memctl"));
        var v2Existing    = Path.Combine(v1Path, ".memctl");
        var v2HasObsidian = Directory.Exists(Path.Combine(v2Existing, ".obsidian"));

        if (v2HasObsidian && !hasV1Obsidian)
            return MemctlOutcome.Ok("migrate-vault", "Vault already at V2 layout — no action needed",
                new MigrationResult(v1Path, "already-v2", 0, []));

        if (!hasV1Obsidian || !hasV1Memctl)
            return MemctlOutcome.Fail("migrate-vault",
                $"Vault at {v1Path} not in V1 layout (missing .obsidian/ or .memctl/ siblings)");

        var v2Dest = destPath ?? Path.Combine(v1Path, ".memctl-v2");

        // Collect .md files from V1 vault, excluding system dirs
        var v1Notes = Directory.EnumerateFiles(v1Path, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".obsidian" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + ".memctl" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + ".memctl-v2" + Path.DirectorySeparatorChar))
            .ToList();

        if (dryRun)
            return MemctlOutcome.Ok("migrate-vault",
                $"Dry-run: would copy {v1Notes.Count} notes from V1 ({v1Path}) to V2 ({v2Dest})",
                new MigrationResult(v1Path, "dry-run", v1Notes.Count, v1Notes));

        // Initialize V2 structure (creates .obsidian/, chats/, models/, claude-memory/)
        _vault.InitVaultStructure(v2Dest);

        // Read each .md and write to V2 with relative path preserved
        var copied = new List<string>();
        foreach (var src in v1Notes)
        {
            var rel = Path.GetRelativePath(v1Path, src);
            var dst = Path.Combine(v2Dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            // Skip if dest exists (V2 init may have created README.md, MEMORY.md placeholder)
            if (File.Exists(dst))
            {
                copied.Add($"SKIP (dest exists): {rel}");
                continue;
            }

            var content = File.ReadAllBytes(src);
            File.WriteAllBytes(dst, content);
            copied.Add(rel);
        }

        var msg = $"Migrated {v1Notes.Count} notes from V1 to V2 at {v2Dest}. " +
                  "V1 untouched. Run 'memctl ingest --vault " + v2Dest + "' to rebuild index. " +
                  "After verifying notes in V2, manually delete V1 artifacts: " +
                  ".obsidian/, .memctl/, sibling root .md, chats/.";

        return MemctlOutcome.Ok("migrate-vault", msg,
            new MigrationResult(v1Path, "migrated-readonly", copied.Count, copied));
    }
}

public sealed record MigrationResult(
    string                VaultPath,
    string                Status,
    int                   NoteCount,
    IReadOnlyList<string> Notes);
```

### Step 2 — Boundary DTO + JsonContext registration

- **File MODIFY:** `src/memctl/Boundary/MemctlResult.cs` — add `MigrateVaultResultDto` for typed wire response:

```csharp
public sealed record MigrateVaultResultDto(
    [property: JsonPropertyName("vault_path")]   string VaultPath,
    [property: JsonPropertyName("status")]       string Status,
    [property: JsonPropertyName("note_count")]   int NoteCount,
    [property: JsonPropertyName("notes")]        IReadOnlyList<string> Notes
);
```

- **File MODIFY:** `src/memctl/Boundary/MemctlJsonContext.cs` — register `MigrateVaultResultDto` for AOT.
- **File MODIFY:** `src/memctl/Operators/Mapping/MemctlResultMapper.cs` — case dispatch `MigrationResult → MigrateVaultResultDto`.

### Step 3 — Bootstrap CLI wiring

- **File MODIFY:** `src/memctl/Bootstrap/Program.cs`:

```csharp
var migrateCmd = new Command("migrate-vault", "Migrate V1 vault layout (siblings) to V2 (.memctl/ root container) by copying .md notes");
var dryRunOpt  = new Option<bool>("--dry-run", "Report planned copies without executing");
var destOpt    = new Option<string?>("--dest", "Destination V2 vault path (default: <vault>/.memctl-v2/)");
migrateCmd.AddOption(dryRunOpt);
migrateCmd.AddOption(destOpt);
migrateCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVaultExplicit(g, ctx) is not { } vault) return;
    var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
    var dest   = ctx.ParseResult.GetValueForOption(destOpt);
    var op     = new MigrateVaultOperator(vaultReader);
    ResultPrinter.Print(op.Execute(vault, dest, dryRun));
});
root.AddCommand(migrateCmd);
```

### Step 4 — Tests

- **File CREATE:** `tests/memctl.Tests/Vault/MigrationTests.cs` — 6 tests (xUnit + IDisposable tmp cleanup):

```csharp
[Fact] public void V1_to_V2_copies_all_md_notes_v1_untouched()
{
    // Setup V1: <root>/.obsidian/app.json + <root>/.memctl/index.db + <root>/note.md + <root>/chats/2026-01-01.md
    // Run migrate (default dest = <root>/.memctl-v2)
    // Assert: <root>/.memctl-v2/.obsidian/ exists (init), <root>/.memctl-v2/note.md content matches V1, <root>/.memctl-v2/chats/2026-01-01.md exists
    // Assert: V1 still intact — <root>/.obsidian/app.json + <root>/.memctl/index.db + <root>/note.md all unchanged
}

[Fact] public void DryRun_lists_planned_notes_no_writes()
{
    // Setup V1, run migrate --dry-run, assert NoteCount > 0, no .memctl-v2/ directory created
}

[Fact] public void Custom_dest_path_respected()
{
    // Setup V1, migrate --dest /tmp/somewhere/v2, assert notes copied to /tmp/somewhere/v2 not <root>/.memctl-v2
}

[Fact] public void V2_layout_already_returns_already_v2_status()
{
    // Setup pre-migrated V2 (<root>/.memctl/.obsidian/ exists), no sibling .obsidian/
    // Run migrate, assert status = "already-v2", NoteCount = 0
}

[Fact] public void Missing_path_returns_fail()
{
    // Run migrate on /nonexistent, assert MemctlOutcome.Success = false
}

[Fact] public void Re_migrate_skips_existing_dest_files()
{
    // Setup V1 + half-migrated V2 (some files in .memctl-v2/ already)
    // Run migrate again, assert second copy logs "SKIP (dest exists)" for pre-existing, copies remaining
}

[Fact] public void Dest_inside_v1_internal_rejected()
{
    // Setup V1, run migrate --dest <V1>/.memctl/anywhere
    // Assert MemctlOutcome.Success = false, message mentions destination cannot be inside V1 .memctl/
    // Same assertion: --dest <V1>/.memctl (the V1 internal subdir itself)
}
```

### Step 5 — Real-fs smoke test

```bash
TMP=$(mktemp -d)
mkdir -p "$TMP/legacy/.obsidian" "$TMP/legacy/.memctl" "$TMP/legacy/chats" "$TMP/legacy/claude-memory"
echo "{}" > "$TMP/legacy/.obsidian/app.json"
sqlite3 "$TMP/legacy/.memctl/index.db" "CREATE TABLE notes(id INTEGER);"
echo "# old chat" > "$TMP/legacy/chats/2026-04-01.md"
echo "# old note" > "$TMP/legacy/personal-note.md"
echo "# memory index" > "$TMP/legacy/claude-memory/MEMORY.md"

# Dry-run
memctl migrate-vault --vault "$TMP/legacy" --dry-run | grep -q "would copy 3 notes" && echo "dry-run count OK"
test ! -d "$TMP/legacy/.memctl-v2" && echo "dry-run no mutation OK"

# Real migrate (V1 untouched)
memctl migrate-vault --vault "$TMP/legacy"
test -f "$TMP/legacy/.memctl-v2/.obsidian/app.json" && echo "V2 obsidian init OK"
test -d "$TMP/legacy/.memctl-v2/.obsidian/memctl" && echo "V2.1 runtime dir created OK"
test -d "$TMP/legacy/.memctl-v2/tasks" && echo "tasks/ dir created OK"
test -d "$TMP/legacy/.memctl-v2/patterns" && echo "patterns/ dir created OK"
test -d "$TMP/legacy/.memctl-v2/decisions" && echo "decisions/ dir created OK"
test -d "$TMP/legacy/.memctl-v2/attachments" && echo "attachments/ dir created OK"
test -f "$TMP/legacy/.memctl-v2/personal-note.md" && echo "root note copied OK"
test -f "$TMP/legacy/.memctl-v2/chats/2026-04-01.md" && echo "chats copied OK"
test -f "$TMP/legacy/.memctl-v2/claude-memory/MEMORY.md" && echo "memory index copied OK"

# V1 INTACT
test -f "$TMP/legacy/.obsidian/app.json" && echo "V1 obsidian intact OK"
test -f "$TMP/legacy/.memctl/index.db" && echo "V1 index intact OK"
test -f "$TMP/legacy/personal-note.md" && echo "V1 root note intact OK"

# Idempotent
memctl migrate-vault --vault "$TMP/legacy" 2>&1 | grep -q "SKIP\|already" && echo "re-migrate handles existing OK"

# Post-migrate ingest workflow
memctl ingest --vault "$TMP/legacy/.memctl-v2"
memctl list  --vault "$TMP/legacy/.memctl-v2" | grep -q "personal-note" && echo "indexed in V2 OK"

# User cleanup (manual step)
rm -rf "$TMP/legacy/.obsidian" "$TMP/legacy/.memctl" "$TMP/legacy/personal-note.md" "$TMP/legacy/chats" "$TMP/legacy/claude-memory"
mv "$TMP/legacy/.memctl-v2" "$TMP/legacy/.memctl"

# Final state — clean V2 vault
memctl status --vault "$TMP/legacy/.memctl" --json | grep -q "walk-up v2" && echo "final V2 layout resolves OK"

rm -rf "$TMP"
```

### Step 6 — Build + test verify

```bash
dotnet build src/memctl/memctl.csproj -c Release --nologo -v q       # 0 warning, 0 error
dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo          # 51 from #31 + 6 new = 57/57
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `memctl migrate-vault --vault <V1>` reads all `.md` from V1 (excl `.obsidian/`, `.memctl/`, `.memctl-v2/`) and writes to `<V1>/.memctl-v2/` with relative path preserved | `dotnet test --filter MigrationTests.V1_to_V2_copies_all_md_notes_v1_untouched` exit 0 |
| FR-2 | V1 vault completely untouched post-migration (READ-ONLY semantics) | (same test) asserts source files unchanged via timestamp + content check |
| FR-3 | V2 destination initialized with full structure (`.obsidian/` + `chats/` + `models/` + `claude-memory/`) | (same test) asserts each subdir exists post-migrate |
| FR-4 | `--dry-run` lists planned notes, mutates 0 files | `dotnet test --filter MigrationTests.DryRun_lists_planned_notes_no_writes` exit 0 |
| FR-5 | `--dest <path>` overrides default `<V1>/.memctl-v2/` destination | `dotnet test --filter MigrationTests.Custom_dest_path_respected` exit 0 |
| FR-6 | Already-V2 vault returns `already-v2` status, NoteCount = 0 | `dotnet test --filter MigrationTests.V2_layout_already_returns_already_v2_status` exit 0 |
| FR-7 | Missing source path returns failure outcome (no crash) | `dotnet test --filter MigrationTests.Missing_path_returns_fail` exit 0 |
| FR-8 | Re-running migrate skips destination files that already exist (no overwrite) | `dotnet test --filter MigrationTests.Re_migrate_skips_existing_dest_files` exit 0 |
| FR-9 | Smoke: full lifecycle V1 → migrate → V2 ingest → list → manual cleanup → V2 resolves cleanly | smoke script exit 0 with all `OK` lines |
| NFR-1 | Build clean: 0 warning, 0 error | `dotnet build -c Release --nologo -v q 2>&1 \| grep -cE "warning\|error"` returns 0 |
| NFR-2 | Tests: 51 from #31 + 6 new = 57 total | `dotnet test --nologo` "Passed: 57" |
| NFR-3 | NO destructive operations on V1 — migrate writes only to dest path | `grep -E "File\.Delete\|Directory\.Delete\|Move" src/memctl/Operators/MigrateVaultOperator.cs` returns 0 hits |
| NFR-4 | Migration is opt-in — no auto-trigger from other commands | `grep -rn "MigrateVaultOperator\|migrate-vault" src/memctl/ \| grep -v "Bootstrap/Program.cs\|Operators/MigrateVaultOperator.cs"` returns 0 hits |

## Out of Scope

- Auto-cleanup of V1 artifacts post-migration. User does manually after verifying V2.
- Auto-renaming `.memctl-v2/` → `.memctl/` post-cleanup. Manual step.
- Index.db preservation. Rebuilt via `memctl ingest` after migrate.
- Obsidian workspace state (`.obsidian/workspace.json` tabs). Acceptable loss; new fresh `.obsidian/` created.
- `--cleanup-v1` flag (destructive auto-cleanup). Future task if user demand emerges.
- V2 → V1 reverse. One-way.
- Multi-vault batch migration. Future task.

## Dependencies

- **Blocked by #31** (V2 resolver + Init refactor + InitVaultStructure detect direct vs parent path).
- Touches: `src/memctl/Operators/MigrateVaultOperator.cs` (new), `src/memctl/Bootstrap/Program.cs`, `src/memctl/Boundary/MemctlResult.cs`, `src/memctl/Boundary/MemctlJsonContext.cs`, `src/memctl/Operators/Mapping/MemctlResultMapper.cs`.

## Risk

| Risk | Mitigation |
|------|-----------|
| User runs migrate, doesn't realize V1 is preserved → confused why disk usage doubled | Output message explicitly says "V1 untouched. After verifying, manually delete: ...". Doc + CHANGELOG emphasize 2-step migrate-then-cleanup pattern. |
| User skips post-migrate cleanup → V2 vault inside V1 vault → walk-up confusion (resolves V1 first because legacy detect catches sibling layout) | Walk-up legacy detection (#31) returns V1 strategy; user sees `legacy v1` warning even after partial migration. Hint message: "complete cleanup or run `memctl migrate-vault` again to verify." |
| Notes with frontmatter weight or special metadata: copy preserves bytes? | Yes — `File.ReadAllBytes/WriteAllBytes` preserves binary content exactly. Frontmatter intact, weight preserved, links intact. |
| File mode (executable bit on Linux/macOS) lost during ReadAllBytes/WriteAllBytes | `.md` files don't have executable bit — irrelevant. Confirmed by Step 5 smoke. |
| Dest path conflicts with V1 internal layout (e.g., user passes `--dest <V1>/.memctl/` which is V1's internal index dir) | Detect: if dest equals or is descendant of `<V1>/.memctl/` (the V1 internal subdir), emit error. Acceptable: dest must be NEW directory or `<V1>/.memctl-v2/` (distinct sibling). |
| Cross-volume copy still uses File.Copy semantics (no atomic rename concern) | Already cross-volume safe by design — no rename, just read+write. |
| Dev vault test corrupted | Em runs `--dry-run` first, shows output to anh. Anh approves before real migrate. Em backs up `.memctl/.backup-pre-v2/` even though V1 is technically untouched — paranoid safety. |

## Effort

~3-4h:
- 1h: MigrateVaultOperator.cs (read-and-copy logic + dest path resolution)
- 0.5h: Boundary DTO + JsonContext + Mapper wiring
- 0.5h: Bootstrap CLI command (migrate-vault + --dry-run + --dest options)
- 1h: 6 unit tests with V1 fixture setup + IDisposable cleanup
- 0.5h: real-fs smoke script (full lifecycle V1 → V2 → cleanup → V2 resolves)
- 0.5h: build + run all 57 tests, fix any regression

## User Actions Required

- [USER-ACTION-REQUIRED] BEFORE em runs migrate on dev vault `E:\repos\CLIs\VDG_CLI_memctl\.memctl/`: anh approve em chạy `--dry-run` first, em show output, anh confirm "ok migrate".
- [USER-ACTION-REQUIRED] AFTER migrate succeeds + anh verify V2 has all notes: anh manually `rm -rf .obsidian .memctl chats claude-memory <root *.md notes>` and `mv .memctl-v2 .memctl`. Em không tự động làm cleanup destructive bước này.

## Notes

- **Read-and-copy approach (anh request)** vs atomic-rename: simpler, no FS-edge-case risk, naturally cross-volume safe, V1 readonly during migration. Trade-off: temporary 2× disk usage until manual cleanup.
- Index.db rebuilt by user via `memctl ingest --vault <V2>` post-migrate. Documented in operator output message.
- Obsidian workspace UI state lost — user re-opens vault at new path, configures tabs/links once.
- Dest default `.memctl-v2/` chosen so user doesn't accidentally overwrite V1 internal `.memctl/` subdir. Final cleanup involves rename `.memctl-v2/` → `.memctl/` after V1 artifacts removed.
