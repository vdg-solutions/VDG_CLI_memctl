# Requirements Spec: G5 Temporal Decay — memctl decay

**Task:** 10
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Overview

Old notes accumulate in the vault and compete equally with fresh notes in `list` and search ranking. The `decay` command addresses this by periodically reducing the weight of stale notes — those not recently modified and not recently boosted by the user. Notes that have been manually prioritized (weight > 1.0) or recently weight-set are protected from full decay. Notes that decay below a threshold are archived and hidden from `list` by default, keeping the vault focused on current work rather than historical noise.

The command is designed to run periodically (e.g. weekly via scheduler) or on demand. It is idempotent within the same day.

---

## 2. User Stories

- As a developer, I want stale notes to naturally fade from `list` output so that my working context stays relevant.
- As a developer, I want manually boosted notes to decay much slower so that intentional prioritization is preserved.
- As a developer, I want to preview what would decay before committing, via `--dry-run`.
- As a developer, I want archived notes to remain searchable so that old knowledge is not lost, only deprioritized.
- As a developer, I want to tune the decay aggressiveness via `--decay-factor` and `--days` so that different vaults can have different retention profiles.

---

## 3. Functional Requirements

### 3.1 New `decay` Command

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-001 | Add `memctl decay --days <N>` subcommand | Must | [e2e] | `memctl decay --vault ./vault --days 30` exits 0 and prints JSON report |
| FR-002 | `--days` option required, integer, no default | Must | [e2e] | Omitting `--days` prints error and exits non-zero |
| FR-003 | `--decay-factor <float>` option, default 0.9 | Must | [unit] | When omitted, factor 0.9 is used; when provided, that value is used |
| FR-004 | `--dry-run` flag — compute and report without writing | Must | [e2e] | `--dry-run` produces identical JSON structure but no index rows are modified |
| FR-005 | Vault auto-detection via `RequireVault` | Must | [e2e] | `--vault` optional; auto-detected from cwd same as other commands |

### 3.2 Decay Algorithm

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-010 | Scan all notes in index including already-archived | Must | [unit] | Already-archived notes are counted in `already_archived` field of report |
| FR-011 | Compute `days_since_modified = (UtcNow - note.Modified).TotalDays` | Must | [unit] | Uses `Modified` timestamp from index, UTC |
| FR-012 | Apply decay only when `days_since_modified > --days` AND `days_since_last_weight_set > --days` | Must | [unit] | Note modified 5 days ago with `--days 30` → unchanged; note with `LastWeightSet` 5 days ago with `--days 30` → unchanged |
| FR-013 | Normal tier (weight ≤ 1.0): `new_weight = weight * decay_factor` | Must | [unit] | weight=1.0, factor=0.9 → new_weight=0.9 |
| FR-014 | Protected tier (weight > 1.0): `new_weight = weight * pow(decay_factor, 1.0/3)` | Must | [unit] | weight=1.5, factor=0.9 → new_weight=1.5 * pow(0.9, 1/3) ≈ 1.447 |
| FR-015 | Archive when `new_weight < 0.05`: set `archived = 1` | Must | [unit] | weight=0.049 after decay → archived flag set |
| FR-016 | Already-archived notes (archived=1): skip decay, increment `already_archived` counter | Must | [unit] | Note with archived=1 → weight unchanged, counted in already_archived |
| FR-017 | Upsert weight and archived flag into index for decayed notes | Must | [unit] | After decay run, `GetById` returns updated weight; archived notes have archived=1 |
| FR-018 | Idempotent: running twice on the same day produces same result | Must | [unit] | Second run on same day → same JSON report values; weights do not double-decay |

