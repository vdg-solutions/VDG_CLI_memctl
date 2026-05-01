# Technical Design: Vault Auto-Detect and Skill Rename

**Spec:** docs/specs/8-spec.md
**Task:** 8
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

Minimal-touch change. The vault discovery logic is extracted into a single static utility class `VaultLocator` placed in the `Implementations/Config/` layer (peer to `MemctlConfig`). `Program.cs` replaces its inline null-check with a `VaultLocator.FindVault()` call. No operator, port, entity, or index changes required.

### System Context

```
User runs: memctl search "ethereum"  (no --vault)
   ↓
Program.cs: RequireVault(g, ctx)
   ↓
VaultLocator.FindVault(g.Vault=null)
   ↓ walk up from cwd
   Found: /home/user/project/.obsidian/ → return "/home/user/project"
   ↓
SearchOperator.Execute("/home/user/project", ...)
```

```
User runs: memctl mcp  (no --vault, spawned from /home/user/myproject)
   ↓
Program.cs: RequireVault(g, ctx)
   ↓
VaultLocator.FindVault(null)
   ↓ cwd = /home/user/myproject → finds .obsidian/ → returns path
   ↓
McpServerOperator constructed with vault path
```

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Implementations/Config/VaultLocator.cs` | Walk cwd upward to find vault by `.obsidian/` marker | `VaultLocator.FindVault(string?)` |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Bootstrap/Program.cs` | Replace `RequireVault` body; add `RequireVaultExplicit`; change `initCmd` to use explicit variant | FR-005, FR-006, FR-007 |
| `docs/SKILL.md` → `docs/memctl.md` | Rename file; update `--vault` to optional; update MCP config example | FR-010, FR-011, FR-012 |

### Integration Code Blocks

```
// INTEGRATION: Program.cs → RequireVault() (replace body only)
// old_string (unique anchor):
    if (g.Vault is not null) return g.Vault;
    ResultPrinter.Print(MemctlOutcome.Fail("error", "--vault is required for this command"));
    ctx.ExitCode = 1;
    return null;

// new_string:
    var vault = VaultLocator.FindVault(g.Vault);
    if (vault is not null) return vault;
    ResultPrinter.Print(MemctlOutcome.Fail("error",
        "No vault found. Create one with 'memctl init --vault <path>' or run from a directory containing a vault."));
    ctx.ExitCode = 1;
    return null;
```

```
// INTEGRATION: Program.cs — add RequireVaultExplicit after closing brace of RequireVault
// old_string (anchor — closing of RequireVault):
}

// --- init ---

// new_string:
}

// init requires explicit vault path (can't auto-detect a vault that doesn't exist yet)
string? RequireVaultExplicit(GlobalOptions g, InvocationContext ctx)
{
    if (g.Vault is not null) return g.Vault;
    ResultPrinter.Print(MemctlOutcome.Fail("error", "--vault is required for this command"));
    ctx.ExitCode = 1;
    return null;
}

// --- init ---
```

```
// INTEGRATION: Program.cs → initCmd.SetHandler (change RequireVault → RequireVaultExplicit)
// old_string:
    if (RequireVault(g, ctx) is not { } vault) return;
    vaultReader.InitVaultStructure(vault);

// new_string:
    if (RequireVaultExplicit(g, ctx) is not { } vault) return;
    vaultReader.InitVaultStructure(vault);
```

### Deleted Files

| File Path | Reason |
|-----------|--------|
| `docs/SKILL.md` | Renamed to `docs/memctl.md` |

---

## 3. Data Model

No data model changes.

---

## 4. API Design

No API changes. CLI interface changes:
- `--vault` becomes optional for all commands except `init`
- `init` still requires `--vault`

---

## 5. VaultLocator Implementation

```csharp
// src/memctl/Implementations/Config/VaultLocator.cs
namespace Memctl.Implementations.Config;

public static class VaultLocator
{
    /// <summary>
    /// Returns the vault path to use.
    /// If explicitPath is provided, returns it directly.
    /// Otherwise walks up from cwd looking for a directory containing .obsidian/.
    /// Returns null if no vault found.
    /// </summary>
    public static string? FindVault(string? explicitPath)
    {
        if (explicitPath is not null)
            return explicitPath;

        var dir = Directory.GetCurrentDirectory();
        while (true)
        {
            if (Directory.Exists(Path.Combine(dir, ".obsidian")))
                return Path.GetFullPath(dir);

            var parent = Directory.GetParent(dir);
            if (parent is null) return null;
            dir = parent.FullName;
        }
    }
}
```

---

## 6. Business Logic

**FR-001 — Walk algorithm:**
1. If explicit path provided → return immediately (no walk)
2. Start at `Directory.GetCurrentDirectory()`
3. Check `{dir}/.obsidian` exists
4. If yes → return `Path.GetFullPath(dir)`
5. If no → get parent via `Directory.GetParent(dir)`
6. If parent is null (at root) → return null
7. Repeat from step 3 with parent

