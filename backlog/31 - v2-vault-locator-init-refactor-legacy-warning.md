---
id: 31
type: task
title: 'V2 foundation — VaultLocator + Init refactor + path updates + loud legacy warning'
status: Todo
priority: high
parent: 30
tags:
  - vault
  - layout-v2
  - resolver
  - foundation
created: 2026-05-01
updated: 2026-05-01
---

## Description

Foundation child of epic #30. Refactor `VaultLocator.Discover()` to walk-up tìm `.memctl/` (containing `.obsidian/`) làm vault root container. Refactor `InitVaultStructure` cho **V2.1 layout** với runtime nested inside `.obsidian/memctl/` (auto-hidden by Obsidian) + semantic top-level subdirs matching SDLC pipeline writers.

### V2.1 layout

```
<project>/.memctl/                          ← vault root (Obsidian opens here)
├── .obsidian/                              ← Obsidian config (auto-hidden)
│   ├── app.json, appearance.json, daily-notes.json, ...
│   └── memctl/                             ← memctl runtime (nested, hidden by Obsidian)
│       ├── index.db                        (SQLite — embeddings + BM25)
│       ├── models/embeddinggemma-300m/     (~295 MB)
│       └── hook.log                        (diagnostic)
├── tasks/                                  ← /sdlc per-phase artifacts (task-{id}-{phase}.md)
├── patterns/                               ← /retro: error patterns + hit_count mutate
├── lessons/                                ← /qc-dream: cross-task wisdom (dedupe + merge)
├── decisions/                              ← /design: ADR-style (adr-{NNNN}-{slug}.md)
├── chats/                                  ← Stop hook: daily-rollup (YYYY-MM-DD.md)
├── attachments/                            ← images, binaries (flat with date prefix)
├── claude-memory/MEMORY.md                 ← top-level index (bot reads first)
└── README.md                               ← vault explainer (init-generated)
```

### Writer ownership

| Subdir | Writer | Mutate |
|--------|--------|--------|
| `tasks/` | /sdlc orchestrator + each phase skill | append-only |
| `patterns/` | /retro post-merge | mutate `hit_count` |
| `lessons/` | /qc-dream | dedupe + merge |
| `decisions/` | /design phase | append-only (ADR numbering) |
| `chats/` | Stop hook (`memctl capture`) | append into daily file |
| `attachments/` | tool/hook output | append-only |
| `claude-memory/MEMORY.md` | /qc-dream consolidation | rewrite (compress) |

Crucially: legacy V1 detection (sibling `.obsidian/` + `.memctl/`) returns strategy `"legacy v1 — run memctl migrate-vault"`. StatusOperator + SessionStart hook MUST surface user-visible `::warning::` line — NO silent degrade. Migration itself ships in #32.

NO migration command yet — that's #32. Existing V1 vaults still resolve (in legacy mode) and reads work, just with a warning displayed.

## Implementation

### Step 0 — Prereq fail-fast
- Verify `#28` Done: `bl show 28 | grep -q '^status: Done'` || exit "Blocked by #28"
- Verify build clean baseline: `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q` || exit "Fix build first"
- Verify tests baseline: `dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo` || exit "Fix tests first"
- Verify VaultLocator.cs file: `test -f src/memctl/Implementations/Config/VaultLocator.cs` || exit "VaultLocator missing"

### Step 1 — VaultLocator V2 walk-up + legacy detection

- **File MODIFY:** `src/memctl/Implementations/Config/VaultLocator.cs`
- Walk-up tìm `.memctl/` chứa `.obsidian/` (V2). Return path of `.memctl/` itself.
- Legacy V1: dir chứa CẢ `.obsidian/` AND `.memctl/` siblings → return strategy `legacy v1 — run memctl migrate-vault`, vault path = dir (so reads still work pre-migration).