### 3.3 JSON Report Output

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-020 | Output `MemctlOutcome.Ok` JSON with action `"decay"` | Must | [e2e] | `success: true, action: "decay"` in output |
| FR-021 | Report `data` contains: `decayed`, `archived`, `unchanged`, `already_archived` integer fields | Must | [e2e] | `{"decayed":N,"archived":M,"unchanged":K,"already_archived":P}` |
| FR-022 | `decayed` = count of notes whose weight was reduced (includes those that became archived) | Must | [unit] | Notes that had weight updated → counted in decayed |
| FR-023 | `archived` = count of notes newly set to archived=1 in this run | Must | [unit] | Notes that crossed weight < 0.05 threshold → counted in archived |
| FR-024 | `unchanged` = count of notes that were eligible but weight was not changed (modifier or weight-set too recent, or weight=0 already) | Must | [unit] | Notes skipped by recency guard → counted in unchanged |
| FR-025 | `already_archived` = count of notes that were archived=1 before this run | Must | [unit] | Pre-archived notes counted separately |

### 3.4 Schema Changes — `Note` Entity

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-030 | Add `Archived` property to `Note`: `public bool Archived { get; init; } = false` | Must | [unit] | Note record has Archived field; default false |
| FR-031 | Add `LastWeightSet` property to `Note`: `public DateTime? LastWeightSet { get; init; }` | Must | [unit] | Note record has LastWeightSet field; default null (never set) |

### 3.5 Schema Changes — SQLite Index

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-040 | Add `archived INTEGER NOT NULL DEFAULT 0` column via `MigrateAddColumn` | Must | [unit] | Existing databases gain column without data loss |
| FR-041 | Add `last_weight_set TEXT` column (nullable ISO 8601) via `MigrateAddColumn` | Must | [unit] | Column added idempotently; null for notes never manually weighted |
| FR-042 | `ReadNote` maps `archived` column to `Note.Archived` | Must | [unit] | `GetById` returns note with correct `Archived` value |
| FR-043 | `ReadNote` maps `last_weight_set` column to `Note.LastWeightSet` | Must | [unit] | `GetById` returns note with correct `LastWeightSet` value |
| FR-044 | `SetWeight` updates `last_weight_set = UtcNow` when called | Must | [unit] | After `weight` command, `GetById` returns `LastWeightSet` ≈ now |
| FR-045 | `GetAll()` filters archived=0 by default | Must | [unit] | `GetAll()` returns only non-archived notes |
| FR-046 | Add `GetAll(bool includeArchived)` overload or parameter | Must | [unit] | `GetAll(includeArchived: true)` returns all notes; `GetAll()` / `GetAll(false)` excludes archived |
| FR-047 | `INoteIndex` interface updated: `GetAll()` and `GetAll(bool includeArchived)` | Must | [unit] | Interface contract reflects both overloads |

### 3.6 `DecayOperator`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-050 | New `DecayOperator(IVaultReader, INoteIndex)` in `Memctl.Operators` namespace | Must | [unit] | Class follows same constructor pattern as `ListOperator`, `WeightOperator` |
| FR-051 | `Execute(string vaultPath, int days, float decayFactor, bool dryRun): MemctlOutcome` | Must | [unit] | Method signature matches; returns MemctlOutcome |
| FR-052 | Auto-ingest if needed via `IngestOperator.NeedsIngest` | Must | [unit] | First run on fresh vault does not fail |
| FR-053 | Uses `GetAll(includeArchived: true)` to scan all notes | Must | [unit] | Already-archived notes are included in scan |

### 3.7 `list` Command Updates

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-060 | `list` excludes archived notes by default (via updated `GetAll()`) | Must | [e2e] | After decay archives a note, it no longer appears in `memctl list` |
| FR-061 | Add `--include-archived` flag to `list` command | Must | [e2e] | `memctl list --include-archived` shows all notes including archived |
| FR-062 | `ListOperator.Execute` accepts `includeArchived` bool parameter | Must | [unit] | Signature: `Execute(string vaultPath, string? tag, int limit, bool includeArchived = false)` |

