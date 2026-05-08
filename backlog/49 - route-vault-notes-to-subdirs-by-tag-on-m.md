---
id: 49
type: task
title: Route vault notes to subdirs by tag on memctl add
status: Todo
priority: normal
created: 2026-05-08
updated: 2026-05-08
---

## Description

`memctl add --tags "golden-rule"` writes the file to vault root instead of the
appropriate semantic subdir. The tag schema (SKILL.md) declares routing intent but
`AddOperator.ExecuteAsync` ignores tags when computing file path.

## Root cause

Two separate issues in `AddOperator.ExecuteAsync`:

1. **Line ~43** — `FilePath` ignores tags:
   ```csharp
   FilePath = fileName ?? (SanitizeFileName(resolvedTitle) + ".md"),
   ```

2. **Line 51** — `vault.WriteNote` receives the original CLI `fileName`, not the routed path:
   ```csharp
   vault.WriteNote(withEmbed, vaultPath, fileName);
   ```
   `WriteNote` uses its `fileName` parameter to build the disk path (`Path.Combine(vaultPath, fileName)`),
   ignoring `note.FilePath` entirely. Changing only `note.FilePath` without updating this call
   causes disk path and index path to diverge silently.

## Fix — both lines must change

After resolving `resolvedTags`, before constructing `note`:

```csharp
var subdir   = fileName is null ? ResolveSubdir(resolvedTags) : null;
var filePath = fileName
    ?? (subdir is not null
        ? Path.Combine(subdir, SanitizeFileName(resolvedTitle) + ".md")
        : SanitizeFileName(resolvedTitle) + ".md");
```

In the `note` initializer: `FilePath = filePath`

Line 51 must become: `vault.WriteNote(withEmbed, vaultPath, filePath)`
(pass `filePath`, not `fileName`, so disk path matches what the index stores)

### ResolveSubdir

```csharp
// First match wins. null or unrecognised tags → null (vault root).
private static readonly (string[] Tags, string Subdir)[] TagSubdirMap =
[
    (["golden-rule", "anti-pattern", "insight", "dream-log"], "lessons"),
    (["qc-rule", "qc-error", "qc-feedback"],                  "patterns"),
    (["decisions", "adr"],                                     "decisions"),
    (["session"],                                              "chats"),
];

private static string? ResolveSubdir(string[]? tags)
{
    if (tags is null or { Length: 0 }) return null;
    foreach (var (routeTags, subdir) in TagSubdirMap)
        if (routeTags.Any(rt => tags.Any(t => string.Equals(t, rt, StringComparison.OrdinalIgnoreCase))))
            return subdir;
    return null;
}
```

`StringComparison.OrdinalIgnoreCase` is required — CLI `--tags` args bypass `ParseTags`
and arrive with their original user-supplied casing.

`--file` path (when provided) is relative to `vaultPath`, matching the existing `WriteNote`
convention (`Path.Combine(vaultPath, fileName)` on line 93 of `ObsidianVaultReader`).
Routing is skipped entirely when `--file` is present.

`CaptureOperator`, `DistillOperator`, and all other operators are **out of scope**.

## Files

- `src/memctl/Operators/AddOperator.cs`
  - Add `TagSubdirMap` static readonly field at class top (RULE #9 — named constant)
  - Add `ResolveSubdir(string[]? tags)` private static method with `OrdinalIgnoreCase`
  - Line ~43: set `FilePath = filePath` (computed above)
  - Line 51: change `vault.WriteNote(withEmbed, vaultPath, fileName)` → `vault.WriteNote(withEmbed, vaultPath, filePath)`
- `plugins/memctl-claude/skills/memctl/SKILL.md` — add `Subdir` column to tag schema table
- `tests/memctl.Tests/Operators/AddRoutingTests.cs` — new file

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `memctl add --tags golden-rule "..."` → file in `lessons/` | `find vault/ -name "*.md" -path "*/lessons/*"` returns 1 result |
| AC-2 | `memctl add --tags anti-pattern "..."` → file in `lessons/` | same |
| AC-3 | `memctl add --tags insight "..."` → file in `lessons/` | same |
| AC-4 | `memctl add --tags dream-log "..."` → file in `lessons/` | same |
| AC-5 | `memctl add --tags qc-rule "..."` → file in `patterns/` | `find vault/ -name "*.md" -path "*/patterns/*"` returns 1 result |
| AC-6 | `memctl add --tags qc-error "..."` → file in `patterns/` | same |
| AC-7 | `memctl add --tags qc-feedback "..."` → file in `patterns/` | same |
| AC-8 | `memctl add --tags decisions "..."` → file in `decisions/` | `find vault/ -name "*.md" -path "*/decisions/*"` returns 1 result |
| AC-9 | `memctl add --tags session,task-42 "..."` → file in `chats/` | `find vault/ -name "*.md" -path "*/chats/*"` returns 1 result |
| AC-10 | `memctl add --tags golden-rule,session "..."` → file in `lessons/` (first match wins) | `find vault/ -name "*.md" -path "*/lessons/*"` = 1, `chats/` = 0 |
| AC-11 | `memctl add --tags Golden-Rule "..."` → file in `lessons/` (case-insensitive match) | `find vault/ -name "*.md" -path "*/lessons/*"` returns 1 result |
| AC-12 | `memctl add --tags user-preference "..."` → file at vault root (unmapped tag) | `find vault/ -maxdepth 1 -name "*.md"` returns 1 result |
| AC-13 | `memctl add "..."` (no tags) → file at vault root (no regression) | `find vault/ -maxdepth 1 -name "*.md"` returns 1 result |
| AC-14 | `memctl add --file custom/note.md "..."` → file at `vaultPath/custom/note.md` (explicit wins, path relative to vault) | `find vault/ -name "note.md"` |
| AC-15 | Unit test: after `ExecuteAsync(..., tags: ["golden-rule"], ...)`, `File.Exists(Path.Combine(vaultPath, outcome.Data.FilePath))` is true — catches WriteNote/index mismatch | `AddRoutingTests.cs` assertion |
| AC-16 | `memctl add --tags golden-rule "..."` on vault without pre-existing `lessons/` → subdir auto-created | init fresh vault, add note, dir exists |
| AC-17 | SKILL.md tag schema table has `Subdir` column with correct values for all 4 routing rows | `grep "Subdir" SKILL.md` |
| AC-18 | Unit tests in `tests/memctl.Tests/Operators/AddRoutingTests.cs` cover AC-1, AC-9, AC-10, AC-11, AC-12, AC-13, AC-15 | `dotnet test` passes |

## Out of scope

- CaptureOperator, DistillOperator, OrganizeOperator — do not touch
- Auto-migrating existing misplaced notes
- Auto-moving notes when tags change via `memctl append`
- `memctl add --file` path validation

## Performance

One linear scan of `TagSubdirMap` (≤5 entries) per `add` call. Negligible.
