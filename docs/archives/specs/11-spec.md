# Requirements Spec: G1 Auto-capture — memctl capture

**Task:** 11
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Overview

`memctl capture` is a new CLI command that enables zero-effort session memory by implementing the Hook Protocol v1 `after-response` event. When wired to a Claude Code `Stop` hook (or any compliant client), the command reads the conversation payload from stdin after each AI response, filters noise, and persists meaningful turns to the vault as dated session notes. The bot never needs to explicitly remember to save — memory formation becomes automatic.

---

## 2. User Stories

- As a bot using Claude Code, I want my conversation turns auto-saved after each response, so that I never lose context across session boundaries without any manual effort.
- As a developer using a non-Claude Code LLM client, I want to call `memctl capture --role user --text "..."` from a shell wrapper, so that I can get the same auto-capture behavior without Claude Code hooks.
- As a user reviewing past sessions, I want session notes organized as `sessions/<date>-<session_id>.md`, so that I can find and review specific session history in Obsidian.
- As a hook consumer, I want `memctl capture` to always exit 0, so that a vault error or missing vault never blocks my LLM session.

---

## 3. Functional Requirements

### 3.1 Hook Mode (stdin JSON payload)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | Read Hook Protocol v1 payload from stdin | Must | [unit] | Parses `{ session_id, cwd, transcript: [{role, content}] }` from stdin; extra fields ignored |
| FR-002 | Fallback when `cwd` absent | Must | [unit] | If `cwd` missing from payload, use `Directory.GetCurrentDirectory()` for vault detection |
| FR-003 | Auto-detect vault from cwd | Must | [unit] | Uses VaultLocator to walk up from cwd; if no vault found, exits 0 silently |
| FR-004 | Filter noise — short turns | Must | [unit] | Skips turns where content stripped of whitespace is < 50 chars |
| FR-005 | Filter noise — tool-call-only turns | Must | [unit] | Skips turns that contain only tool call results and no human-readable prose |
| FR-006 | Create session note if absent | Must | [e2e] | Creates `sessions/<YYYY-MM-DD>-<session_id>.md` with weight=0.5; exit 0; file exists on disk |
| FR-007 | Append to existing session note | Must | [e2e] | If `sessions/<date>-<session_id>.md` exists, appends new turns; existing weight NOT overwritten |
| FR-008 | Re-index after write | Must | [unit] | Calls `index.Upsert(note)` after every create or append (single-note, not full ingest) |
| FR-009 | Exit 0 on invalid JSON stdin | Must | [e2e] | If stdin is not valid JSON, command exits 0 silently with no output |
| FR-010 | Exit 0 when vault missing | Must | [e2e] | If vault cannot be found, exits 0 with no error output |

### 3.2 Direct Mode (`--role`/`--text`)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-011 | Direct input mode | Must | [e2e] | `memctl capture --role user --text "hello"` creates session note without reading stdin |
| FR-012 | Auto session_id in direct mode | Must | [unit] | Uses `<date>-<random-suffix>` as session_id when none provided; stable per process invocation |
| FR-013 | Direct mode exits 0 always | Must | [e2e] | Exit 0 even if vault missing in direct mode |
| FR-014 | Direct mode same note structure | Must | [unit] | Output note format identical to hook mode: `sessions/<date>-<session_id>.md` |

### 3.3 Dry Run

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-015 | `--dry-run` flag | Should | [e2e] | Prints what would be saved to stdout; no write to disk; exit 0 |

### 3.4 Note Format

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-016 | Turn format in note | Must | [unit] | Each turn formatted as `## Turn {ISO timestamp}\n**{role}:** {content}\n` |
| FR-017 | Note title | Must | [unit] | Note title: `Session {date} — {session_id}` |
| FR-018 | Initial weight | Must | [unit] | New session notes created with `weight = 0.5f` (not default 0.0f) |

---

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Performance | Fast enough not to delay hook | Full capture cycle < 2s for typical session (< 50 turns) |
| NFR-002 | Reliability | Never crash the calling hook | All unhandled exceptions caught at top level; exit 0 always |
| NFR-003 | Idempotency | Same payload twice → same note, not duplicated | Re-running with same session_id appends; does not duplicate turns |
| NFR-004 | Error handling | No output to stderr on expected failures | Vault missing, invalid JSON → silent exit 0 (no stderr noise in hook logs) |

---

## 5. Edge Cases & Error Scenarios

1. **Empty transcript**: Payload has `transcript: []`. Expected: exit 0, nothing written, no error.
2. **All turns filtered**: Every turn is < 50 chars or tool-call only. Expected: exit 0, no file created/appended.
3. **session_id contains path separators**: `session_id` = `"../../evil"`. Expected: sanitize to safe filename before use.
4. **Vault found but not indexed**: IngestOperator.NeedsIngest returns true. Expected: initialize index, then write (same as VaultWriteOperator behavior).
5. **Note file exists but unindexed**: `sessions/2026-01-01-abc.md` exists on disk but not in index. Expected: append to file AND upsert to index.
6. **Concurrent hook calls**: Two `memctl capture` processes run simultaneously (rapid responses). Expected: last-write-wins on the note file; both exit 0.
7. **Huge transcript**: 500 turns in one payload. Expected: filters reduce to meaningful turns; write completes without timeout.
8. **`--role` without `--text`**: Direct mode called with only `--role`. Expected: error message to stdout explaining missing `--text`; exit 1 (this is user error, not hook failure).
9. **`cwd` points to non-existent directory**: Payload has `cwd` that doesn't exist. Expected: VaultLocator falls back gracefully; exit 0.

---

## 6. Out of Scope

- Parsing Claude Code-specific tool call JSON schema (filter by presence of prose, not by exact schema)
- Multi-vault routing (one vault per cwd, VaultLocator handles it)
- Encryption or redaction of captured content
- Telegram / chat platform integration (that's `add-turn`)
- MCP tool exposure of `capture` (CLI-only; hooks call CLI)

---

## 7. Dependencies

- `VaultWriteOperator.cs` (task #7) — `ExecuteCreate`, `ExecuteAppend`
- `GemmaEmbeddingEngine` — required for note embedding on create/append
- `VaultLocator` — vault auto-detection from cwd
- `IngestOperator.NeedsIngest` + `DbPath` — index initialization guard

---

## 8. Open Questions

- [x] Should direct mode `--session-id` be supported for shell wrappers that maintain their own session IDs? → Desirable; add as optional flag, generate random if absent.
- [x] Should captures be skipped if the embedding model is not downloaded? → Yes — if embedding unavailable, write note without embedding (index.Upsert with null Embedding); still searchable via BM25.

---

## 9. QC Checklist

- [ ] `memctl capture` with valid JSON payload creates `sessions/<date>-<session_id>.md` (exit 0)
- [ ] `memctl capture` with invalid JSON stdin exits 0 silently
- [ ] `memctl capture` with no vault exits 0 silently
- [ ] Short turns (< 50 chars) not written to note
- [ ] Tool-call-only turns not written to note
- [ ] New note created with weight=0.5
- [ ] Append to existing note does not overwrite weight
- [ ] `--dry-run` prints content, does not write file
- [ ] `--role user --text "..."` direct mode creates note without stdin
- [ ] `session_id` containing path separators is sanitized
- [ ] Re-running same session_id appends, not duplicates