### 3.8 Search Commands — Archived Notes Visibility

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-070 | `search`, `search-semantic`, `search-text`, `search-date`, `grep` include archived notes | Must | [e2e] | Archived note matching query appears in search results |
| FR-071 | `SearchBm25`, `SearchSemantic`, `SearchByTags`, `SearchByDate` do NOT filter on archived | Must | [unit] | No `WHERE archived = 0` in search queries |

### 3.9 `SetWeight` Range

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|-------------|----------|------------|---------------------|
| FR-080 | `SetWeight` clamps to [0.0, 2.0] (was [0.0, 1.0] per WeightOperator) | Must | [unit] | `SetWeight(id, 2.5f)` stores 2.0; `SetWeight(id, 1.5f)` stores 1.5 |
| FR-081 | `weight` command help text updated to "0.0–2.0" | Should | [e2e] | `memctl weight --help` shows range 0.0–2.0 |

---

## 4. Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | Schema migration is idempotent — `MigrateAddColumn` pattern, safe on existing databases |
| NFR-002 | `decay` on a vault with 10,000 notes completes in under 5 seconds (single SQLite write transaction) |
| NFR-003 | All decay writes wrapped in a single SQLite transaction for atomicity |
| NFR-004 | `--dry-run` must not open any write transactions |
| NFR-005 | Floating-point decay computed with `float` precision, consistent with existing `Weight` field type |

---

## 5. Edge Cases & Error Scenarios

| Scenario | Expected Behavior |
|----------|-------------------|
| Vault not found | `MemctlOutcome.Fail("decay", "No vault found...")` exit 1 |
| `--days 0` | All notes with `LastWeightSet` null are eligible; functionally valid |
| `--decay-factor 0.0` | Weight → 0.0; note archived immediately if below 0.05 |
| `--decay-factor 1.0` | Weight unchanged for normal tier; no-op decay |
| `--decay-factor > 1.0` | Weight increases — valid input, not clamped; operator's responsibility to pass valid value |
| Note with `weight = 0.0` | Already at floor; skip (no change), count as unchanged |
| Note with `LastWeightSet = null` | Treated as "never set" — no recency protection; eligible for decay if modified > N days ago |
| Note with `Modified` in the future (clock skew) | `days_since_modified` will be negative → not eligible; counts as unchanged |
| Empty vault (0 notes) | Returns `{decayed:0, archived:0, unchanged:0, already_archived:0}` with exit 0 |
| `--dry-run` with `--days 30` | JSON report identical to live run but no index mutations |
| Running decay twice same day | Second run: previously decayed weights are further reduced by same factor — **not** idempotent by calendar day unless implementation tracks last_decay_date; see Open Questions |

---

## 6. Out of Scope

- Automatic scheduled decay (cron integration) — operator must invoke manually or via external scheduler
- Unarchiving / restoring archived notes — no `decay --unarchive` in this task
- Per-tag or per-folder decay rate overrides
- Decay of notes based on `access_count` (access-based retention is future work)
- Surfacing `archived` field in `NoteDto` / search result output
- Bulk un-archive command

---

## 7. Dependencies

| Dependency | Notes |
|------------|-------|
| `Note.cs` — add `Archived`, `LastWeightSet` fields | Affects all callers of `ReadNote` |
| `INoteIndex.cs` — `GetAll(bool includeArchived)` overload | `ListOperator` must pass flag; all other callers use default |
| `SqliteNoteIndex.cs` — schema migration, `SetWeight` update, `GetAll` filter | Core index change |
| `Program.cs` — register `decay` subcommand, add `--include-archived` to `list` | Bootstrap wiring |
| `WeightOperator.cs` — clamp range change from 1.0 to 2.0 | Breaking change for callers relying on [0,1] range |

---

## 8. Open Questions