```csharp
namespace Memctl.Implementations.Config;

public sealed record VaultDiscovery(
    string?                Vault,
    string                 SearchPath,
    string                 Strategy,
    IReadOnlyList<string>  CheckedPaths);

public static class VaultLocator
{
    public static string? FindVault(string? explicitPath)
        => Discover(explicitPath, Directory.GetCurrentDirectory()).Vault;

    public static string? FindVaultFrom(string startDir)
        => Discover(null, startDir).Vault;

    public static VaultDiscovery Discover(string? explicitPath, string searchPath)
    {
        if (explicitPath is not null)
            return new VaultDiscovery(explicitPath, explicitPath, "explicit", [explicitPath]);

        var checkedPaths = new List<string>();
        var dir = searchPath;
        while (true)
        {
            checkedPaths.Add(dir);

            var v2Vault = Path.Combine(dir, ".memctl");
            if (Directory.Exists(v2Vault) && Directory.Exists(Path.Combine(v2Vault, ".obsidian")))
                return new VaultDiscovery(Path.GetFullPath(v2Vault), searchPath, "walk-up v2 (.memctl/)", checkedPaths);

            // Legacy V1: .obsidian/ + .memctl/ as siblings (pre-V2 layout)
            if (Directory.Exists(Path.Combine(dir, ".obsidian")) && Directory.Exists(Path.Combine(dir, ".memctl")))
                return new VaultDiscovery(Path.GetFullPath(dir), searchPath, "legacy v1 — run memctl migrate-vault", checkedPaths);

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return new VaultDiscovery(null, searchPath, "walk-up v2 + legacy", checkedPaths);
    }
}
```

### Step 2 — InitVaultStructure refactor (V2.1 layout)

- **File MODIFY:** `src/memctl/Implementations/Vault/ObsidianVaultReader.cs`
- `InitVaultStructure(vaultPath)`: detect if caller passes parent dir vs `.memctl/` direct path. Create vault root inside `<vaultPath>/.memctl/` UNLESS path already ends with `.memctl`.
- Inside vault root: `.obsidian/` (Obsidian config) → `.obsidian/memctl/` (memctl runtime nested, hidden) + 7 top-level semantic subdirs (`tasks/`, `patterns/`, `lessons/`, `decisions/`, `chats/`, `attachments/`, `claude-memory/`).

```csharp
public void InitVaultStructure(string vaultPath)
{
    var trimmed = vaultPath.TrimEnd(Path.DirectorySeparatorChar);
    var isDirect = Path.GetFileName(trimmed) == ".memctl";
    var vaultRoot = isDirect ? trimmed : Path.Combine(trimmed, ".memctl");

    Directory.CreateDirectory(vaultRoot);

    // Obsidian config + nested memctl runtime (hidden by Obsidian auto-hide of .obsidian/)
    Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian", "memctl"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian", "memctl", "models"));

    // Semantic top-level dirs (writer ownership matrix in description)
    Directory.CreateDirectory(Path.Combine(vaultRoot, "tasks"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "patterns"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "lessons"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "decisions"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "chats"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "attachments"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "claude-memory"));

    WriteIfAbsent(Path.Combine(vaultRoot, ".obsidian", "app.json"),        "{}");
    WriteIfAbsent(Path.Combine(vaultRoot, ".obsidian", "appearance.json"), "{}");
    WriteIfAbsent(Path.Combine(vaultRoot, ".obsidian", "community-plugins.json"),
        """["dataview","calendar"]""");
    WriteIfAbsent(Path.Combine(vaultRoot, ".obsidian", "core-plugins.json"),
        """{"daily-notes":true,"templates":true,"backlink":true,"outline":true,"word-count":true}""");
    WriteIfAbsent(Path.Combine(vaultRoot, ".obsidian", "daily-notes.json"),
        """{"folder":"chats","format":"YYYY-MM-DD"}""");

    WriteIfAbsent(Path.Combine(vaultRoot, "claude-memory", "MEMORY.md"),
        "# Memory index\n\n");

    WriteIfAbsent(Path.Combine(vaultRoot, "README.md"),
        "# memctl vault\n\nObsidian: open this folder as vault. Memctl handles indexing automatically.\n\n## Subdirs\n\n- `tasks/` — /sdlc per-phase artifacts\n- `patterns/` — /retro error patterns\n- `lessons/` — /qc-dream wisdom\n- `decisions/` — /design ADRs\n- `chats/` — Stop hook daily rollups\n- `attachments/` — images/binaries\n- `claude-memory/MEMORY.md` — top-level index\n");
}
```

