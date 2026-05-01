# Requirements Spec: Add Importance Weight Field to Notes

**Task:** #4
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

Notes need a priority signal so AI agents (and the upcoming MCP server) can load the most important context first. This task adds two orthogonal signals to each note: a user-set `weight` (0.0–1.0 float) and an auto-tracked `access_count` (integer, incremented on `get`). Both are stored in the SQLite index and exposed in all note outputs. A new `weight` command lets users manually promote a note.

## 2. User Stories

- As a user, I want `memctl weight <id> 0.9` to mark a note as highly important, so the MCP server loads it in Layer 1 tiered context.
- As a user, I want the weight to be preserved when I re-ingest the vault, so I don't lose my manual annotations.
- As an agent consuming the MCP server, I want `GetAll()` to return notes sorted by effective importance (weight + access_count), so I can build tiered context without extra filtering.
- As a user, I want `memctl get <id>` to track that I accessed a note, so frequently-used notes naturally rise in importance.

## 3. Functional Requirements

### 3.1 Note Entity

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | `Note` entity has `float Weight` property (default 0.0) | Must | [unit] | `new Note()` has `Weight == 0.0f`; `Note` with `Weight = 0.8f` serializes/deserializes correctly |
| FR-002 | `Note` entity has `int AccessCount` property (default 0) | Must | [unit] | `new Note()` has `AccessCount == 0` |

### 3.2 SQLite Schema

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-003 | `notes` table has `weight REAL NOT NULL DEFAULT 0.0` column | Must | [unit] | `PRAGMA table_info(notes)` shows `weight` column with `dflt_value = 0.0` |
| FR-004 | `notes` table has `access_count INTEGER NOT NULL DEFAULT 0` column | Must | [unit] | `PRAGMA table_info(notes)` shows `access_count` column |
| FR-005 | Migration is safe on existing databases: columns added via `ALTER TABLE ADD COLUMN` if not already present | Must | [unit] | Running `ApplySchema()` twice on the same DB does not throw; existing rows gain default values |
| FR-006 | `Upsert` preserves existing `weight` and `access_count` on re-ingest | Must | [unit] | Set weight=0.9, re-ingest, weight still reads 0.9 (ON CONFLICT DO UPDATE excludes weight and access_count) |

### 3.3 `weight` Command

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-007 | `memctl weight <id> <value>` sets the note's weight | Must | [e2e] | `memctl weight {id} 0.8 --vault .` exits 0; subsequent `get` returns `weight: 0.8` |
| FR-008 | Weight value is clamped to [0.0, 1.0] | Must | [unit] | `weight {id} 1.5` stores 1.0; `weight {id} -0.5` stores 0.0 |
| FR-009 | `weight` command accepts note ID or file path (same as `get`) | Must | [e2e] | `memctl weight docs/specs/4-spec.md 0.5 --vault .` exits 0 |
| FR-010 | Note not found returns failure with exit code 1 | Must | [e2e] | `memctl weight nonexistent-id 0.5 --vault .` exits 1; output has `success: false` |
| FR-011 | Invalid value (non-numeric) returns failure with exit code 1 | Must | [e2e] | `memctl weight {id} "abc" --vault .` exits 1 with parse error message |

### 3.4 Access Count Tracking

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-012 | `get` command increments `access_count` by 1 after successful retrieval | Must | [unit] | Call `get` 3 times on same note; `access_count` = 3 |
| FR-013 | `get` on a non-existent note does NOT increment access_count | Must | [unit] | Failed `get` leaves access_count unchanged |

### 3.5 Output Exposure

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-014 | All note outputs (`get`, `search`, `search-semantic`, `search-text`, `list`) include `weight` and `access_count` fields | Must | [e2e] | `get` stdout JSON contains `"weight": 0.0` and `"access_count": 0` fields |
| FR-015 | `INoteIndex.GetAll()` returns notes sorted by `weight DESC, access_count DESC` | Must | [unit] | Notes with higher weight appear first; equal weight sorted by access_count |

