---
id: 29
type: task
title: 'Vault MEMCTL_VAULT env var — shared-mode opt-in (lowest priority)'
status: Todo
priority: high
tags:
  - vault
  - isolation
  - env-var
  - resolver
  - privacy
created: 2026-05-01
updated: 2026-05-01
---

## Description

`docs/vault-isolation-runbook.md` + `plugins/memctl-claude/README.md` đã document `MEMCTL_VAULT` env var như thể đã work — nhưng `src/memctl/Implementations/Config/VaultLocator.cs:20` chỉ implement (a) explicit `--vault` flag (b) `.obsidian/` walk-up. Env var support chưa wired. Doc/code mismatch — bot/user đọc doc sẽ set env var và confused why nó không work.

User intent rõ: per-project vault là default (đã đúng — walk-up `.obsidian/`); muốn share vault giữa projects → đặt env var explicit. Task này wire env var với priority THẤP NHẤT (sau walk-up) để per-project override env var khi cả hai cùng tồn tại — đảm bảo sensitive project vault bao giờ cũng thắng.

Plus: sửa docs đã commit để khớp behavior thực tế sau patch.

## Implementation

### Step 0 — Prereq fail-fast
- Verify build clean trước khi đụng code: `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q` || exit "Fix build first"
- Verify tests pass baseline: `dotnet test --nologo` || exit "Fix existing tests first"
- Verify VaultLocator file: `test -f src/memctl/Implementations/Config/VaultLocator.cs` || exit "VaultLocator missing — wrong branch?"

### Step 1 — Extend VaultLocator with env var fallback

- **File MODIFY:** `src/memctl/Implementations/Config/VaultLocator.cs`
- Add env var check AFTER walk-up loop returns null (explicit-low-priority placement). Inject env var read so unit tests can override:

```csharp
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

    public static VaultDiscovery Discover(string? explicitPath, string searchPath)
    {
        if (explicitPath is not null)
            return new VaultDiscovery(explicitPath, explicitPath, "explicit", [explicitPath]);

        var checkedPaths = new List<string>();
        var dir = searchPath;
        while (true)
        {
            checkedPaths.Add(dir);
            if (Directory.Exists(Path.Combine(dir, ".obsidian")))
                return new VaultDiscovery(Path.GetFullPath(dir), searchPath, "walk-up from cwd", checkedPaths);

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // Fallback: MEMCTL_VAULT env var (explicit opt-in to shared/global vault).
        // Lower priority than walk-up so per-project .obsidian/ always wins.
        var sharedVault = EnvReader("MEMCTL_VAULT");
        if (!string.IsNullOrWhiteSpace(sharedVault))
        {
            checkedPaths.Add($"$MEMCTL_VAULT={sharedVault}");
            if (Directory.Exists(Path.Combine(sharedVault, ".obsidian")))
                return new VaultDiscovery(
                    Path.GetFullPath(sharedVault), searchPath, "MEMCTL_VAULT env (shared)", checkedPaths);
        }

        return new VaultDiscovery(null, searchPath, "walk-up from cwd + env", checkedPaths);
    }
}
```

### Step 2 — Unit tests

- **File CREATE:** `tests/memctl.Tests/Vault/VaultLocatorEnvVarTests.cs`
- 4 test cases (xUnit):

