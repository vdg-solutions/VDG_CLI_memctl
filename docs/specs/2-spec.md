# Requirements Spec: Add --folder Filter to Search Commands

**Task:** #2
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

memctl's search commands (BM25, semantic, hybrid) currently scan the entire vault. Adding a `--folder` option lets users pre-filter notes by file path prefix before expensive operations (FTS match, vector similarity), which narrows the search space and improves precision — analogous to the MemPalace "Wing/Room" spatial pre-filtering concept.

## 2. User Stories

- As a user, I want `memctl search --vault . --folder crypto "ethereum"` to return only notes under `crypto/`, so my search is scoped to the relevant topic area.
- As a user, I want `--folder` to work the same way on `search`, `search-semantic`, and `search-text`, so I have a consistent filtering experience across all search commands.
- As a developer building an MCP server on top of memctl, I want folder-scoped search so I can implement tiered context loading (Layer 2: scope-filtered results).

## 3. Functional Requirements

### 3.1 CLI Option

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | `search` command accepts `--folder <prefix>` option | Must | [e2e] | `memctl search --folder crypto "ethereum"` exits 0; output `results` array contains only notes with `file_path` starting with `crypto/` |
| FR-002 | `search-semantic` command accepts `--folder <prefix>` option | Must | [e2e] | `memctl search-semantic --folder crypto "ethereum"` exits 0; output filtered to `crypto/` notes |
| FR-003 | `search-text` command accepts `--folder <prefix>` option | Must | [e2e] | `memctl search-text --folder crypto "ethereum"` exits 0; output filtered to `crypto/` notes |
| FR-004 | `--folder` is independent of the existing `--scope` (note IDs) on `search-semantic` | Must | [unit] | Both `--folder crypto --scope id1,id2` together are valid; folder pre-filters first, then ID scope further restricts |
| FR-005 | When `--folder` is omitted, behavior is identical to current (no regression) | Must | [unit] | All existing tests pass; searches without `--folder` return full-vault results |

### 3.2 Folder Prefix Matching

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-006 | Prefix match is case-sensitive and matches `file_path` stored in index | Must | [unit] | `--folder crypto` matches `crypto/ethereum.md`, `crypto/defi/uniswap.md` but not `Crypto/bitcoin.md` |
| FR-007 | Prefix match normalizes trailing slash: `--folder crypto` and `--folder crypto/` are equivalent | Should | [unit] | Both forms produce identical result sets |
| FR-008 | Nested folder matching is supported | Must | [unit] | `--folder projects/memctl` matches `projects/memctl/design.md` but not `projects/other/foo.md` |
| FR-009 | Non-existent folder prefix returns empty results (not error) | Must | [unit] | `--folder nonexistent` returns `count: 0`, exit 0 |

### 3.3 Index Layer

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-010 | `INoteIndex.SearchBm25` accepts optional `folderPrefix` parameter | Must | [unit] | `index.SearchBm25("query", 10, "crypto")` returns only notes where `file_path LIKE 'crypto/%'` |
| FR-011 | `INoteIndex.SearchSemantic` accepts optional `folderPrefix` parameter | Must | [unit] | `index.SearchSemantic(embedding, 10, folderPrefix: "crypto")` returns only matching notes |
| FR-012 | Folder filter is applied at the SQL level (pre-filter, not post-filter) | Must | [unit] | BM25 FTS query includes `file_path LIKE` clause; semantic query fetches only matching rows |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Performance | Folder filter reduces candidate set before vector similarity computation | Semantic search with `--folder` on a 1000-note vault with 10% matching folder loads only ~100 embeddings into memory |
| NFR-002 | Consistency | `--folder` option description is identical across all 3 commands | All 3 help texts read "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)" |
| NFR-003 | Backward compatibility | No breaking changes to `INoteIndex` — new parameters use default `null` | Existing callers without `folderPrefix` argument compile and behave identically |

## 5. Edge Cases & Error Scenarios

1. **Empty folder prefix**: `--folder ""` — treat as no filter (same as omitted).
2. **Root prefix**: `--folder /` — matches all notes (same as omitted); normalize to null.
3. **Folder with trailing slash**: `--folder crypto/` — strip trailing slash before matching.
4. **Folder with no matches**: return empty result, exit 0, `count: 0`.
5. **`--folder` + `--scope` (note IDs) together on `search-semantic`**: apply folder filter first (SQL WHERE), then intersect with ID scope list in memory or via additional SQL IN clause.
6. **Ingest not yet run**: auto-ingest triggers before filter is applied (existing behavior unchanged).

## 6. Out of Scope

- Case-insensitive folder matching (can be a follow-up).
- Multiple `--folder` values (e.g., `--folder crypto --folder defi`).
- Glob-style patterns (e.g., `--folder "*/crypto"`).
- Any changes to the vault file format or note schema.

## 7. Dependencies

- Existing `INoteIndex.SearchBm25` and `SearchSemantic` in `CoreAbstractions/Ports/INoteIndex.cs`
- Existing `SqliteNoteIndex` in `Implementations/Index/SqliteNoteIndex.cs`
- Existing `SearchOperator`, `SearchSemanticOperator`, `SearchTextOperator` in `Operators/`
- Existing `Program.cs` command wiring in `Bootstrap/`

## 8. Open Questions

- None — design is straightforward given existing `scopeIds` precedent in `SearchSemantic`.

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001: `search --folder crypto` returns only `crypto/` notes (exit 0, count >= 0)
- [ ] FR-002: `search-semantic --folder crypto` returns only `crypto/` notes
- [ ] FR-003: `search-text --folder crypto` returns only `crypto/` notes
- [ ] FR-004: `search-semantic --folder crypto --scope id1` is valid and applies both filters
- [ ] FR-005: All 3 commands without `--folder` return same results as before
- [ ] FR-006: Prefix is case-sensitive (`--folder crypto` ≠ `--folder Crypto`)
- [ ] FR-007: `--folder crypto` and `--folder crypto/` produce identical results
- [ ] FR-008: `--folder projects/memctl` matches nested paths
- [ ] FR-009: Non-existent folder returns `count: 0`, exit 0
- [ ] FR-010: `SearchBm25` SQL includes `file_path LIKE 'prefix/%'` when folderPrefix set
- [ ] FR-011: `SearchSemantic` SQL includes folder filter when folderPrefix set
- [ ] FR-012: Filter applied at SQL level, not post-filter in C#
- [ ] NFR-003: `INoteIndex` signature change uses default null — existing callers unaffected
- [ ] No regression: existing tests still pass
