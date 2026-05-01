---
id: 30
type: task
title: 'Vault layout V2 — .memctl/ as vault root container + migration + skill rewrite'
status: Todo
priority: high
tags:
  - vault
  - layout
  - breaking-change
  - migration
  - isolation
  - skill
  - plugin
created: 2026-05-01
updated: 2026-05-01
---

## Description

V1 vault layout pollutes project trees: `memctl init --vault <repo>` tạo `.obsidian/` + `.memctl/` ngay tại repo root, `EnumerateMarkdownFiles` recursive scan → README.md, src/**/*.md, docs/**/*.md đều bị index như memory notes. Per-project install hoặc poison context, hoặc force user vào ngõ cụt (init subdir thì walk-up không tìm thấy).

V2 layout giải quyết: `.memctl/` LÀ vault root container — chứa `.obsidian/` + index.db + chats/ + notes ở bên trong. Project root không có `.obsidian/` → Obsidian/Memctl không nhầm project là vault. Walk-up từ `<project>/src/...` tìm `<project>/.memctl/` (giống pattern `.git/`).

```
V1 (current — broken for per-project):       V2 (this task):
<project>/                                    <project>/
├── .obsidian/                                ├── .memctl/                ← vault root
├── .memctl/                                  │   ├── .obsidian/          ← Obsidian config inside
│   ├── index.db                              │   ├── index.db            ← flat (no nested .memctl/)
│   └── models/                               │   ├── models/
├── chats/                                    │   ├── chats/
├── claude-memory/                            │   ├── claude-memory/
├── src/        ← INDEXED (bad)               │   └── *.md                ← memory notes
└── README.md   ← INDEXED (bad)               ├── src/                    ← NOT indexed
                                              └── README.md               ← NOT indexed
```

Breaking change. Bumps to v1.3.0 (major-minor — vault contract changes). Migration command `memctl migrate-vault` cho legacy users. Skill text + plugin README + public memctl-releases plugin source phải rewrite theo V2 examples.

## Implementation

### Step 0 — Prereq fail-fast
- Verify `#28` Done: `bl show 28 | grep -q '^status: Done'` || exit "Blocked by #28 (workflow version verify-versions enforces version lockstep)"
- Verify build clean baseline: `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q` || exit "Fix build first"
- Verify tests pass baseline: `dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo` || exit "Fix existing tests first"
- Verify legacy vault test fixture builds OK (em sẽ tạo trong Step 5).

### Step 1 — Refactor VaultLocator: walk-up `.memctl/` marker

- **File MODIFY:** `src/memctl/Implementations/Config/VaultLocator.cs`
- New marker = `.memctl/` (must contain `.obsidian/` to count as full vault).
- Returns vault path = path of `.memctl/` itself (not its parent).
- Detect legacy V1 layout (`.obsidian/` standalone, sibling of `.memctl/`) → return strategy `"legacy v1 — run memctl migrate-vault"` so StatusOperator can surface migration hint.

```csharp
public static VaultDiscovery Discover(string? explicitPath, string searchPath)
{
    if (explicitPath is not null)
        return new VaultDiscovery(explicitPath, explicitPath, "explicit", [explicitPath]);

    var checkedPaths = new List<string>();
    var dir = searchPath;
    while (true)
    {
        checkedPaths.Add(dir);

        // V2: <project>/.memctl/ containing .obsidian/
        var v2Vault = Path.Combine(dir, ".memctl");
        if (Directory.Exists(v2Vault) && Directory.Exists(Path.Combine(v2Vault, ".obsidian")))
            return new VaultDiscovery(Path.GetFullPath(v2Vault), searchPath, "walk-up v2 (.memctl/)", checkedPaths);

        // V1 legacy: dir contains both .obsidian/ AND .memctl/ as siblings → migration needed
        if (Directory.Exists(Path.Combine(dir, ".obsidian")) && Directory.Exists(Path.Combine(dir, ".memctl")))
            return new VaultDiscovery(Path.GetFullPath(dir), searchPath, "legacy v1 — run memctl migrate-vault", checkedPaths);

        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }

    // Env var fallback (from #29 if shipped). Skip for #30; merge if #29 ships first.
    return new VaultDiscovery(null, searchPath, "walk-up v2 + legacy", checkedPaths);
}
```

### Step 2 — Refactor InitVaultStructure for V2 layout