### Step 3 — Path computations: runtime nested in `.obsidian/memctl/`

Vault path NOW IS `.memctl/`. Runtime files (index, models, log) move INTO `.obsidian/memctl/` subdir → auto-hidden by Obsidian.

- **File MODIFY:** `src/memctl/Operators/IngestOperator.cs:82` — `Path.Combine(vaultPath, ".obsidian", "memctl", "index.db")` (was `.memctl/index.db`)
- **File MODIFY:** `src/memctl/Operators/HookLog.cs:10,16` — `Path.Combine(vaultPath, ".obsidian", "memctl", "hook.log")`
- **File MODIFY:** `src/memctl/Implementations/Config/MemctlConfig.cs:11,15` — config + models paths point inside `.obsidian/memctl/`
- **File MODIFY:** `src/memctl/Implementations/Embedding/GemmaEmbeddingEngine.cs:158` — `Path.Combine(vaultPath, ".obsidian", "memctl", "models", "embeddinggemma-300m")`
- **File MODIFY:** `src/memctl/Operators/StatusOperator.cs:10` — model path V2; legacy compat: try V2 `.obsidian/memctl/models/` first, fallback V1 `.memctl/models/` for legacy detection
- **File MODIFY:** `src/memctl/Implementations/Vault/ObsidianVaultReader.cs:18` — `EnumerateMarkdownFiles` exclude `.obsidian/` only (runtime nested inside, automatically excluded)
- **File MODIFY:** `src/memctl/Operators/GrepOperator.cs:23` — same exclude update

### Step 4 — Loud legacy warning (StatusOperator + Bootstrap)

Legacy V1 detection MUST surface user-visible warning. Hooks that exit 0 silent are documented graceful degrade BUT must emit stderr line + GitHub Actions warning annotation:

- **File MODIFY:** `src/memctl/Operators/StatusOperator.cs` — when `discovery.Strategy.StartsWith("legacy v1")`:
  - Add `data.legacy_v1: true`
  - Add `data.migration_hint: "Run: memctl migrate-vault --vault <path> (lands in v1.3.0+)"`
  - Always log to stderr: `Console.Error.WriteLine($"::warning::Vault legacy v1 layout detected at {vaultPath}. Run 'memctl migrate-vault' (available in v1.3.0+).")`
- **File MODIFY:** `src/memctl/Bootstrap/Program.cs` — every command that resolves vault checks discovery strategy; if legacy → emit warning once per process (use `static bool _warned`).

### Step 5 — Tests

- **File CREATE:** `tests/memctl.Tests/Vault/VaultLocatorV2Tests.cs` — 5 tests (xUnit + IDisposable):
  - `WalkUp_finds_v2_when_memctl_contains_obsidian` — `<root>/.memctl/.obsidian/` exists → strategy `walk-up v2 (.memctl/)`, vault = `<root>/.memctl/`
  - `WalkUp_finds_v2_from_subdir` — cwd `<root>/src/x.cs` → walk-up resolves `<root>/.memctl/`
  - `Legacy_v1_returns_migration_hint` — `<root>/.obsidian/` + `<root>/.memctl/` siblings → strategy `legacy v1 — run memctl migrate-vault`
  - `No_vault_returns_null` — empty tree → null
  - `Explicit_overrides_walk_up` — `--vault` flag wins

- **File CREATE:** `tests/memctl.Tests/Vault/InitV2Tests.cs` — 3 tests:
  - `Init_with_parent_anchor_creates_memctl_subdir` — `InitVaultStructure(<path>)` → `<path>/.memctl/.obsidian/` exists, `<path>/.memctl/index.db` flat (test by checking parent has only `.memctl/` not `.obsidian/` directly)
  - `Init_with_direct_memctl_path_skips_nesting` — `InitVaultStructure(<path>/.memctl)` → `<path>/.memctl/.obsidian/` (NOT `<path>/.memctl/.memctl/.obsidian/`)
  - `Reinit_existing_v2_idempotent` — second call no overwrite of existing JSON files

