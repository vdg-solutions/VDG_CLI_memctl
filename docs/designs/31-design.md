# Technical Design: V2 foundation

**Spec:** docs/specs/31-spec.md
**Task:** 31
**Date:** 2026-05-01

## 1. Architecture Overview

3 logical changes inside V2.1 epic:

1. **Resolver (VaultLocator):** walk-up looks for `<dir>/.memctl/.obsidian/` (V2 marker pair). On miss, also checks legacy `<dir>/.obsidian/` + `<dir>/.memctl/` siblings to surface migration hint without breaking reads.
2. **Init (ObsidianVaultReader):** create `.memctl/` if not direct; inside, create `.obsidian/memctl/` runtime + 7 semantic top-level dirs.
3. **Path computations:** runtime files (index.db, hook.log, models/) move from `<vault>/.memctl/` → `<vault>/.obsidian/memctl/`. 6 operators touched.

Plus loud legacy warning at every command entrypoint that resolves a vault — emit `::warning::` annotation once per process via static flag.

## 2. File Changes

### Modified

| File | Change |
|------|--------|
| `src/memctl/Implementations/Config/VaultLocator.cs` | V2 walk-up + legacy detection in Discover() |
| `src/memctl/Implementations/Vault/ObsidianVaultReader.cs` | InitVaultStructure V2.1 layout; EnumerateMarkdownFiles exclude `.obsidian/` only |
| `src/memctl/Operators/IngestOperator.cs` | index.db path = `<vault>/.obsidian/memctl/index.db` |
| `src/memctl/Operators/HookLog.cs` | hook.log path = `<vault>/.obsidian/memctl/hook.log` |
| `src/memctl/Implementations/Config/MemctlConfig.cs` | config + models paths drop `.memctl/` prefix, add `.obsidian/memctl/` prefix |
| `src/memctl/Implementations/Embedding/GemmaEmbeddingEngine.cs` | model path nested in `.obsidian/memctl/models/` |
| `src/memctl/Operators/StatusOperator.cs` | model path V2.1; legacy detection → JSON `data.legacy_v1` + `data.migration_hint` + stderr warning |
| `src/memctl/Operators/GrepOperator.cs` | exclude `.obsidian/` only (drop `.memctl/`) |
| `src/memctl/Bootstrap/Program.cs` | static flag emits legacy warning once per process at first vault resolution |

### Created

| File | Purpose |
|------|---------|
| `tests/memctl.Tests/Vault/VaultLocatorV2Tests.cs` | 5 tests: walk-up V2, walk-up from subdir, legacy detection, no vault null, explicit override |
| `tests/memctl.Tests/Vault/InitV2Tests.cs` | 3 tests: parent path creates `.memctl/` subdir, direct path skips nesting, reinit idempotent |
| `tests/memctl.Tests/Vault/LegacyWarningTests.cs` | 1 test: stderr emits `::warning::` annotation on legacy detection |

## 3. Resolver Decision Tree

```
explicitPath != null      → return (explicit, explicitPath)
walk up dir, parent, ...:
    <dir>/.memctl/.obsidian/ exists?      → return (walk-up v2 (.memctl/), <dir>/.memctl/)
    <dir>/.obsidian/ AND <dir>/.memctl/?  → return (legacy v1 — run memctl migrate-vault, <dir>)
    parent null                            → break
return null                                (no vault — strategy "walk-up v2 + legacy")
```

V2 check before legacy check ensures `<dir>/.memctl/.obsidian/` (V2 marker) wins over coincidental legacy match if both somehow exist.

## 4. InitVaultStructure (V2.1 layout)

```csharp
var trimmed = vaultPath.TrimEnd(Path.DirectorySeparatorChar);
var isDirect = Path.GetFileName(trimmed) == ".memctl";
var vaultRoot = isDirect ? trimmed : Path.Combine(trimmed, ".memctl");

// .obsidian/ + nested memctl runtime
Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian"));
Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian", "memctl"));
Directory.CreateDirectory(Path.Combine(vaultRoot, ".obsidian", "memctl", "models"));

// 7 semantic top-level dirs
foreach (var d in new[] { "tasks", "patterns", "lessons", "decisions", "chats", "attachments", "claude-memory" })
    Directory.CreateDirectory(Path.Combine(vaultRoot, d));

// Obsidian config files
WriteIfAbsent(Path.Combine(vaultRoot, ".obsidian", "app.json"), "{}");
// ... etc
WriteIfAbsent(Path.Combine(vaultRoot, "claude-memory", "MEMORY.md"), "# Memory index\n\n");
WriteIfAbsent(Path.Combine(vaultRoot, "README.md"), /* explainer */);
```

## 5. Path computations