| # | Question | Impact |
|---|----------|--------|
| Q1 | Should idempotency be enforced by storing a `last_decay_date` in `metadata` table, preventing double-decay on same calendar day? The task says "idempotent: running twice same day → same result" but the algorithm as described would decay twice. | Determines whether `metadata` tracking is required |
| Q2 | Should `already_archived` skip `weight` decay entirely (current spec), or should already-archived notes continue to have weight lowered toward 0 so they eventually approach absolute zero? | Minor — affects weight value for archived notes only |
| Q3 | `WeightOperator` currently clamps to 1.0 but FR-080 requires 2.0. Should the `weight` command's argument description also change, or should the CLI guard and the index-level `SetWeight` clamp separately? | Ensures weight command and decay protected tier are consistent |
| Q4 | Should `search-tags` and `search-links` also include archived notes (like other search commands)? The backlog says "all search commands" but `search-tags` and `search-links` are not listed explicitly. | Scope of FR-071 |
| Q5 | `--decay-factor` flag: should it validate range [0.0, 1.0] and reject values outside, or allow > 1.0 (weight inflation)? | Input validation behavior |

---

## 9. QC Checklist

- [ ] `Note.cs` — `Archived: bool`, `LastWeightSet: DateTime?` added; record still compiles
- [ ] `INoteIndex.cs` — `GetAll(bool includeArchived = false)` replaces or overloads `GetAll()`
- [ ] `SqliteNoteIndex.cs` — `archived` and `last_weight_set` columns added via `MigrateAddColumn`
- [ ] `SqliteNoteIndex.cs` — `ReadNote` maps both new columns correctly (handle DBNull for last_weight_set)
- [ ] `SqliteNoteIndex.cs` — `GetAll()` / `GetAll(false)` filters `WHERE archived = 0`
- [ ] `SqliteNoteIndex.cs` — `GetAll(true)` returns all notes
- [ ] `SqliteNoteIndex.cs` — `SetWeight` also writes `last_weight_set = UtcNow`
- [ ] `SqliteNoteIndex.cs` — search methods (`SearchBm25`, `SearchSemantic`, `SearchByTags`, `SearchByDate`) do NOT add archived filter
- [ ] `DecayOperator.cs` — new file in `Memctl.Operators`, constructor `(IVaultReader, INoteIndex)`
- [ ] `DecayOperator.cs` — normal tier decay formula correct: `weight * decayFactor`
- [ ] `DecayOperator.cs` — protected tier formula correct: `weight * MathF.Pow(decayFactor, 1f/3f)`
- [ ] `DecayOperator.cs` — archive threshold: `newWeight < 0.05f`
- [ ] `DecayOperator.cs` — `LastWeightSet` recency guard applied correctly
- [ ] `DecayOperator.cs` — `--dry-run` does not mutate index
- [ ] `DecayOperator.cs` — wraps all writes in a single SQLite transaction (or batch)
- [ ] `Program.cs` — `decay` subcommand registered with `--days`, `--decay-factor`, `--dry-run` options
- [ ] `Program.cs` — `list` command gains `--include-archived` flag
- [ ] `ListOperator.cs` — passes `includeArchived` to `GetAll`
- [ ] `WeightOperator.cs` — clamp updated to `Math.Clamp(parsed, 0.0f, 2.0f)`
- [ ] E2E: `memctl decay --vault ./vault --days 30` → JSON with all four counter fields
- [ ] E2E: `memctl decay --dry-run --days 30` → JSON report, no index change verified by second `list`
- [ ] E2E: `memctl list` does not show archived notes after decay
- [ ] E2E: `memctl list --include-archived` shows archived notes
- [ ] E2E: `memctl search "keyword"` returns archived note if content matches
- [ ] Unit: weight=1.0 not modified in 30 days → weight=0.9 after `decay --days 30`
- [ ] Unit: weight=1.5 not modified in 30 days → weight ≈ 1.5 * pow(0.9, 1/3) after `decay --days 30`
- [ ] Unit: note with weight=0.04 after decay → `archived=1`
- [ ] Unit: note modified 5 days ago, `--days 30` → unchanged
- [ ] Unit: note with `LastWeightSet` 5 days ago, `--days 30` → unchanged