- **File CREATE:** `tests/memctl.Tests/Vault/LegacyWarningTests.cs` — 1 test:
  - `Legacy_v1_emits_stderr_warning` — capture stderr while running StatusOperator on V1 fixture, assert `::warning::` substring + "migrate-vault" substring present

### Step 6 — Build + test verify

```bash
dotnet build src/memctl/memctl.csproj -c Release --nologo -v q       # 0 warning, 0 error
dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo          # 42 baseline + 9 new = 51/51

# Smoke: V2.1 init clean
TMP=$(mktemp -d)
mkdir -p "$TMP/project/src"
echo "# README" > "$TMP/project/README.md"
memctl init --vault "$TMP/project"
test -d "$TMP/project/.memctl/.obsidian/memctl" && echo "V2.1 runtime nested OK"
test -d "$TMP/project/.memctl/tasks" && echo "tasks/ dir OK"
test -d "$TMP/project/.memctl/patterns" && echo "patterns/ dir OK"
test -d "$TMP/project/.memctl/lessons" && echo "lessons/ dir OK"
test -d "$TMP/project/.memctl/decisions" && echo "decisions/ dir OK"
test -d "$TMP/project/.memctl/chats" && echo "chats/ dir OK"
test -d "$TMP/project/.memctl/attachments" && echo "attachments/ dir OK"
test -d "$TMP/project/.memctl/claude-memory" && echo "claude-memory/ dir OK"
test ! -d "$TMP/project/.obsidian" && echo "no V1 pollution at parent OK"
test -f "$TMP/project/.memctl/.obsidian/app.json" && echo "Obsidian config inside .memctl/ OK"

# Smoke: legacy v1 warning
mkdir -p "$TMP/legacy/.obsidian" "$TMP/legacy/.memctl"
echo "{}" > "$TMP/legacy/.obsidian/app.json"
memctl status --vault "$TMP/legacy" 2>&1 | grep -q "legacy v1" && echo "legacy detection OK"
memctl status --vault "$TMP/legacy" 2>&1 | grep -q "::warning::" && echo "loud warning OK"

rm -rf "$TMP"
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `<path>/.memctl/.obsidian/` resolved as V2 vault, returned path = `<path>/.memctl/` | `dotnet test --filter VaultLocatorV2Tests.WalkUp_finds_v2_when_memctl_contains_obsidian` exit 0 |
| FR-2 | Walk-up V2 from project subdir resolves to `.memctl/` ancestor | `dotnet test --filter VaultLocatorV2Tests.WalkUp_finds_v2_from_subdir` exit 0 |
| FR-3 | Legacy V1 (sibling `.obsidian/` + `.memctl/`) returns strategy `legacy v1 — run memctl migrate-vault` | `dotnet test --filter VaultLocatorV2Tests.Legacy_v1_returns_migration_hint` exit 0 |
| FR-4 | `InitVaultStructure(<parent>)` creates V2.1 layout: `.obsidian/memctl/` runtime + 7 top-level subdirs (tasks, patterns, lessons, decisions, chats, attachments, claude-memory) | smoke runs all `test -d` lines pass |
| FR-5 | `InitVaultStructure(<path>/.memctl)` direct invocation does NOT create `<path>/.memctl/.memctl/` | `dotnet test --filter InitV2Tests.Init_with_direct_memctl_path_skips_nesting` exit 0 |
| FR-6 | Index.db at `<vault>/.obsidian/memctl/index.db` (nested, hidden by Obsidian) | smoke: `memctl init --vault $TMP/p; memctl ingest --vault $TMP/p/.memctl; test -f $TMP/p/.memctl/.obsidian/memctl/index.db` exit 0 |
| FR-7 | EnumerateMarkdownFiles excludes `.obsidian/` only (runtime nested inside, auto-excluded) | smoke: file at `<vault>/.memctl/foo.md` indexed by ingest, `<vault>/.memctl/.obsidian/memctl/anything.md` NOT indexed |
| FR-8 | Legacy V1 detection emits stderr `::warning::` annotation | `dotnet test --filter LegacyWarningTests.Legacy_v1_emits_stderr_warning` exit 0 |
| FR-9 | StatusOperator JSON output includes `data.legacy_v1: true` + `data.migration_hint` when legacy | smoke: `memctl status --vault <V1-fixture> --json \| python -c "import sys, json; d=json.load(sys.stdin); assert d['data'].get('legacy_v1') is True"` exit 0 |
| NFR-1 | Build clean: 0 warning, 0 error | `dotnet build -c Release --nologo -v q 2>&1 \| grep -cE "warning\|error"` returns 0 |
| NFR-2 | Existing 42 tests still pass + 9 new = 51 total | `dotnet test --nologo` "Passed: 51" in output |
| NFR-3 | NO migration command yet (lands in #32) | `grep -c "migrate-vault" src/memctl/Bootstrap/Program.cs` returns 0 |

## Out of Scope

- Migration command itself — #32.
- Skill / plugin README rewrite — #33.
- Version bump to 1.3.0 — #33 (after migration ships, atomic with docs).
- Public memctl-releases sync — #33.
- StatusOperator `data.discovery.strategy` field exposure — already in VaultDiscovery; just propagate via existing JSON serialization.

## Dependencies

- Blocked by `#28` (Done) — version lockstep workflow ready when #33 bumps.
- Soft depends `#27` (Done) — plugin scaffold exists; #33 will rewrite README.
- Touches: VaultLocator.cs, ObsidianVaultReader.cs, IngestOperator.cs, HookLog.cs, MemctlConfig.cs, GemmaEmbeddingEngine.cs, StatusOperator.cs, GrepOperator.cs, Bootstrap/Program.cs.

