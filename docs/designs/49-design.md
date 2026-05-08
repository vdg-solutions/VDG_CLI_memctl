# Design: Route vault notes to subdirs by tag (Task #49)

## Scope

`AddOperator.ExecuteAsync` only. `CaptureOperator`, `DistillOperator`, others — untouched.

## Root Cause

Two separate bugs in `AddOperator`:

1. Line 39: `FilePath = fileName ?? (SanitizeFileName(resolvedTitle) + ".md")` — ignores tags
2. Line 51: `vault.WriteNote(withEmbed, vaultPath, fileName)` — passes CLI `fileName`, not computed path

`ObsidianVaultReader.WriteNote` uses its `fileName` param directly (`Path.Combine(vaultPath, fileName)`), ignoring `note.FilePath`. Fixing only line 39 silently diverges disk path from index path.

## Changes

### AddOperator.cs

- Make `embedding` nullable (`GemmaEmbeddingEngine? embedding`) — consistent with `IngestOperator`
- Add `TagSubdirMap` readonly field at class top (RULE #9)
- Add `ResolveSubdir(string[]? tags)` private static — `OrdinalIgnoreCase` (CLI args bypass `ParseTags`)
- Compute `subdir` + `filePath` after `resolvedTags`, before note construction
- `note.FilePath = filePath`
- `vault.WriteNote(withEmbed, vaultPath, filePath)` ← pass `filePath`, not `fileName`
- Guard `embedding.Embed(...)` with null check

### Routing table

| Tags | Subdir |
|------|--------|
| golden-rule, anti-pattern, insight, dream-log | lessons |
| qc-rule, qc-error, qc-feedback | patterns |
| decisions, adr | decisions |
| session | chats |
| (unmatched / no tags) | vault root |

First match wins. `--file` present → skip routing entirely.

### SKILL.md

Add `Subdir` column to tag schema table.

### AddRoutingTests.cs (new)

Integration tests using real `ObsidianVaultReader` + `SqliteNoteIndex`. All use `embedding: null`.
Key assertion: `File.Exists(Path.Combine(_vaultPath, outcome.Data.FilePath))` — catches WriteNote/index divergence.

## No design artifacts needed

Spec is sufficient. Implementation follows spec directly.
