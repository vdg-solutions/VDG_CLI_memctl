# Requirements Spec: General Event Logging System (EventLog)

**Task:** 52
**Date:** 2026-05-08
**Status:** Draft

---

## 1. Overview

`EventLog` is a structured, vault-native audit trail for memctl operations. It already exists (`EventLog.cs`) and writes to `events/` in the vault with rich frontmatter (type, severity, source, payload, timestamp). However it is only wired into 4 of the 11 write-path operators. This task completes the wiring, fixes a pre-existing bug where archived event notes leak into BM25 search after ingest, and preserves `HookLog` for backward compatibility with `hook-status`.

## 2. User Stories

- As a memctl user, I want every write operation (add, delete, weight, decay, organize, migrate-tags, distill) to produce an event note, so that I can audit what changed and when.
- As a user running `memctl search`, I want search results to exclude internal event notes even after `memctl ingest`, so that audit trail noise does not pollute my memory queries.
- As a user running `memctl hook-status`, I want the command to keep working unchanged after this feature lands.

## 3. Functional Requirements

### 3.1 EventLog Wiring — Write-Path Operators

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | `AddOperator.ExecuteAsync` calls `EventLog.Record` on success | Must | [unit] | After `AddOperator.ExecuteAsync`, an event file exists in `events/` with `source: add`, `type: operator_run`, `severity: info` |
| FR-002 | `DeleteOperator.Execute` calls `EventLog.Record` on success | Must | [unit] | After successful delete, an event file exists in `events/` with `source: delete` |
| FR-003 | `WeightOperator.Execute` calls `EventLog.Record` on success | Must | [unit] | After weight set, an event file exists in `events/` with `source: weight` |
| FR-004 | `DecayOperator.Execute` calls `EventLog.Record` on non-dryRun success | Must | [unit] | After non-dryRun decay, an event file exists in `events/` with `source: decay`; dryRun produces no event file |
| FR-005 | `OrganizeOperator.ExecuteAsync` calls `EventLog.Record` on completion | Must | [unit] | After organize completes, an event file exists in `events/` with `source: organize` |
| FR-006 | `MigrateTagsOperator.Execute` calls `EventLog.Record` on non-dryRun | Must | [unit] | After non-dryRun migrate-tags, event file with `source: migrate-tags`; dryRun produces no event |
| FR-007 | `DistillOperator.ExecuteAsync` calls `EventLog.Record` on success path (line ~99) | Must | [unit] | After successful distill, event file with `source: distill`, `severity: info` |

### 3.2 SearchBm25 Archived Filter Fix

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-008 | `SqliteNoteIndex.SearchBm25` excludes archived notes | Must | [unit] | A note inserted with `archived=1` does NOT appear in `SearchBm25` results; a note with `archived=0` DOES appear |
| FR-009 | `SearchBm25` fix is consistent with `GetAll(includeArchived: false)` | Must | [unit] | Both `GetAll()` and `SearchBm25` return the same set of non-archived notes for identical vault content |

### 3.3 HookLog Preservation

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-010 | `HookLog.cs` and `HookStatusOperator` unchanged | Must | [unit] | `memctl hook-status` command still reads `hook.log` and returns results; no regression |
| FR-011 | No double-logging at Program.cs capture hook sites (lines 740, 771) | Must | [unit] | Each capture operation produces exactly one event note in `events/` (CaptureOperator handles it internally; Program.cs does not add a second call) |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Error Handling | `EventLog.Record` remains best-effort — must never throw to callers | Unit test: when vault path is invalid/unwritable, `EventLog.Record` does not propagate exception |
| NFR-002 | Persistence | Event notes are disk-only — no `index.Upsert` in `EventLog.Record` | `SqliteNoteIndex` contains zero rows for event notes immediately after `EventLog.Record` (before any ingest) |
| NFR-003 | Stability | `EventLog.Record` signature unchanged | `EventLog.Record(vaultPath, type, severity, source, payload, conversationId?)` — no new parameters added |
| NFR-004 | Search Integrity | After `memctl ingest`, event notes do NOT appear in `memctl search` | `SearchBm25("operator_run")` returns 0 results when vault contains only event notes (archived=true) |