```csharp
using System.IO;
using Memctl.Implementations.Config;
using Xunit;

public class VaultLocatorEnvVarTests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly Func<string, string?> _origEnvReader;

    public VaultLocatorEnvVarTests()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), "memctl-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_tmpRoot);
        _origEnvReader = VaultLocator.EnvReader;
    }

    public void Dispose()
    {
        VaultLocator.EnvReader = _origEnvReader;
        if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, true);
    }

    private string MakeVault(string sub)
    {
        var p = Path.Combine(_tmpRoot, sub);
        Directory.CreateDirectory(Path.Combine(p, ".obsidian"));
        return p;
    }

    [Fact]
    public void EnvVar_used_when_no_walk_up_match()
    {
        var sharedVault = MakeVault("shared");
        var noVaultDir = Path.Combine(_tmpRoot, "elsewhere");
        Directory.CreateDirectory(noVaultDir);
        VaultLocator.EnvReader = name => name == "MEMCTL_VAULT" ? sharedVault : null;

        var d = VaultLocator.Discover(null, noVaultDir);

        Assert.NotNull(d.Vault);
        Assert.Equal(Path.GetFullPath(sharedVault), d.Vault);
        Assert.Equal("MEMCTL_VAULT env (shared)", d.Strategy);
    }

    [Fact]
    public void Walk_up_wins_over_env_var()
    {
        var projectVault = MakeVault("project");
        var sharedVault  = MakeVault("shared");
        VaultLocator.EnvReader = name => name == "MEMCTL_VAULT" ? sharedVault : null;

        var d = VaultLocator.Discover(null, projectVault);

        Assert.Equal(Path.GetFullPath(projectVault), d.Vault);
        Assert.Equal("walk-up from cwd", d.Strategy);
    }

    [Fact]
    public void Explicit_flag_wins_over_env_var()
    {
        var explicitVault = MakeVault("explicit");
        var sharedVault   = MakeVault("shared");
        VaultLocator.EnvReader = name => name == "MEMCTL_VAULT" ? sharedVault : null;

        var d = VaultLocator.Discover(explicitVault, _tmpRoot);

        Assert.Equal(explicitVault, d.Vault);
        Assert.Equal("explicit", d.Strategy);
    }

    [Fact]
    public void Env_var_pointing_at_invalid_dir_falls_through_to_null()
    {
        var noVaultDir = Path.Combine(_tmpRoot, "elsewhere");
        Directory.CreateDirectory(noVaultDir);
        VaultLocator.EnvReader = name => name == "MEMCTL_VAULT" ? "/nonexistent/path" : null;

        var d = VaultLocator.Discover(null, noVaultDir);

        Assert.Null(d.Vault);
    }
}
```

### Step 3 — Sync existing docs to actual behavior

