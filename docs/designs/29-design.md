# Technical Design: Vault MEMCTL_SHARED_VAULT env var (V2.1-aware)

**Spec:** docs/specs/29-spec.md
**Task:** 29
**Date:** 2026-05-01
**Status:** Draft

---

## 1. Architecture Overview

V2.1 walk-up resolver currently in `VaultLocator.Discover()` searches for `<dir>/.memctl/.obsidian/` marker pair from cwd upward. After the loop exits without a hit, NEW behavior: check `MEMCTL_SHARED_VAULT` env var. If set and points at a valid V2.1 vault root (contains `.obsidian/` subdir), return it with strategy `"MEMCTL_SHARED_VAULT env (shared)"`. Otherwise return null Vault as before.

Env var path: pointing at a vault root containing `.obsidian/` (i.e. `$MEMCTL_SHARED_VAULT/.obsidian/` exists). NOT pointing at a parent — env var is the vault root itself, parallel to what walk-up returns.

Test injection via `internal static Func<string, string?> EnvReader` hook. Production reads via `Environment.GetEnvironmentVariable`. Tests assign + restore in IDisposable.

## 2. File Changes

### Modified

| File | Change | Reason |
|------|--------|--------|
| `src/memctl/Implementations/Config/VaultLocator.cs` | Add `EnvReader` hook + post-walk-up env var fallback | FR-1, FR-5, FR-6 |
| `src/memctl/memctl.csproj` | Bump `<Version>` 1.3.0 → 1.3.1 + add `InternalsVisibleTo` for tests | NFR-3, FR-6 |
| `plugins/memctl-claude/.claude-plugin/plugin.json` | Bump `version` 1.3.0 → 1.3.1 | NFR-3 |
| `backlog/wiki/memory-pipeline.md` | Update env var status note: "MEMCTL_SHARED_VAULT support landed in v1.3.1+" | FR-7 |
| `plugins/memctl-claude/README.md` | Same correction | FR-7 |

### New

| File | Purpose | Key Exports |
|------|---------|-------------|
| `tests/memctl.Tests/Vault/VaultLocatorEnvVarTests.cs` | 4 test cases for env var fallback | xUnit Facts |

### Integration Code Blocks

```csharp
// INTEGRATION: VaultLocator.cs — full file replacement
namespace Memctl.Implementations.Config;

public sealed record VaultDiscovery(
    string?                Vault,
    string                 SearchPath,
    string                 Strategy,
    IReadOnlyList<string>  CheckedPaths);

public static class VaultLocator
{
    /// Hook for tests — production reads Environment directly.
    internal static Func<string, string?> EnvReader =
        name => Environment.GetEnvironmentVariable(name);

    public static string? FindVault(string? explicitPath)
        => Discover(explicitPath, Directory.GetCurrentDirectory()).Vault;

    public static string? FindVaultFrom(string startDir)
        => Discover(null, startDir).Vault;

    /// V2.1 resolver:
    /// 1. Explicit --vault flag wins.
    /// 2. Walk-up looks for `<dir>/.memctl/.obsidian/` marker pair.
    /// 3. MEMCTL_SHARED_VAULT env var fallback (lowest priority — per-project always wins).
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

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // Lowest-priority fallback: MEMCTL_SHARED_VAULT env var.
        // Per-project .memctl/ already wins above; sensitive vaults always take priority.
        var sharedVault = EnvReader("MEMCTL_SHARED_VAULT");
        if (!string.IsNullOrWhiteSpace(sharedVault))
        {
            checkedPaths.Add($"$MEMCTL_SHARED_VAULT={sharedVault}");
            if (Directory.Exists(Path.Combine(sharedVault, ".obsidian")))
                return new VaultDiscovery(
                    Path.GetFullPath(sharedVault), searchPath, "MEMCTL_SHARED_VAULT env (shared)", checkedPaths);
        }

        return new VaultDiscovery(null, searchPath, "walk-up v2", checkedPaths);
    }
}
```

```xml
<!-- INTEGRATION: memctl.csproj → add InternalsVisibleTo -->
<!-- Insert after the existing <ItemGroup> with PackageReferences -->
<ItemGroup>
  <InternalsVisibleTo Include="memctl.Tests" />
</ItemGroup>
```

```json
// INTEGRATION: plugin.json → version bump
// "version": "1.3.0" → "1.3.1"
```

## 3. Data Model

(no schema changes)

## 4. API Design

(no public API change — `Discover()` Strategy string adds new value `"MEMCTL_SHARED_VAULT env (shared)"`)

## 5. UI Components

