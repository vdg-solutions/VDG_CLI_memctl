# Requirements Spec: Add Vault Write Tools for Bidirectional Agent Access

**Task:** 7
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

Extend the memctl MCP server with four write-side tools (`create`, `update`, `append`, `set_weight`) so AI agents can persist decisions, insights, and annotations back into the vault during SDLC sessions. Currently memctl covers the full read stack (L0–L3) but has zero write capability. After this task, agents have symmetric read/write access to the vault via MCP.

## 2. User Stories

- As an AI agent (via MCP), I want to create new vault notes, so that I can persist spec decisions and design rationale as searchable knowledge.
- As an AI agent (via MCP), I want to update the content of an existing note, so that I can revise knowledge as it evolves.
- As an AI agent (via MCP), I want to append text to an existing note, so that I can add session insights without overwriting prior content.
- As an AI agent (via MCP), I want to set a note's importance weight, so that I can pin critical notes to the top of `list` without using the CLI.
- As a vault owner, I want all MCP writes to stay within the vault root, so that agents cannot write outside the designated vault directory.
- As a vault owner, I want written notes to be immediately searchable, so that the same session can read back what was just written.

## 3. Functional Requirements

### 3.1 MCP Tool — create

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-001 | `create` tool is listed in `tools/list` response | Must | [unit] | `tools/list` result contains entry with `name: "create"` |
| FR-002 | `tools/call create` with `content` creates a new markdown file in vault and returns `id`, `file`, `title` | Must | [e2e] | File exists on disk at `<vaultPath>/<file>`; stdout JSON has `"success":true`, `"action":"create"` |
| FR-003 | Created note is immediately indexed — subsequent `get` by returned ID succeeds | Must | [e2e] | `tools/call get` with returned ID returns same note content |
| FR-004 | `create` accepts optional `title` (overrides auto-extracted title), `folder` (subfolder path), `filename` (explicit filename without extension) | Must | [unit] | With `folder:"notes"`, file is created at `<vaultPath>/notes/<filename>.md` |
| FR-005 | `create` with no `title` extracts title from first `# Heading` or first non-blank line of content | Must | [unit] | Note title in returned data matches extracted heading |
| FR-006 | `create` with `folder` that does not exist creates the folder | Must | [e2e] | Directory is created; no error returned |
| FR-007 | `create` path safety: resolved file path must be inside vault root | Must | [unit] | `folder: "../../etc"` returns `"success":false` with path traversal error; no file written |

### 3.2 MCP Tool — update

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-008 | `update` tool is listed in `tools/list` response | Must | [unit] | `tools/list` result contains entry with `name: "update"` |
| FR-009 | `tools/call update` with `id` and `content` overwrites the note's markdown content on disk and re-indexes it | Must | [e2e] | `tools/call get` after update returns new content; `modified` timestamp updated |
| FR-010 | `update` preserves the note's ID, file path, and existing frontmatter tags/links | Must | [unit] | Returned `id` matches input `id`; tags array unchanged |
| FR-011 | `update` with non-existent ID returns `"success":false` | Must | [e2e] | stdout JSON has `"success":false`; no file written |

### 3.3 MCP Tool — append

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-012 | `append` tool is listed in `tools/list` response | Must | [unit] | `tools/list` result contains entry with `name: "append"` |
| FR-013 | `tools/call append` with `id` and `content` appends a newline + content to the note on disk and re-indexes it | Must | [e2e] | `tools/call get` after append returns original content + `\n` + appended content |
| FR-014 | `append` preserves all existing content above the append point | Must | [unit] | Original content unchanged; only new content added at end |
| FR-015 | `append` with non-existent ID returns `"success":false` | Must | [e2e] | stdout JSON has `"success":false`; no file modified |

### 3.4 MCP Tool — set_weight

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-016 | `set_weight` tool is listed in `tools/list` response | Must | [unit] | `tools/list` result contains entry with `name: "set_weight"` |
| FR-017 | `tools/call set_weight` with `id` and `weight` (0.0–1.0) sets note importance weight in index | Must | [e2e] | After call, `tools/call list` shows note with updated position relative to other notes |
| FR-018 | `set_weight` weight value is clamped to [0.0, 1.0] — values outside range are clamped silently | Must | [unit] | `weight: 2.5` → stored as `1.0`; `weight: -1` → stored as `0.0`; response shows clamped value |
| FR-019 | `set_weight` with non-existent ID returns `"success":false` | Must | [e2e] | stdout JSON has `"success":false` |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|---------------------|
| NFR-001 | Security | Path traversal prevention on all write operations | Resolved file path starts with `Path.GetFullPath(vaultPath)`; any path outside vault returns `"success":false` immediately |
| NFR-002 | Consistency | Written notes are immediately searchable without manual re-ingest | `index.Upsert(note)` called after every write; `search` and `get` return the note in same MCP session |
| NFR-003 | Architecture | No new `IVaultReader` or `INoteIndex` port methods | Both interfaces unchanged — use existing `WriteNote`, `Upsert`, `SetWeight` |
| NFR-004 | Conventions | `WriteOperator` follows `AddOperator`/`WeightOperator` pattern | Same constructor injection (`IVaultReader`, `INoteIndex`), same vault-init guard, same `MemctlOutcome` return |