- **File MODIFY:** `docs/vault-isolation-runbook.md` — replace "verified" claim about env var (currently misleading — claim was made before code support landed). Add note: "MEMCTL_VAULT support landed in v1.2.1+ via task #29." Keep priority order as-is (already matches new code).
- **File MODIFY:** `plugins/memctl-claude/README.md` — same correction. Note version requirement.
- **File MODIFY:** `plugins/memctl-claude/.claude-plugin/plugin.json` — bump `version` 1.2.0 → 1.2.1 to match next csproj bump (NFR-3 task #28 verify-versions enforces).
- **File MODIFY:** `src/memctl/memctl.csproj` — bump `<Version>` 1.2.0 → 1.2.1 (release-coupled with this feature).

### Step 4 — Re-sync plugin source to public release host

After private repo merge, propagate to `vdg-solutions/memctl-releases/plugins/memctl-claude/README.md` (Contents API PUT). Manual until next tag push triggers workflow auto-sync. Documented in `docs/plugin-publish-runbook.md` mid-release flow.

### Step 5 — Build + test verify

```bash
dotnet build src/memctl/memctl.csproj -c Release --nologo -v q
dotnet test --filter VaultLocatorEnvVarTests --nologo
dotnet test --nologo                              # full suite, all green
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `MEMCTL_VAULT` env var pointing at vault with `.obsidian/` resolves to that vault when cwd has no `.obsidian/` walk-up match | `dotnet test --filter EnvVar_used_when_no_walk_up_match` exit 0 |
| FR-2 | `.obsidian/` walk-up from cwd takes priority over `MEMCTL_VAULT` (sensitive project always wins) | `dotnet test --filter Walk_up_wins_over_env_var` exit 0 |
| FR-3 | `--vault` CLI flag takes priority over both env var and walk-up | `dotnet test --filter Explicit_flag_wins_over_env_var` exit 0 |
| FR-4 | `MEMCTL_VAULT` pointing at non-existent dir falls through to "no vault" (does NOT throw) | `dotnet test --filter Env_var_pointing_at_invalid_dir_falls_through_to_null` exit 0 |
| FR-5 | `Discover()` strategy field reports `"MEMCTL_VAULT env (shared)"` when env var hit | grep `Strategy = "MEMCTL_VAULT env (shared)"` in test assertions; visible in `memctl status --json` `data.discovery.strategy` field |
| NFR-1 | Build clean: 0 warning, 0 error | `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q 2>&1 \| grep -cE "warning\|error"` returns 0 |
| NFR-2 | Full test suite green (no regression) | `dotnet test --nologo` exit 0, all tests pass |
| NFR-3 | Plugin version === csproj version (workflow #28 enforces) | `python -c "import json; print(json.load(open('plugins/memctl-claude/.claude-plugin/plugin.json'))['version'])"` === `grep -oE '<Version>[^<]+' src/memctl/memctl.csproj \| sed 's/<Version>//'` |
| NFR-4 | Cross-platform: env var read via `Environment.GetEnvironmentVariable` (Windows/Linux/macOS portable) | grep `EnvReader` in VaultLocator.cs to confirm injection point |

## Out of Scope

- Multiple env-var search paths (`MEMCTL_VAULT_PATH=path1:path2`). Future task if needed.
- Auto-init project vault if cwd lacks one (could surprise users — keep explicit `memctl init` requirement).
- Symlink resolution / canonicalization beyond `Path.GetFullPath`. Future task.
- Removal of `MEMCTL_VAULT` env var support if `.obsidian/` walk-up succeeds (current code already short-circuits walk-up before env check — env only kicks in on walk-up miss).

## Dependencies

- Blocked by `#28` (workflow `verify-versions` job) — Done. Plugin version bump must be lockstep with csproj.
- Touches `src/memctl/Implementations/Config/VaultLocator.cs` (last modified prior to task batch #14-23).

## Risk

| Risk | Mitigation |
|------|-----------|
| User has `MEMCTL_VAULT` set + accidentally `cd` into project without `.obsidian/` → unexpected fallback to global vault | Documented behavior — env var is explicit opt-in. `memctl status --json` shows `data.discovery.strategy` so user can audit which vault active. |
| Env var pointing at deleted dir crashes resolver | Test FR-4 covers — falls through to null, no exception. |
| Cross-process race (env var changed mid-process) | `Environment.GetEnvironmentVariable` reads at call time, not cached — safe per-invocation. |
| Test EnvReader injection leaks across test runs | `IDisposable.Dispose()` restores `_origEnvReader` after each test. xUnit isolates instances. |
| Plugin version 1.2.1 ships before csproj bump merged | Step 3 bumps both atomically in same commit — workflow `verify-versions` job rejects mismatched tag if anyone tries to ship out of order. |

## Effort

~3-4h:
- 0.5h: VaultLocator.cs extension + EnvReader injection point
- 1h: 4 unit tests + Dispose cleanup pattern
- 0.5h: build + run tests, fix any regression
- 0.5h: docs sync (vault-isolation-runbook.md + plugin README) + version bumps
- 0.5h: API sync plugin README to public memctl-releases
- 0.5h: PR + self-review

## User Actions Required

- (none — fully bot-actionable; PAT scope already covers required repos)

## Notes

- This task partly retro-fixes a doc/code mismatch from the post-#28 documentation push. Documenting "MEMCTL_VAULT works as fallback" before code shipped was a false claim — task #29 makes the doc honest.
- `.obsidian/` is the current vault marker (Obsidian-compatible by design). Don't change to `.memctl/` — would break vault interop with the Obsidian app.
- `MEMCTL_VAULT` being LOWEST priority (after walk-up) is the security-relevant property: per-project sensitive vaults always win over a globally-set env var. Don't reorder priorities without security review.