## Risk

| Risk | Mitigation |
|------|-----------|
| Path computation refactor breaks existing tests (model path, hook log, etc.) | Run full test suite after each file change; fix any regressions before next edit |
| Legacy V1 detection false-positive on dirs that happen to have both `.obsidian/` and `.memctl/` for unrelated reasons | Convention is clear: only memctl-managed vaults have both. False-positive cost = unnecessary migration warning, not data loss. |
| Loud `::warning::` annotation noise in non-CI shells | Annotation prefix only triggers GitHub Actions parser; in regular shells it's a plain stderr line, easy to ignore. Acceptable. |
| Existing V1 vault on dev box stops indexing post-#31 (model path resolves wrong) | StatusOperator legacy fallback: try V2 model path, then `.memctl/models/` fallback. Reads don't fail until migration. |
| Test fixture cross-platform path issues | Use `Path.Combine` exclusively; existing FrontmatterParserTests pattern proven OK |

## Effort

~4-5h:
- 0.5h: VaultLocator.cs walk-up V2 + legacy detection
- 1h: InitVaultStructure refactor + parent/direct path detection
- 1h: Path computation updates across 6 operators (hook log, ingest, config, embedding, status, vault reader, grep)
- 0.5h: Loud legacy warning (StatusOperator + bootstrap)
- 1h: 9 tests (VaultLocatorV2 + InitV2 + LegacyWarning)
- 0.5h: build + run tests, fix regressions
- 0.5h: smoke run V2 init + legacy detection

## User Actions Required

- (none — no migration yet, no breaking deploy)

## Notes

- After #31 ships, dev vault at `E:\repos\CLIs\VDG_CLI_memctl\.memctl/` will report `legacy v1` strategy on `memctl status`. Reads + writes still work via legacy mode. Migration in #32.
- DO NOT bump version in this child — version stays 1.2.0 until #33 atomic ship with docs.
- StatusOperator legacy hint must NOT promise migration command exists yet (lands in #32). Phrase as "available in v1.3.0+".
