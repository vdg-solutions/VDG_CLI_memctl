# Requirements Spec: Prune stale DB entries on ingest when vault files are deleted

**Task:** 34
**Date:** 2026-05-04
**Status:** Approved (extracted from backlog/34)

## 1. Overview

`IngestOperator.Execute()` only upserts — it loops over all `.md` files on disk and calls `index.Upsert()` per file. Deleted files stay in the index forever. Result: stale session notes, dead patterns, old QC scores persist in `context-inject` output, wasting Claude Code context window tokens.

Add a prune phase before the upsert loop: diff index entries vs actual disk files, delete unmatched rows. `INoteIndex.Delete()` already exists and is tested.

## 2. User Stories

- As a memctl user who deletes stale session notes, I want `memctl ingest` to remove them from search results so I don't see dead data in `memctl list` or Claude Code prompts.
- As a Claude Code user with auto-capture enabled, I want old captured sessions that I manually cleaned to stop appearing in `## Memory Context` injections so my context window isn't wasted.

## 3. Functional Requirements

| ID | Requirement | Priority | Test | Acceptance |
|----|------------|----------|------|------------|
| FR-1 | Deleting a vault `.md` file then running `memctl ingest` removes its index entry | Must | [unit] | `memctl list` excludes deleted note; `memctl stats` note_count decrements |
| FR-2 | `context-inject` does not return deleted notes | Must | [unit] | `echo "test prune" \| memctl context-inject` → no deleted note in output |
| FR-3 | Ingest of unchanged vault is a no-op (pruned=0, no false positives) | Must | [unit] | Run ingest twice → second run shows `"pruned": 0` |
| FR-4 | Prune count appears in `IngestReport` and CLI output | Must | [unit] | `memctl ingest` JSON shows `"pruned": N`; N>0 when files were deleted |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance |
|----|----------|------------|-----------|
| NFR-1 | Performance | Prune adds < 50ms on 1000-note vault | `SELECT id, file_path` is O(n) scan, no joins; `HashSet` lookup O(1) per entry |
| NFR-2 | Regression | Full test suite passes including new prune tests | `dotnet test` → 60+/60+ pass (57 baseline + ≥3 new); 0 warnings |

## 5. Edge Cases

1. **Windows path separator**: `Path.GetRelativePath` returns `\` on Windows. Normalize with `.Replace('\\', '/')` to match DB format. Use `StringComparer.OrdinalIgnoreCase` for case-insensitive comparison.
2. **Deleted + recreated same path before next ingest**: File deleted → prune removes entry → external tool creates new `.md` at same path → next ingest upserts it back. Briefly absent then restored — correct behavior.
3. **Large vault (10k notes)**: `SELECT id, file_path` returns all rows. No joins, O(n) scan. < 10ms for 10k rows. Add LIMIT if profiling shows regression.
4. **Concurrent writer**: Ingest is single-threaded per vault. No concurrent writes in current architecture. No race condition.
5. **Empty vault / first ingest**: `index.GetAllFilePaths()` returns empty list → prune loop skips → pruned=0.

## 6. Out of Scope

- No separate `memctl prune` subcommand — prune is always coupled to ingest.
- No recursive orphan link cleanup — pruned notes' backlinks in other notes remain. Scope creep.

## 7. Dependencies

- Blocked by: none
- `INoteIndex.Delete(string noteId)` — already exists, field-tested
- `IVaultReader.EnumerateMarkdownFiles(string vaultPath)` — already exists

## 8. Open Questions

(none)

## 9. QC Checklist

- [ ] FR-1: `memctl list` excludes deleted note after ingest
- [ ] FR-2: `context-inject` output has no deleted note
- [ ] FR-3: Second ingest shows `pruned=0` on unchanged vault
- [ ] FR-4: `"pruned": N` appears in JSON output
- [ ] NFR-1: Prune loop completes in acceptable time on test vault
- [ ] NFR-2: `dotnet test` → all pass, 0 warnings
- [ ] Contract rule: `INoteIndex.Delete()` called (not raw SQL DELETE)
- [ ] Pattern rule: `Path.GetRelativePath().Replace('\\', '/')` for cross-platform
- [ ] Edge case: empty vault first ingest → pruned=0
- [ ] Edge case: no deleted files → pruned=0