- **File MODIFY:** `src/memctl/Implementations/Vault/ObsidianVaultReader.cs`
- `InitVaultStructure(string vaultPath)` interpretation: `vaultPath` = directory the user wants as project anchor. Inside it, create `<vaultPath>/.memctl/` as vault root and populate.
- Backward-compat consideration: if user passes `vaultPath` that already ENDS with `.memctl` segment, treat as direct vault root (don't create `.memctl/.memctl/`).

```csharp
public void InitVaultStructure(string vaultPath)
{
    // Detect: caller passed parent dir OR direct .memctl/ path
    var isDirect = Path.GetFileName(vaultPath.TrimEnd(Path.DirectorySeparatorChar)) == ".memctl";
    var vaultRoot = isDirect ? vaultPath : Path.Combine(vaultPath, ".memctl");

    Directory.CreateDirectory(vaultRoot);
    Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "chats"));
    Directory.CreateDirectory(Path.Combine(vaultRoot, "models"));

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

    // Hint Obsidian: this vault is intentionally inside a hidden folder.
    WriteIfAbsent(Path.Combine(vaultRoot, "README.md"),
        "# memctl vault\n\nObsidian: open this folder as vault. Memctl handles indexing automatically.\n");
}
```

### Step 3 — Update path computations across codebase

- **File MODIFY:** `src/memctl/Operators/IngestOperator.cs:82` — `Path.Combine(vaultPath, "index.db")` (was `.memctl/index.db`).
- **File MODIFY:** `src/memctl/Operators/HookLog.cs:10,16` — `Path.Combine(vaultPath, "hook.log")` (was `.memctl/hook.log`).
- **File MODIFY:** `src/memctl/Implementations/Config/MemctlConfig.cs:11,15` — config + models path drop `.memctl` prefix.
- **File MODIFY:** `src/memctl/Implementations/Embedding/GemmaEmbeddingEngine.cs:158` — model path drop `.memctl` prefix.
- **File MODIFY:** `src/memctl/Operators/StatusOperator.cs:10,26` — model path drop prefix; "no vault found" message references V2 location.
- **File MODIFY:** `src/memctl/Implementations/Vault/ObsidianVaultReader.cs:18` (+`Operators/GrepOperator.cs:23`) — drop `.memctl/` exclusion (vault root IS `.memctl/`, only exclude `.obsidian/` from .md scan).

### Step 4 — Migration command

- **File CREATE:** `src/memctl/Operators/MigrateVaultOperator.cs`:
  ```csharp
  // Detect: <vaultPath>/.obsidian + <vaultPath>/.memctl coexist as siblings (V1 layout)
  // Action: move .obsidian into .memctl/, flatten .memctl/.memctl/index.db -> .memctl/index.db,
  //         move sibling chats/, claude-memory/, root *.md notes into .memctl/.
  // Idempotent: if already V2, exit 0.
  // Dry-run flag: report planned moves without executing.
  ```
- **File MODIFY:** `src/memctl/Bootstrap/Program.cs` — register `migrate-vault` subcommand:
  ```csharp
  var migrateCmd = new Command("migrate-vault", "Migrate V1 vault layout to V2 (.memctl/ as root container)");
  var dryRunOpt = new Option<bool>("--dry-run", "Report planned moves without executing");
  migrateCmd.AddOption(dryRunOpt);
  migrateCmd.SetHandler(ctx => {
      var g = G(ctx);
      if (RequireVaultExplicit(g, ctx) is not { } vault) return;
      var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
      var op = new MigrateVaultOperator();
      ResultPrinter.Print(op.Execute(vault, dryRun));
  });
  root.AddCommand(migrateCmd);
  ```

### Step 5 — Tests

- **File CREATE:** `tests/memctl.Tests/Vault/VaultLocatorV2Tests.cs` — 5 tests:
  - V2 layout: `<root>/.memctl/.obsidian/` → resolves to `<root>/.memctl/`, strategy `walk-up v2`
  - V2 walk-up from subdir: cwd `<root>/src/x.cs` → finds `<root>/.memctl/`
  - V1 legacy: `<root>/.obsidian/` + `<root>/.memctl/` siblings → strategy `legacy v1 — run memctl migrate-vault`
  - No vault: cwd in dir tree without any markers → null
  - Explicit `--vault` overrides walk-up

- **File CREATE:** `tests/memctl.Tests/Vault/InitV2Tests.cs` — 3 tests:
  - `InitVaultStructure(<path>)` creates `<path>/.memctl/.obsidian/` + `<path>/.memctl/index.db` (parent style)
  - `InitVaultStructure(<path>/.memctl)` direct invocation skips nested `.memctl/.memctl/`
  - Re-init existing V2 vault: idempotent (no overwrite of existing JSON)

- **File CREATE:** `tests/memctl.Tests/Vault/MigrationTests.cs` — 4 tests:
  - V1 → V2: setup V1 fixture, run migrate, verify `.memctl/.obsidian/` exists, `.memctl/index.db` (flat) exists, sibling `.obsidian/` removed, sibling `chats/` moved into `.memctl/chats/`
  - Already V2: migrate no-op, exit 0, no file mutations
  - Dry-run: prints planned moves, makes 0 file changes
  - Mixed/corrupt: V1 with empty `.memctl/` (no index.db) → migrate succeeds, creates fresh layout

### Step 6 — Skill + plugin README rewrite

- **File MODIFY:** `docs/memctl.md` (single source of truth for skill text):
  - Replace `memctl init ~/my-vault` examples with `memctl init --vault <project-anchor-dir>` (V2 creates `.memctl/` inside)
  - Note: Obsidian users open `<project-anchor>/.memctl/` as vault folder
  - Add "Vault layout (V2)" section with directory tree diagram
  - Add migration note for v1.x users: `memctl migrate-vault --vault <old-vault>`

- **File MODIFY:** `plugins/memctl-claude/README.md`:
  - Per-project example: `cd <repo>; memctl init --vault . && Add-Content .gitignore ".memctl/"`
  - Drop `--vault .memctl-vault` examples (legacy V1-style subdir convention now obsolete)
  - Vault auto-detect priority updated to V2 markers

- **File MODIFY:** `docs/vault-isolation-runbook.md`:
  - "Three-tier setup" rewritten — V2 examples
  - "Per-project vault" no longer needs `.gitignore` workaround pattern (vault is `.memctl/` which is intuitive to gitignore)

- **File EXEC:** `bash scripts/sync-skill-to-plugin.sh` — propagate updated `docs/memctl.md` to `plugins/memctl-claude/skills/memctl/SKILL.md`.

### Step 7 — Public release host sync (manual until next tag)

- **API PUT:** sync these files to `vdg-solutions/memctl-releases/plugins/memctl-claude/`:
  - `README.md`
  - `skills/memctl/SKILL.md`
  - `commands/*.md` if examples reference vault paths
- **API PUT:** sync top-level `vdg-solutions/memctl-releases/SKILL.md` (release archive embed).

After next tag push, workflow `release.yml` auto-syncs everything per #28 sync step. This manual API sync is for users who `claude plugin update` between tags.

### Step 8 — Version bump v1.2.0 → v1.3.0

- **File MODIFY:** `src/memctl/memctl.csproj` — `<Version>1.3.0</Version>`
- **File MODIFY:** `plugins/memctl-claude/.claude-plugin/plugin.json` — `"version": "1.3.0"`
- Major-minor bump (1.2 → 1.3) reflects vault layout breaking change. SemVer minor (not patch, not major-major) chosen because:
  - Existing v1.2 binaries still work on legacy vaults (read path computations would fail post-migration, but pre-migration they work)
  - Migration command provides clear upgrade path
  - Public API surface (CLI commands, MCP wire, JSON contract) unchanged — only filesystem layout

### Step 9 — Build + test verify + smoke

```bash
dotnet build src/memctl/memctl.csproj -c Release --nologo -v q       # 0 warning, 0 error
dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo          # 42 existing + 12 new = 54/54

# Smoke: V2 init + ingest + status
TMP=$(mktemp -d)
mkdir -p "$TMP/project/src"
echo "// dummy" > "$TMP/project/src/foo.cs"
echo "# Project README" > "$TMP/project/README.md"
memctl init --vault "$TMP/project"
test -d "$TMP/project/.memctl/.obsidian" && echo "V2 layout OK"
test ! -f "$TMP/project/.obsidian/app.json" && echo "no V1 pollution at parent OK"
echo "# memory note" > "$TMP/project/.memctl/test-note.md"
memctl ingest --vault "$TMP/project/.memctl"
memctl list --vault "$TMP/project/.memctl" | grep "test-note" && echo "note indexed OK"
memctl list --vault "$TMP/project/.memctl" | grep -q "foo\|README" && echo "FAIL: project source leaked into index" || echo "isolation OK"

# Smoke: V1 → V2 migration
mkdir -p "$TMP/legacy/.obsidian" "$TMP/legacy/.memctl" "$TMP/legacy/chats"
echo "{}" > "$TMP/legacy/.obsidian/app.json"
sqlite3 "$TMP/legacy/.memctl/index.db" "CREATE TABLE notes(id INTEGER);"
echo "# old chat" > "$TMP/legacy/chats/2026-04-01.md"
memctl migrate-vault --vault "$TMP/legacy"
test -d "$TMP/legacy/.memctl/.obsidian" && echo "migration OK"
test -f "$TMP/legacy/.memctl/index.db" && echo "index preserved OK"
test ! -d "$TMP/legacy/.obsidian" && echo "old .obsidian/ moved OK"

rm -rf "$TMP"
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `memctl init --vault <path>` creates V2 layout with `<path>/.memctl/.obsidian/` + `<path>/.memctl/index.db` flat (no nested `.memctl/.memctl/`) | smoke test exit 0 + `find <path> -name index.db -path '*/.memctl/.memctl/*'` returns 0 hits |
| FR-2 | Walk-up from project subdir resolves to `<project>/.memctl/` (V2) | `dotnet test --filter VaultLocatorV2Tests.WalkUp_finds_v2_from_subdir` exit 0 |
| FR-3 | Legacy V1 layout (sibling `.obsidian/` + `.memctl/`) returns strategy `legacy v1 — run memctl migrate-vault` | `dotnet test --filter VaultLocatorV2Tests.Legacy_v1_returns_migration_hint` exit 0 |
| FR-4 | `memctl migrate-vault --vault <legacy>` moves V1 → V2: `.obsidian/` into `.memctl/`, sibling chats/ + claude-memory/ + root *.md into `.memctl/`, flatten nested `.memctl/.memctl/index.db` to `.memctl/index.db` | `dotnet test --filter MigrationTests.V1_to_V2_full_migration` exit 0 |
| FR-5 | `memctl migrate-vault --dry-run` reports planned moves but mutates 0 files | `dotnet test --filter MigrationTests.DryRun_no_mutations` exit 0 |
| FR-6 | Re-running migrate on V2 vault is idempotent (exit 0, no changes) | `dotnet test --filter MigrationTests.V2_idempotent` exit 0 |
| FR-7 | `EnumerateMarkdownFiles` excludes `.obsidian/` only, includes vault root + chats/ + subdirs | smoke: file at `<vault>/.memctl/foo.md` indexed; `<vault>/.memctl/.obsidian/app.json` not indexed (it's .json anyway); `<vault>/src/foo.md` (project source, OUTSIDE `.memctl/`) NOT indexed |
| FR-8 | StatusOperator surfaces V2 vault details + migration hint when legacy detected | run `memctl status --json` on V1 fixture, expect `data.discovery.strategy` contains "legacy v1" |
| FR-9 | Skill text (docs/memctl.md) reflects V2 examples — no `~/my-vault` legacy patterns | `grep -E "init.*~/.*vault" docs/memctl.md \| wc -l` returns 0; `grep -c "\.memctl/" docs/memctl.md` returns ≥3 |
| FR-10 | Plugin README V2 examples — drop legacy `.memctl-vault` pattern | `grep -c "\.memctl-vault" plugins/memctl-claude/README.md` returns 0 |
| NFR-1 | Build clean: 0 warning, 0 error | `dotnet build -c Release --nologo -v q 2>&1 \| grep -cE "warning\|error"` returns 0 |
| NFR-2 | Existing 42 tests still pass + 12 new tests added (54 total) | `dotnet test --nologo` exit 0, "Passed: 54" in output |
| NFR-3 | Plugin version === csproj version === 1.3.0 (verify-versions enforces) | `python -c "import json; print(json.load(open('plugins/memctl-claude/.claude-plugin/plugin.json'))['version'])"` returns 1.3.0; csproj `<Version>` returns 1.3.0 |
| NFR-4 | Migration command exit 0 when no vault found at path (graceful) | `memctl migrate-vault --vault /nonexistent; echo $?` returns 0 OR clear error message; verified via test |
| NFR-5 | Public memctl-releases SKILL.md + plugin README synced (manual API or via next tag push) | `curl https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/SKILL.md \| grep -c "\.memctl/"` returns ≥3 |

## Out of Scope

- Auto-detect which V1 vault belongs to which project (multi-vault scan). User runs migrate per-vault explicitly.
- Backwards-incompatible search/list semantics (notes API surface unchanged — only filesystem layout).
- Auto-migration on first `memctl status` after upgrade (would surprise users — explicit `migrate-vault` opt-in).
- Vault-format version field in `index.db` (could add `schema_version` row in future task; not blocking).
- Removal of #29 `MEMCTL_SHARED_VAULT` env var feature (still useful for personal global vault — keep if shipped).

## Dependencies

- **Blocked by #28 (Done)** — workflow verify-versions enforces csproj/plugin lockstep. Without it, version bump could ship out of sync.
- **Coordinates with #29** — if #29 ships first, merge env var fallback at correct priority position (after walk-up V2 + V1 legacy detect, before null). Em sẽ rebase #29 onto #30 if both in flight.
- Touches: VaultLocator.cs, ObsidianVaultReader.cs, IngestOperator.cs, HookLog.cs, MemctlConfig.cs, GemmaEmbeddingEngine.cs, StatusOperator.cs, GrepOperator.cs, Bootstrap/Program.cs, docs/memctl.md, plugins/memctl-claude/README.md, docs/vault-isolation-runbook.md.

## Risk

| Risk | Mitigation |
|------|-----------|
| User auto-updates plugin/binary mid-session, vault stops resolving (V1 → V2 detection fails on existing vault) | Detection step in VaultLocator returns strategy `"legacy v1 — run memctl migrate-vault"`, StatusOperator surfaces hint, hooks degrade silently (exit 0). User runs migration explicitly. Document in CHANGELOG + plugin README upgrade notes. |
| Migration corrupts vault (move operation interrupted, partial state) | Migration writes new layout into `<vaultPath>/.memctl-v2-tmp/`, then atomic rename to `<vaultPath>/.memctl/` after all moves succeed. Original `.memctl/` and `.obsidian/` removed only after rename. Crash mid-way leaves both layouts coexisting — re-run safe. |
| Obsidian app open at OLD vault path post-migration breaks Obsidian's `.obsidian/` references | Document: user must re-open Obsidian vault at new `.memctl/` path. Existing Obsidian workspace state in `.obsidian/workspace.json` migrated as-is — links/tabs may need refresh once. |
| Public memctl-releases SKILL.md drift from source repo skill | Workflow #25 release job auto-syncs on next tag. Manual API sync between tags documented in plugin-publish-runbook.md. |
| Breaking change anger users running v1.2.x without reading CHANGELOG | Major-minor SemVer bump (1.2 → 1.3) signals — not a patch. Release notes lead with "BREAKING: vault layout changed, run `memctl migrate-vault`". Migration command preserves data (no destructive ops). |
| `memctl init --vault <path>` ambiguity (does path mean parent of .memctl/ or .memctl/ itself?) | InitVaultStructure detects `Path.GetFileName(vaultPath) == ".memctl"` — both invocation forms work. Doc explicitly recommends parent-dir form: `memctl init --vault <project-anchor>` → vault at `<anchor>/.memctl/`. |
| Existing 61 notes in dev test vault (legacy V1 at repo root .memctl/) lost during dev work | Em sẽ run `memctl migrate-vault --dry-run --vault E:\repos\CLIs\VDG_CLI_memctl` first to inspect, then full migrate, before merging this task. Confirms migration logic on real data. |

## Effort

~10-12h:
- 1h: VaultLocator.cs walk-up V2 + legacy detection
- 1.5h: InitVaultStructure refactor + path computation updates across 5+ files
- 1.5h: MigrateVaultOperator + bootstrap wiring + atomic rename safety
- 1.5h: 12 unit tests (VaultLocatorV2 + InitV2 + Migration) + IDisposable tmp cleanup
- 1h: build + run tests, fix regressions across operators
- 1.5h: skill text rewrite (docs/memctl.md) + plugin README + isolation runbook update
- 0.5h: sync-skill-to-plugin.sh + version bumps (csproj + plugin.json) to 1.3.0
- 1h: API sync to public memctl-releases/plugins + top-level SKILL.md
- 1h: end-to-end smoke (V2 init isolated, V1 migrate, plugin install update on clean Claude Code)
- 0.5h: CHANGELOG entry + release notes draft for next tag

## User Actions Required

- [USER-ACTION-REQUIRED] After merge + tag `v1.3.0`, anh chạy `memctl migrate-vault --vault <existing-vault>` cho mỗi vault legacy. Plugin update fetches new binary; legacy vaults won't resolve until migrated. Documented in release notes + CHANGELOG.
- (optional) Anh test `memctl migrate-vault --dry-run --vault E:\repos\CLIs\VDG_CLI_memctl` để inspect 61-note dev vault trước khi full migrate.

## Notes

- V2 layout makes per-project vault the natural default — `cd <repo>` → walk-up finds `<repo>/.memctl/` → hooks work without env var.
- `MEMCTL_SHARED_VAULT` env var (from #29 if shipped) still useful for personal global vault (~/.memctl-personal/) — V2 doesn't obsolete #29.
- Skill rewrite (docs/memctl.md) MUST happen in this task because skill is single-source-of-truth synced to plugin SKILL.md and copied to public memctl-releases SKILL.md per release. Stale skill = stale install everywhere.
- Plugin marketplace.json `version` field bumped to 1.3.0 in next workflow run (sync-marketplace job from #28).
- Migration is one-way: V2 → V1 reverse migration NOT supported. Documented as a one-direction upgrade.