## 5. Edge Cases & Error Scenarios

1. **Path traversal via `folder`**: `folder: "../../sensitive"` → `Path.GetFullPath(resolved)` is outside vault → return `success:false`, no file written.
2. **Filename collision on create**: file already exists at the target path → overwrite with new content (last-write-wins; no conflict error).
3. **update/append on note not in index but file exists on disk**: note exists as file but not in SQLite → return `success:false` ("Note not found") — do not write without index entry.
4. **Empty content on create**: `content: ""` → create file with empty body; title extracted as date-based default.
5. **append separator**: note has no trailing newline → append adds `\n` before new content to prevent line merging.
6. **Very long content**: no size limit enforced; write proceeds. Embedding truncates at model limit (512 tokens) silently.
7. **Vault not yet ingested**: `create` called before first ingest → `IngestOperator.NeedsIngest` triggers auto-ingest before write, same as other operators.

## 6. Out of Scope

- YAML frontmatter field editing (tags, links via MCP)
- Binary attachments or non-markdown files
- Folder deletion or note deletion via MCP
- Multi-note transactions / rollback
- Optimistic locking / conflict detection (last-write-wins)
- CLI commands for write operations (existing `add` command covers create; update/append CLI is future work)
- Embedding model selection per write

## 7. Dependencies

- Task #3: MCP server infrastructure (McpServerOperator) — complete ✓
- Task #4: INoteIndex.Upsert + SetWeight — complete ✓
- Task #6: IVaultReader.WriteNote already used by AddOperator — complete ✓
- No new NuGet dependencies

## 8. Open Questions

- [x] File format: raw markdown content only; frontmatter upsert out of scope
- [x] Re-ingest strategy: single-note `index.Upsert` (not full vault re-ingest)
- [x] Conflict handling: last-write-wins (no optimistic locking)
- [x] ID for new notes: `Guid.NewGuid().ToString("N")[..16]` — same as AddOperator

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001: `tools/list` contains `create` tool entry
- [ ] FR-002: `tools/call create` → file on disk + `"success":true` + `id`, `file`, `title` in response
- [ ] FR-003: `tools/call get` with returned ID succeeds immediately after create
- [ ] FR-004: `create` with `folder` arg → file created at `<vault>/<folder>/<filename>.md`
- [ ] FR-005: `create` without `title` extracts title from first heading
- [ ] FR-006: `create` with non-existent `folder` → folder created automatically
- [ ] FR-007: `create` with `folder: "../../etc"` → `"success":false`, no file written
- [ ] FR-008: `tools/list` contains `update` tool entry
- [ ] FR-009: `tools/call update` → disk file has new content; `get` returns new content
- [ ] FR-010: `update` preserves ID, file path, frontmatter tags
- [ ] FR-011: `update` with non-existent ID → `"success":false`
- [ ] FR-012: `tools/list` contains `append` tool entry
- [ ] FR-013: `tools/call append` → `get` returns original + newline + appended content
- [ ] FR-014: `append` leaves content above append point unchanged
- [ ] FR-015: `append` with non-existent ID → `"success":false`
- [ ] FR-016: `tools/list` contains `set_weight` tool entry
- [ ] FR-017: `tools/call set_weight` → note position in `list` changes accordingly
- [ ] FR-018: `set_weight` with value > 1.0 → clamped to 1.0 in response
- [ ] FR-019: `set_weight` with non-existent ID → `"success":false`
- [ ] NFR-001: Path traversal test — `folder: "../../x"` rejected before any disk write
- [ ] NFR-002: Note searchable via `search`/`get` immediately after `create`/`update`/`append`
- [ ] NFR-003: `IVaultReader` interface unchanged after this task
- [ ] NFR-003: `INoteIndex` interface unchanged after this task
- [ ] Edge case: `append` on note with no trailing newline → no line merging