**FR-006 — init stays explicit:**
- `initCmd` uses `RequireVaultExplicit` instead of `RequireVault`
- `RequireVaultExplicit` only checks `g.Vault is not null`, no walking

---

## 7. Error Handling Strategy

| Error Scenario | Handling | User-Facing Message |
|---------------|----------|-------------------|
| No `.obsidian/` found anywhere | Return null from FindVault → RequireVault prints error | "No vault found. Create one with 'memctl init --vault \<path\>' or run from a directory containing a vault." |
| `init` without `--vault` | RequireVaultExplicit returns null | "--vault is required for this command" |
| `--vault` points to nonexistent path | Unchanged — operators handle this | Existing operator errors |

---

## 8. Security Considerations

- `Path.GetFullPath(dir)` canonicalizes the found path, preventing relative path issues
- No new I/O beyond `Directory.Exists()` per walk level
- Walk stops at filesystem root — no infinite loop possible

---

## 9. Performance Considerations

- Walk is O(depth) `Directory.Exists()` calls — each is a single syscall
- Typical project depth: 3-6 levels → negligible overhead
- No caching needed (vault path resolved once per command invocation)

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Unit | VaultLocator.FindVault with explicit path | — | 1 |
| Unit | VaultLocator.FindVault finds .obsidian in cwd | — | 1 |
| Unit | VaultLocator.FindVault finds .obsidian in parent | — | 1 |
| Unit | VaultLocator.FindVault returns null when not found | — | 1 |
| E2E | memctl search without --vault from vault dir | Smoke | 1 |
| E2E | memctl mcp without --vault from vault dir | Smoke | 1 |

### Key Test Cases

1. `FindVault(null)` from dir with `.obsidian/` → returns that dir
2. `FindVault(null)` from child of vault dir → returns parent vault dir
3. `FindVault(null)` from dir with no vault anywhere → returns null
4. `FindVault("/explicit/path")` → returns "/explicit/path" without walking

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command / Request | Expected Output | FR |
|----------|------------------|-----------------|-----|
| Search auto-detect | `memctl search "test"` (run from vault dir) | stdout contains `"success"`, exit 0 | FR-005 |
| MCP no vault arg | `memctl mcp` (run from vault dir, killed after 1s) | process starts without error output | FR-008 |
| No vault error | `memctl search "x"` (run from /tmp) | stdout contains "No vault found", exit non-zero | FR-007 |
| Explicit vault still works | `memctl search "x" --vault ./vault` | stdout contains `"success"`, exit 0 | FR-004 |

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| System.IO | Built-in | Directory.GetCurrentDirectory, GetParent, Exists | No |

---

## 12. Implementation Order

1. NEW: `src/memctl/Implementations/Config/VaultLocator.cs` — the core walk algorithm
2. MODIFY: `src/memctl/Bootstrap/Program.cs`
   - a. Replace `RequireVault` body to use `VaultLocator.FindVault`
   - b. Add `RequireVaultExplicit` local function after `RequireVault`
   - c. Change `initCmd.SetHandler` to use `RequireVaultExplicit`
3. RENAME: `docs/SKILL.md` → `docs/memctl.md` (git mv)
4. UPDATE: `docs/memctl.md` — `--vault` optional note, MCP example without `--vault`
5. Build + smoke test

---

## 13. Assumptions & Open Design Decisions

- [x] `status` command: currently passes `g.Vault ?? ""` — after change, auto-detect applies; StatusOperator handles empty vault gracefully already
- [x] `model` subcommands: don't use vault at all — no change needed
- [x] Walk starts at `Directory.GetCurrentDirectory()` not at the binary's location

---

## 14. Traceability Matrix

| Requirement | Design Section | Files | Test Cases |
|-------------|---------------|-------|------------|
| FR-001 | 5 | VaultLocator.cs | TC-001 to TC-004 |
| FR-002 | 5 | VaultLocator.cs | TC-001 |
| FR-003 | 5, 8 | VaultLocator.cs | TC-003 |
| FR-004 | 5 | VaultLocator.cs | TC-004 |
| FR-005 | 2 | Program.cs | Smoke-001 |
| FR-006 | 6 | Program.cs | Smoke init test |
| FR-007 | 7 | Program.cs | Smoke-003 |
| FR-008 | 2 | Program.cs | Smoke-002 |
| FR-010 | 2 | docs/memctl.md | FR-010 |
| FR-011 | 2 | docs/memctl.md | FR-011 |
| FR-012 | 2 | docs/memctl.md | FR-012 |
| NFR-002 | 8 | VaultLocator.cs | — |
| NFR-003 | 4 | Program.cs | Smoke-004 |