### 3.6 INoteIndex Port

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-016 | `INoteIndex` has `void SetWeight(string noteId, float weight)` method | Must | [unit] | `index.SetWeight(id, 0.7f)` → `GetById(id).Weight == 0.7f` |
| FR-017 | `INoteIndex` has `void IncrementAccess(string noteId)` method | Must | [unit] | `index.IncrementAccess(id)` 3 times → `GetById(id).AccessCount == 3` |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Backward Compat | Existing vaults without weight/access_count columns continue to work | First use on old vault auto-migrates; no crash or data loss |
| NFR-002 | Data Preservation | Re-ingest does not reset user-set weight | `Upsert` ON CONFLICT excludes weight and access_count from UPDATE |
| NFR-003 | Range Safety | Weight stored as clamped float, never outside [0.0, 1.0] | Code review: clamping happens at SetWeight boundary |
| NFR-004 | Error Handling | Non-numeric weight value gives clear error | Output includes parse failure reason, not exception stack trace |

## 5. Edge Cases & Error Scenarios

1. **Existing vault without columns**: `ApplySchema()` runs `ALTER TABLE notes ADD COLUMN weight ...` — must be idempotent. Check with `PRAGMA table_info` before `ALTER TABLE` to avoid "duplicate column" error.
2. **Re-ingest resets weight**: `Upsert` uses `ON CONFLICT DO UPDATE SET` which must NOT include `weight` or `access_count` in the update clause. Currently it updates all columns — fix required.
3. **Weight 0.0 vs NULL**: store as `REAL NOT NULL DEFAULT 0.0` — no NULL handling needed; simplifies queries.
4. **access_count overflow**: `INTEGER` in SQLite is 64-bit; no practical risk.
5. **weight command on note not yet in index**: trigger auto-ingest first (consistent with other commands), then set weight.
6. **Concurrent access_count increments**: CLI is single-process; no concurrency concern.
7. **Float precision**: weight `0.7` stored as REAL may read back as `0.699999988` — display should round to 2 decimal places or use `Math.Round`.

## 6. Out of Scope

- Automatic weight decay over time
- Weight computed from access_count (that's MCP Layer 1 logic in task #3)
- Weight-based search ranking boost (future feature)
- Bulk weight assignment
- Weight visible in Obsidian frontmatter (vault files unchanged)

## 7. Dependencies

- `Note.cs` — add two properties
- `INoteIndex.cs` — add two methods
- `SqliteNoteIndex.cs` — implement migration, update Upsert, implement new methods, update ReadNote
- `GetOperator.cs` — call IncrementAccess after successful retrieval; expose weight/access_count in output
- `GetOperator.NoteToData` — add weight and access_count to output (used by all search commands)
- New `WeightOperator.cs` + Program.cs wiring for `weight` command

## 8. Open Questions

- None — design is clear from task description and codebase analysis.

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001: `Note.Weight` defaults to 0.0f, serializes correctly
- [ ] FR-002: `Note.AccessCount` defaults to 0
- [ ] FR-003: `weight` column present in schema with DEFAULT 0.0
- [ ] FR-004: `access_count` column present in schema with DEFAULT 0
- [ ] FR-005: `ApplySchema()` idempotent on existing DB (no exception on re-run)
- [ ] FR-006: Upsert ON CONFLICT does NOT overwrite weight or access_count
- [ ] FR-007: `memctl weight {id} 0.8` exits 0; `get` shows weight: 0.8
- [ ] FR-008: weight 1.5 → stored as 1.0; weight -0.5 → stored as 0.0
- [ ] FR-009: `weight` accepts file path as well as ID
- [ ] FR-010: `weight nonexistent-id` exits 1, success: false
- [ ] FR-011: `weight {id} abc` exits 1 with clear error
- [ ] FR-012: get same note 3x → access_count = 3
- [ ] FR-013: failed get does not increment access_count
- [ ] FR-014: get/search output includes weight and access_count fields
- [ ] FR-015: GetAll() sorted weight DESC, access_count DESC
- [ ] FR-016: SetWeight persists correctly
- [ ] FR-017: IncrementAccess persists correctly
- [ ] NFR-001: Old vault auto-migrates on first use
- [ ] NFR-002: Re-ingest preserves weight/access_count