## 5. Edge Cases & Error Scenarios

1. **dryRun operators**: `DecayOperator` and `MigrateTagsOperator` take a `dryRun` flag. Expected: EventLog call only on `!dryRun` — no event noise from dry runs.
2. **Vault path unwritable**: If `events/` subdirectory cannot be created. Expected: `EventLog.Record` swallows the exception (best-effort), operator proceeds normally.
3. **Double-logging guard**: `CaptureOperator` already calls `EventLog.Record` internally; `Program.cs` hook handler also calls `HookLog.Record` at lines 740/771. Expected: no second `EventLog.Record` call added at those Program.cs lines.
4. **Distill no-op**: `DistillOperator` returns early with "no conversations to distill" (line 29). Expected: no EventLog call for no-op (nothing changed).
5. **Archived notes in FTS**: FTS virtual table (`notes_fts`) has no `archived` column — filter must be on the joined `notes` table, not on FTS directly.
6. **Concurrent ingest + search**: After fixing SearchBm25, a race between ingest (inserting archived event notes) and search should not surface event notes due to `archived=1` filter.

## 6. Out of Scope

- Retiring `HookLog` — deferred to a future task; `hook-status` must keep working.
- Adding EventLog to read-only operators (`Search*`, `List`, `Get`, `Tags`, `Stats`, `Grep`, `Status`, `Lint`).
- Adding `memctl events` command to query event history.
- Adding `EventLog.Record` calls at Program.cs hook handler sites (lines 630/638/740/771) — operators handle this internally.
- Changing `EventLog.Record` signature or frontmatter schema.

## 7. Dependencies

- `EventLog.cs` — already exists, no changes to signature or schema.
- `SqliteNoteIndex.cs` — SearchBm25 SQL fix at line ~92.
- `HookLog.cs` / `HookStatusOperator.cs` — no changes.
- All 7 write-path operator files: `AddOperator.cs`, `DeleteOperator.cs`, `WeightOperator.cs`, `DecayOperator.cs`, `OrganizeOperator.cs`, `MigrateTagsOperator.cs`, `DistillOperator.cs`.

## 8. Open Questions

- None. Design brief from `/autoresearch optimize 52` (10/10 score) resolved all ambiguities.

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001: `events/` contains a file with `source: add` after `AddOperator.ExecuteAsync` completes
- [ ] FR-002: `events/` contains a file with `source: delete` after `DeleteOperator.Execute` completes
- [ ] FR-003: `events/` contains a file with `source: weight` after `WeightOperator.Execute` completes
- [ ] FR-004: `events/` contains a file with `source: decay` after non-dryRun decay; no file on dryRun
- [ ] FR-005: `events/` contains a file with `source: organize` after `OrganizeOperator.ExecuteAsync`
- [ ] FR-006: `events/` contains a file with `source: migrate-tags` after non-dryRun; no file on dryRun
- [ ] FR-007: `events/` contains a file with `source: distill` after successful distill (not on no-op)
- [ ] FR-008: `SearchBm25` returns 0 results for a note with `archived=1` in the DB
- [ ] FR-009: `GetAll()` and `SearchBm25` return consistent non-archived result sets
- [ ] FR-010: `HookStatusOperator` passes existing tests unchanged
- [ ] FR-011: Exactly one event file created per `CaptureOperator` run (no double from Program.cs)
- [ ] NFR-001: `EventLog.Record` with invalid vault path does not throw
- [ ] NFR-002: No rows in index immediately after `EventLog.Record` (disk-only)
- [ ] NFR-004: `SearchBm25` returns 0 archived event notes after `memctl ingest`
