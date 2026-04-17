# Requirements Spec: Add Layer 0 Identity Note for MCP Context Bootstrapping

**Task:** 6
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

Implement a "Layer 0" identity note — a single designated vault note that MCP clients automatically receive at session start, before any tool call. This completes the MemPalace 4-layer memory stack (L0 identity, L1 top-N by importance, L2 topic-scoped, L3 deep semantic) in memctl. The identity note provides persistent context (user identity, vault purpose, workflow conventions) to AI agents without requiring explicit retrieval.

## 2. User Stories

- As a vault owner, I want to designate one note as the identity note, so that every MCP client session starts with that context automatically.
- As a vault owner, I want to use `memctl identity set <id>` to assign the identity note from the CLI.
- As a vault owner, I want to use `memctl identity get` to inspect which note is currently the identity note.
- As an MCP client (Claude Desktop, Claude Code), I want the identity note content delivered in the `initialize` response, so I don't need to call a tool to load context.
- As an MCP client that doesn't support `serverInfo.instructions`, I want a `get_identity` tool to explicitly retrieve the identity note.
- As a vault owner, I want the identity note to automatically receive weight=1.0 so it appears first in `list`.

## 3. Functional Requirements

### 3.1 CLI — Identity Management

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-001 | `memctl identity set <id-or-path>` designates a note as the identity note | Must | [e2e] | stdout contains `"action":"identity"` and `"success":true`; `GetMetadata("identity_note_id")` returns the note's ID |
| FR-002 | `identity set` accepts both note ID and relative file path | Must | [e2e] | Running with a file path (e.g. `notes/me.md`) succeeds same as with the note ID |
| FR-003 | `identity set` auto-sets note weight to 1.0 | Must | [e2e] | After set, `memctl list` shows identity note as first result |
| FR-004 | `memctl identity get` retrieves the current identity note content | Must | [e2e] | stdout contains `"action":"identity"` and `"success":true` with title and content fields |
| FR-005 | `identity get` with no identity set returns success with explanatory message | Must | [e2e] | stdout contains `"success":true` and message indicates no identity is set; exit code 0 |
| FR-006 | `identity set` with non-existent ID returns failure | Must | [e2e] | stdout contains `"success":false`; exit code 1 |

### 3.2 MCP Server — initialize Response

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-007 | MCP `initialize` response includes identity note content in `result.serverInfo.instructions` when identity is set | Must | [unit] | JSON response has `result.serverInfo.instructions` containing note content string |
| FR-008 | MCP `initialize` response omits `serverInfo.instructions` when no identity note is set | Must | [unit] | JSON response does NOT contain `instructions` key in `serverInfo` |
| FR-009 | MCP `initialize` response omits `serverInfo.instructions` when identity note has been deleted from vault | Must | [unit] | `GetMetadata` returns an ID but `GetById` returns null → instructions omitted |

### 3.3 MCP Server — get_identity Tool

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|---------------------|
| FR-010 | `get_identity` tool is listed in `tools/list` response | Must | [unit] | `tools/list` result contains entry with `name: "get_identity"` |
| FR-011 | `tools/call get_identity` returns identity note data when identity is set | Must | [unit] | Result content JSON has `success: true` and `data` with `id`, `title`, `content` fields |
| FR-012 | `tools/call get_identity` returns graceful message when no identity is set | Must | [unit] | Result content JSON has `success: true`, message "No identity note set", `data: null`; `isError: false` |
| FR-013 | `get_identity` increments access count on the identity note | Should | [unit] | After tool call, note's `access_count` is incremented by 1 |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|---------------------|
| NFR-001 | Consistency | Identity note designation survives re-ingest | After `memctl ingest`, `identity get` still returns the same note |
| NFR-002 | Error Handling | No command or MCP call crashes when identity is unset | All paths with null identity note return graceful output, exit 0 for get/MCP |
| NFR-003 | Architecture | No new `INoteIndex` port methods introduced | `INoteIndex` interface unchanged — uses existing `SetMetadata`/`GetMetadata` only |
| NFR-004 | Conventions | `IdentityOperator` follows `WeightOperator` pattern exactly | Same constructor signature, same vault-init guard, same `MemctlOutcome` return pattern |

## 5. Edge Cases & Error Scenarios

1. **Identity note deleted from vault**: `GetMetadata("identity_note_id")` returns a stale ID; `GetById` returns null → `identity get` returns "Identity note not found (may have been deleted)"; MCP `initialize` omits `instructions`.
2. **identity set with file path not in index**: note not in SQLite yet (vault not ingested) → Fail with "Note not found".
3. **identity set overwrite**: calling `identity set` twice with different notes → second call overwrites; first note retains its weight=1.0 (not rolled back, acceptable).
4. **No vault flag**: `identity set/get` without `--vault` → same RequireVault guard as all other commands → prints error, exit 1.
5. **MCP get_identity with deleted note**: same as #1 — returns "Identity note not found" gracefully, `isError: false`.
6. **Empty note content**: identity note exists but has empty content → instructions field contains empty string, not null (omission only happens when note doesn't exist).

## 6. Out of Scope

- Per-topic or per-folder identity notes (single vault = single identity)
- Multiple identity notes
- Identity note creation/editing via CLI (use `memctl add` separately)
- Auto-detecting identity note by tag or filename convention
- Clearing/unsetting the identity note (future task)
- MCP Resources protocol (`resource://identity`) — raw tools approach is sufficient

## 7. Dependencies

- Task #3: MCP server infrastructure (McpServerOperator) — complete ✓
- Task #4: SQLite metadata table (`SetMetadata`/`GetMetadata`) — complete ✓
- No new NuGet dependencies

## 8. Open Questions

- [x] Best injection point → resolved: `serverInfo.instructions` + `get_identity` tool (dual)
- [x] Designation convention → resolved: SQLite metadata `identity_note_id`
- [x] Sort treatment → resolved: auto-set weight=1.0
- [x] Empty state → resolved: silent skip / graceful message

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001: `memctl identity set <id>` stdout contains `"success":true` and `"action":"identity"`
- [ ] FR-002: `identity set` works with both note ID and file path as argument
- [ ] FR-003: After `identity set`, note has weight=1.0 (verify via `memctl list` or `GetById`)
- [ ] FR-004: `identity get` returns note title and content when identity is set
- [ ] FR-005: `identity get` with no identity set exits 0 with message "No identity note set"
- [ ] FR-006: `identity set <nonexistent>` exits non-zero with `"success":false`
- [ ] FR-007: MCP `initialize` JSON contains `result.serverInfo.instructions` when identity is set
- [ ] FR-008: MCP `initialize` JSON does NOT contain `instructions` key when no identity set
- [ ] FR-009: MCP `initialize` omits instructions when identity note ID is stale (deleted note)
- [ ] FR-010: `tools/list` response includes `get_identity` tool entry
- [ ] FR-011: `tools/call get_identity` returns note id, title, content when identity set
- [ ] FR-012: `tools/call get_identity` returns `success:true`, `data:null`, friendly message when unset
- [ ] FR-013: `get_identity` tool call increments identity note's access_count
- [ ] NFR-001: `identity_note_id` metadata survives re-ingest (metadata table not cleared by ingest)
- [ ] NFR-002: No crash/non-zero exit from any path when identity is unset
- [ ] NFR-003: `INoteIndex` interface has no new methods after this task
- [ ] Memory rule: `serverInfo.instructions` populated in `HandleInitialize` (from qc_errors mcp_serverinfo pattern)
- [ ] Edge case: stale identity note ID (note deleted) → graceful "not found" message
