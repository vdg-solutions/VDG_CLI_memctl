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

`memctl add --tags "golden-rule"` writes the file to the vault root instead of the
appropriate semantic subdir. The tag schema (SKILL.md) declares routing intent but
`AddOperator.ExecuteAsync` ignores tags when computing `note.FilePath`.

## Root cause

`AddOperator.cs` line ~43:
```csharp
FilePath = fileName ?? (SanitizeFileName(resolvedTitle) + ".md"),
```
Tags are resolved by this point (`resolvedTags`) but never consulted for routing.
`fileName` is the explicit `--file` CLI arg — it must keep winning over tag routing.

## Proposed fix — insertion point

In `AddOperator.ExecuteAsync`, after resolving `resolvedTags` and before constructing
`note`, insert:

```csharp
var subdir = fileName is null ? ResolveSubdir(resolvedTags) : null;
var filePath = fileName
    ?? (subdir is not null
        ? Path.Combine(subdir, SanitizeFileName(resolvedTitle) + ".md")
        : SanitizeFileName(resolvedTitle) + ".md");
```

`ResolveSubdir` must be a private static method using a named constant map — no inline
magic strings (RULE #9). Define at class top:

```csharp
// First match wins. Tags not listed here (user-preference, qc-score, etc.) → vault root.
private static readonly (string[] Tags, string Subdir)[] TagSubdirMap =
[
    (["golden-rule", "anti-pattern", "insight", "dream-log"], "lessons"),
    (["qc-rule", "qc-error", "qc-feedback"],                  "patterns"),
    (["decisions", "adr"],                                     "decisions"),
    (["session"],                                              "chats"),
];
```

Priority when multiple routing tags are present: **first match wins** (map order above
is the priority order). `session` is lowest priority — a note tagged both `golden-rule`
and `session` routes to `lessons/`. Tags not in the map (`user-preference`, `qc-score`,
`task-{id}`, unrecognised tags) → vault root, no routing.

Subdir path is **relative to `vaultPath`** (same convention as all existing `WriteNote`
calls). No explicit `Directory.CreateDirectory` needed in `AddOperator` — `WriteNote`
in `ObsidianVaultReader` already calls `Directory.CreateDirectory(Path.GetDirectoryName(path)!)`
before writing. Do not add a redundant call.

`CaptureOperator`, `DistillOperator`, and all other operators are **out of scope** —
they use their own path logic and must not be changed by this task.

## Files

- `src/memctl/Operators/AddOperator.cs`
  - Add `TagSubdirMap` static readonly field at class top
  - Add `ResolveSubdir(string[]? tags)` private static method
  - Update `ExecuteAsync`: resolve `filePath` using `ResolveSubdir` before constructing `note`
- `plugins/memctl-claude/skills/memctl/SKILL.md`
  - Add `Subdir` column to tag schema table (SKILL.md sync rule — golden rule)
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
| AC-10 | `memctl add --tags golden-rule,session "..."` → file in `lessons/` (golden-rule wins) | `find vault/ -name "*.md" -path "*/lessons/*"` returns 1, `chats/` 0 |
| AC-11 | `memctl add --tags user-preference "..."` → file at vault root (unmapped tag → root) | `find vault/ -maxdepth 1 -name "*.md"` returns 1 result |
| AC-12 | `memctl add --file custom/note.md "..."` → file at `custom/note.md` (explicit wins) | `find vault/ -name "note.md"` |
| AC-13 | `memctl add "..."` (no tags) → file at vault root (no regression) | `find vault/ -maxdepth 1 -name "*.md"` returns 1 result |
| AC-14 | `memctl add --tags golden-rule "..."` on vault without pre-existing `lessons/` → subdir auto-created | init fresh vault, add note, dir exists |
| AC-15 | SKILL.md tag schema table has `Subdir` column with correct values for all 4 routing rows | `grep -c "lessons\|patterns\|decisions\|chats" SKILL.md` ≥ 4 |
| AC-16 | `AddRoutingTests.cs` unit tests pass covering AC-1, AC-9, AC-10, AC-11, AC-12, AC-13 | `dotnet test` passes |

## Out of scope

- CaptureOperator, DistillOperator, OrganizeOperator — do not touch
- Auto-migrating existing misplaced notes
- Auto-moving notes when tags change via `memctl append`
- `memctl add --file` path validation (separate concern)

## Performance

One linear scan of `TagSubdirMap` (≤5 entries) per `add` call. Negligible.