n/a (CLI tool)

## 6. Business Logic

**Resolver priority (top wins):**
1. `--vault <path>` explicit flag → strategy `"explicit"`
2. Walk-up V2.1 (`<dir>/.memctl/.obsidian/`) → strategy `"walk-up v2 (.memctl/)"`
3. `MEMCTL_SHARED_VAULT` env var pointing at `<path>/.obsidian/` → strategy `"MEMCTL_SHARED_VAULT env (shared)"`
4. Default: null Vault, strategy `"walk-up v2"` (exhausted)

**Env var validation:**
- `IsNullOrWhiteSpace` → skip
- Path exists check via `Directory.Exists(envValue/.obsidian)` — same V2.1 marker as walk-up resolves
- Rationale: prevents random dir from being treated as vault if user typoed env var

## 7. Error Handling

| Scenario | Handling | FR |
|----------|----------|----|
| Env var set, path doesn't exist | Falls through to null, no exception | FR-4 |
| Env var set, path exists but no `.obsidian/` | Skipped, falls through to null | FR-4 |
| Env var unset/empty/whitespace | Skipped silently | NFR-4 |
| `EnvReader` throws | Bubbles up — production never throws (`GetEnvironmentVariable` doesn't); tests must not inject throwers | n/a |

## 8. Security Considerations

- Per-project vault ALWAYS wins over env var (FR-2). Sensitive client A cannot leak into vault of client B even if user globally set env var.
- `Path.GetFullPath` normalizes but doesn't resolve symlinks. If user symlinks env var path through sensitive dir, that's user policy.
- No env var caching: re-read per `Discover()` call — change in env var picked up next invocation.

## 9. Performance

- Env var read: O(1) syscall, no disk
- Validation: 1 `Directory.Exists` check, ~µs
- Total impact: <1ms on resolver call when walk-up empty

## 10. Testing Strategy

| Level | What | Count |
|-------|------|-------|
| Unit | 4 facts per acceptance criterion FR-1..FR-4 | 4 tests |

## 10.5 E2E Scenarios

ProjectType: cli_tool. No E2E flag — Layer 2.5 smoke only.

| Scenario | Command | Expected | FR |
|----------|---------|----------|----|
| Env var hit reported in status | `MEMCTL_SHARED_VAULT=<vault> memctl status --json` (in dir without walk-up vault) | `data.discovery.strategy == "MEMCTL_SHARED_VAULT env (shared)"` | FR-5 |
| Walk-up wins | `MEMCTL_SHARED_VAULT=<other> memctl status --json` (in dir WITH walk-up vault) | `data.discovery.strategy == "walk-up v2 (.memctl/)"` | FR-2 |

## 11. Dependencies

- Task #28 Done (workflow `verify-versions`)
- Task #31 Done (V2.1 VaultLocator)
- No new NuGet packages (NFR-4 from task body — implicit)

## 12. Implementation Order

1. Bump `<Version>` 1.3.0 → 1.3.1 in csproj, plugin.json
2. Add `InternalsVisibleTo` to csproj
3. Patch VaultLocator.cs (env var fallback + EnvReader hook)
4. Add `tests/memctl.Tests/Vault/VaultLocatorEnvVarTests.cs`
5. Build + run tests
6. Sync docs (memory-pipeline.md, plugin README)
7. Final full test suite verify

## 13. Assumptions

- `memctl.Tests` is the test project assembly name (matches csproj filename without extension).
- `Environment.GetEnvironmentVariable` returns null for unset vars (.NET standard).
- `Path.GetFullPath` doesn't throw on relative paths in env var (returns absolute via cwd join).

## 14. Traceability

| Req | Section | File | Test |
|-----|---------|------|------|
| FR-1 | 6 | VaultLocator.cs | EnvVar_used_when_no_walk_up_match |
| FR-2 | 6 | VaultLocator.cs | Walk_up_wins_over_env_var |
| FR-3 | 6 | VaultLocator.cs | Explicit_flag_wins_over_env_var |
| FR-4 | 7 | VaultLocator.cs | Env_var_pointing_at_invalid_dir_falls_through_to_null |
| FR-5 | 6 | VaultLocator.cs | (assertion in FR-1 test on Strategy) |
| FR-6 | 1 | VaultLocator.cs + csproj | InternalsVisibleTo + EnvReader assignable in tests |
| FR-7 | 2 | docs files | manual grep verification |
| NFR-1 | — | full build | `dotnet build` |
| NFR-2 | — | full test | `dotnet test` |
| NFR-3 | 2 | csproj + plugin.json | grep 1.3.1 |