| Operator/file | V1 path | V2.1 path |
|---------------|---------|-----------|
| `IngestOperator.IndexPath` | `<vault>/.memctl/index.db` | `<vault>/.obsidian/memctl/index.db` |
| `HookLog.LogPath` | `<vault>/.memctl/hook.log` | `<vault>/.obsidian/memctl/hook.log` |
| `MemctlConfig.ConfigPath` | `<vault>/.memctl/config.json` | `<vault>/.obsidian/memctl/config.json` |
| `MemctlConfig.ModelsDir` | `<vault>/.memctl/models` | `<vault>/.obsidian/memctl/models` |
| `GemmaEmbeddingEngine.ModelPath` | `<vault>/.memctl/models/embeddinggemma-300m` | `<vault>/.obsidian/memctl/models/embeddinggemma-300m` |
| `StatusOperator.ModelPath` | `<vault>/.memctl/models/...` | `<vault>/.obsidian/memctl/models/...` (with V1 fallback for legacy display) |
| `EnumerateMarkdownFiles` exclude | `.obsidian/` AND `.memctl/` | `.obsidian/` only (runtime nested inside, auto-excluded) |

## 6. Legacy warning

Static flag in `Bootstrap/Program.cs`:

```csharp
private static bool _legacyWarned = false;

static void WarnIfLegacy(VaultDiscovery d)
{
    if (_legacyWarned) return;
    if (!d.Strategy.StartsWith("legacy v1")) return;
    _legacyWarned = true;
    Console.Error.WriteLine($"::warning::Vault legacy v1 layout detected at {d.Vault}. Run 'memctl migrate-vault' (available in v1.3.0+).");
}
```

Called by every command's vault-resolution path. StatusOperator additionally surfaces JSON `data.legacy_v1: true` + `data.migration_hint` for tooling consumption.

## 7. Testing

xUnit. IDisposable cleanup of tmp dirs. No env var manipulation (V2.1 doesn't depend on env).

```csharp
public class VaultLocatorV2Tests : IDisposable
{
    private readonly string _tmpRoot;
    public VaultLocatorV2Tests() { _tmpRoot = Path.Combine(Path.GetTempPath(), "memctl-test-" + Guid.NewGuid()); Directory.CreateDirectory(_tmpRoot); }
    public void Dispose() { if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, true); }

    private string MakeV2Vault(string sub) {
        var p = Path.Combine(_tmpRoot, sub);
        Directory.CreateDirectory(Path.Combine(p, ".memctl", ".obsidian"));
        return p;
    }
    private string MakeLegacyV1Vault(string sub) {
        var p = Path.Combine(_tmpRoot, sub);
        Directory.CreateDirectory(Path.Combine(p, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(p, ".memctl"));
        return p;
    }

    [Fact] public void WalkUp_finds_v2_when_memctl_contains_obsidian() { ... }
    [Fact] public void WalkUp_finds_v2_from_subdir() { ... }
    [Fact] public void Legacy_v1_returns_migration_hint() { ... }
    [Fact] public void No_vault_returns_null() { ... }
    [Fact] public void Explicit_overrides_walk_up() { ... }
}
```

## 8. Smoke

```bash
TMP=$(mktemp -d)
mkdir -p "$TMP/project/src"
echo "# README" > "$TMP/project/README.md"

memctl init --vault "$TMP/project"
test -d "$TMP/project/.memctl/.obsidian/memctl"      # runtime nested
test -d "$TMP/project/.memctl/tasks"
test -d "$TMP/project/.memctl/patterns"
test -d "$TMP/project/.memctl/lessons"
test -d "$TMP/project/.memctl/decisions"
test -d "$TMP/project/.memctl/chats"
test -d "$TMP/project/.memctl/attachments"
test -d "$TMP/project/.memctl/claude-memory"

# Legacy detection
mkdir -p "$TMP/legacy/.obsidian" "$TMP/legacy/.memctl"
echo "{}" > "$TMP/legacy/.obsidian/app.json"
memctl status --vault "$TMP/legacy" 2>&1 | grep -q "legacy v1"
memctl status --vault "$TMP/legacy" 2>&1 | grep -q "::warning::"
memctl status --vault "$TMP/legacy" --json | python -c "import sys,json; d=json.load(sys.stdin); assert d['data'].get('legacy_v1') is True"

rm -rf "$TMP"
```

## 9. Implementation Order

1. VaultLocator.cs — V2 walk-up + legacy detection
2. ObsidianVaultReader.cs — InitVaultStructure V2.1 + EnumerateMarkdownFiles exclude
3. IngestOperator.cs path
4. HookLog.cs path
5. MemctlConfig.cs paths
6. GemmaEmbeddingEngine.cs path
7. StatusOperator.cs (paths + legacy JSON + stderr warning)
8. GrepOperator.cs exclude
9. Bootstrap/Program.cs WarnIfLegacy hookup
10. Test files
11. dotnet build clean
12. dotnet test 51/51

Build after EVERY file change to catch regression early.
