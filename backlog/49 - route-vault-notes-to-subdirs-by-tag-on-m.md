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

`ResolveSubdir` must be a private static method with a named constant map — no inline
magic strings (RULE #9). Define the map as a static readonly field at class top:

```csharp
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
and `session` routes to `lessons/`.

Subdir path is **relative to `vaultPath`** (same convention as all existing `WriteNote`
calls). The operator must call `Directory.CreateDirectory(Path.Combine(vaultPath, subdir))`
before writing — the vault may have been init'd before this feature shipped.

`CaptureOperator`, `DistillOperator`, and all other operators are **out of scope** —
they use their own path logic and must not be changed by this task.

## Files

- `src/memctl/Operators/AddOperator.cs`
  - Add `TagSubdirMap` static field
  - Add `ResolveSubdir(string[]? tags)` private static method
  - Update `ExecuteAsync`: resolve `filePath` using `ResolveSubdir` before constructing `note`
  - Call `Directory.CreateDirectory` for subdir before `vault.WriteNote`
- `plugins/memctl-claude/skills/memctl/SKILL.md`
  - Add `Subdir` column to tag schema table (SKILL.md sync rule — golden rule)

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `memctl add --tags golden-rule "..."` → file in `lessons/` | `ls vault/lessons/` |
| AC-2 | `memctl add --tags anti-pattern "..."` → file in `lessons/` | `ls vault/lessons/` |
| AC-3 | `memctl add --tags insight "..."` → file in `lessons/` | `ls vault/lessons/` |
| AC-4 | `memctl add --tags dream-log "..."` → file in `lessons/` | `ls vault/lessons/` |
| AC-5 | `memctl add --tags qc-rule "..."` → file in `patterns/` | `ls vault/patterns/` |
| AC-6 | `memctl add --tags qc-error "..."` → file in `patterns/` | `ls vault/patterns/` |
| AC-7 | `memctl add --tags qc-feedback "..."` → file in `patterns/` | `ls vault/patterns/` |
| AC-8 | `memctl add --tags decisions "..."` → file in `decisions/` | `ls vault/decisions/` |
| AC-9 | `memctl add --tags session,task-42 "..."` → file in `chats/` | `ls vault/chats/` |
| AC-10 | `memctl add --tags golden-rule,session "..."` → file in `lessons/` (golden-rule wins) | `ls vault/lessons/` |
| AC-11 | `memctl add --file custom/note.md "..."` → file at `custom/note.md` (explicit wins) | path check |
| AC-12 | `memctl add "..."` (no tags) → file at vault root (no regression) | `ls vault/` |
| AC-13 | `memctl add --tags golden-rule "..."` on vault without pre-existing `lessons/` → subdir auto-created | init fresh vault, add note |
| AC-14 | SKILL.md tag schema table has `Subdir` column with correct values | `grep "Subdir" SKILL.md` |
| AC-15 | New unit tests in `AddRoutingTests.cs` cover AC-1, AC-9, AC-10, AC-11, AC-12, AC-13 | `dotnet test` passes |

## Out of scope

- CaptureOperator, DistillOperator, OrganizeOperator — do not touch
- Auto-migrating existing misplaced notes
- Auto-moving notes when tags change via `memctl append`
- `memctl add --file` path validation (separate concern)

## Performance

One linear scan of `TagSubdirMap` (≤5 entries) per `add` call. Negligible.
