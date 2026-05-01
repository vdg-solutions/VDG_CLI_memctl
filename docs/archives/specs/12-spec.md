# Requirements Spec: G2 Proactive Injection — memctl context-inject

**Task:** 12
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Overview

`memctl context-inject` is a new CLI command that enables proactive memory injection via the Claude Code `UserPromptSubmit` hook. Before the bot processes each user prompt, this command reads the prompt from stdin, extracts keywords, queries the vault for relevant notes, and writes a formatted markdown context block to stdout. Claude Code prepends this block to the user prompt automatically — the bot receives memory context without needing to call `list` or `search` itself.

---

## 2. User Stories

- As a bot using Claude Code, I want relevant memory context auto-injected before each prompt, so that I never miss prior decisions or context without explicitly querying the vault.
- As a developer setting up the hook, I want `memctl context-inject` to always exit 0 and never crash, so that a missing vault or empty stdin never blocks the session.
- As a developer testing the integration, I want `memctl context-inject --dry-run` to print what would be injected, so that I can verify the output before wiring the live hook.
- As a user with a JSON-based hook client, I want the command to extract the prompt text from the first string field if stdin is JSON, so that future hook protocol changes remain backward-compatible.

---

## 3. Functional Requirements

### 3.1 Stdin Parsing

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | Read stdin as plain text | Must | [unit] | Command reads all stdin as UTF-8 text; treats content as the user prompt |
| FR-002 | JSON stdin — extract first string field | Must | [unit] | If stdin is valid JSON, extract the value of the first `string` field as the prompt text; other field types skipped |
| FR-003 | Empty stdin — exit 0 silently | Must | [e2e] | If stdin is empty or whitespace-only, write empty string to stdout and exit 0 |
| FR-004 | Invalid JSON treated as plain text | Must | [unit] | If stdin starts with `{` or `[` but fails to parse as JSON, use raw stdin as the prompt text |

### 3.2 Vault Detection

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-005 | Auto-detect vault from process cwd | Must | [unit] | Uses `VaultLocator.FindVault(Directory.GetCurrentDirectory())` to locate vault |
| FR-006 | Exit 0 when vault missing | Must | [e2e] | If vault not found, write empty string to stdout and exit 0; no error output |

### 3.3 Keyword Extraction

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-007 | Tokenize prompt | Must | [unit] | Split prompt on whitespace and punctuation (`\W+`); lowercase all tokens |
| FR-008 | Deduplicate tokens | Must | [unit] | Duplicate tokens removed before filtering |
| FR-009 | Filter stop words | Must | [unit] | Removes common English stop words: `a, an, the, is, are, was, were, be, been, being, have, has, had, do, does, did, will, would, could, should, may, might, must, shall, can, need, dare, ought, used, and, or, but, if, in, on, at, to, for, of, with, by, as, from, into, about, up, out, then, than, that, this, these, those, i, you, he, she, it, we, they, me, him, her, us, them, my, your, his, its, our, their` |
| FR-010 | Empty keyword set after filtering — fallback | Must | [unit] | If no keywords remain after filtering, skip search; go directly to list fallback (FR-014) |

### 3.4 Vault Query Strategy

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-011 | Primary search — BM25 keyword search | Must | [unit] | Calls `index.SearchBm25(keywordsJoined, limit: 6)`; keywords joined with space |
| FR-012 | Secondary list — top-weighted notes | Must | [unit] | Calls `index.GetAll()`, takes first 3 by weight descending |
| FR-013 | Dedup search + list results | Must | [unit] | List results appended only if their `note.Id` is NOT already in search results; dedup by id |
| FR-014 | Fallback: list-only when search empty | Must | [unit] | If keywords empty (FR-010) OR search returns 0 hits, calls `index.GetAll()`, takes first 6 by weight; no search |
| FR-015 | No results — exit 0 with empty output | Must | [e2e] | If combined results list is empty, write empty string to stdout and exit 0 |

### 3.5 Output Formatting

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-016 | Context block header | Must | [unit] | Output starts with `## Memory Context\n\n` when notes are present |
| FR-017 | Per-note format | Must | [unit] | Each note formatted as `### {title}\n{content}\n\n`; title is `note.Title` |
| FR-018 | Content truncation | Must | [unit] | If `note.Content.Length > 500`, truncate at 500 chars and append `...`; if ≤ 500, output as-is |
| FR-019 | Write to stdout | Must | [e2e] | Context block written to `Console.Out`; exit 0 |
| FR-020 | Dry-run — identical behavior | Must | [e2e] | `--dry-run` flag produces same stdout output as live run; no side effects either way (read-only command) |

### 3.6 Hook Safety

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-021 | Exit 0 always | Must | [e2e] | Command exits 0 in ALL code paths: vault missing, stdin empty, no results, JSON error, exception |
| FR-022 | No error output on non-fatal paths | Must | [e2e] | Stdout is either a valid context block or empty string; no error messages leak to stdout on graceful exits |
| FR-023 | Outer try/catch — never crash | Must | [unit] | Top-level `try/catch` in Program.cs handler; any unhandled exception → exit 0 silently |

### 3.7 Documentation

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-024 | UserPromptSubmit hook config in docs | Should | [unit] | `docs/memctl.md` contains example `UserPromptSubmit` hook JSON config block |

---

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Performance | Latency < 200ms for typical vault (< 1000 notes) | Measured via smoke test; hook latency imperceptible to user |
| NFR-002 | Hook safety | Never block the Claude Code session | Exit 0 in all code paths; verified by test passing even when vault missing |
| NFR-003 | Output purity | Stdout contains only the context block or empty string | No debug lines, no error messages, no JSON wrappers on stdout |
| NFR-004 | Keyword filtering | Stop word list must not break single-word prompts | Single non-stop-word prompt produces ≥1 keyword and triggers search |

---

## 5. Edge Cases & Error Scenarios

1. **All tokens are stop words** (e.g., `"the is and"`): All tokens filtered → empty keyword set → fallback list(6). Expected: returns top 6 weighted notes.

2. **Single non-stop-word keyword** (e.g., `"typescript"`): One keyword → `SearchBm25("typescript", 6)`. Expected: up to 6 BM25 hits.

3. **Search returns 0 hits, list has notes**: Keywords non-empty but BM25 finds nothing → fallback list(6). Expected: returns up to 6 list notes.

4. **Search returns 3 hits, list would add 3 more**: `search_results` has 3 ids; `list_results` has 3 different ids → merge = 6 notes. Expected: 6 notes in output.

5. **Search and list share the same top note**: Note already in search_results by id → list result skipped during dedup. Expected: note appears once.

6. **stdin is JSON with integer field first**: `{ "count": 42, "prompt": "fix the bug" }` → first string field is `"prompt"` → prompt = `"fix the bug"`. Expected: keywords extracted from `"fix the bug"`.

7. **stdin is JSON with no string fields**: `{ "count": 42, "active": true }` → no string field found → treat as empty prompt → output empty string, exit 0.

8. **Very large prompt** (> 10,000 chars): Keywords extracted normally; only unique non-stop tokens used. Expected: no crash, query runs normally.

9. **Vault exists but index empty** (freshly initialized): Search returns 0; GetAll() returns 0 → output empty string, exit 0.

10. **Note content exactly 500 chars**: No truncation — output as-is, no `...` appended.

11. **Note content 501 chars**: Truncated at 500 + `...` → 503 chars in output.

12. **Non-redirected stdin** (interactive terminal, no pipe): `Console.IsInputRedirected == false` → output empty string, exit 0.

---

## 6. Out of Scope

- Semantic/embedding-based search (uses BM25 only — GemmaEmbeddingEngine not required for context-inject)
- Modifying the vault or writing any notes (read-only command)
- Filtering by folder prefix or tags
- Multi-language stop word lists (English only)
- Caching search results between invocations
- Streaming output (write all at once to stdout)

---

## 7. Dependencies

- `INoteIndex.SearchBm25(query, limit)` — exists in current interface
- `INoteIndex.GetAll()` — exists; sorted by `weight DESC, access_count DESC`
- `VaultLocator.FindVault(path)` — exists
- `IngestOperator.NeedsIngest / DbPath` — exists (same init pattern as CaptureOperator)
- No new NuGet packages required

---

## 8. Open Questions

- [ ] Should the output include a separator or blank line after `## Memory Context` header and before the first note? (Current spec: yes — `## Memory Context\n\n` before notes; `\n\n` after each note)
- [ ] Should `index.IncrementAccess` be called for each returned note? (Tentative: yes — accessing notes via injection counts as a read; signals relevance)

---

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001/002: Plain text stdin correctly parsed as prompt; JSON stdin extracts first string field
- [ ] FR-003: Empty stdin → stdout is empty string, exit 0
- [ ] FR-004: Malformed JSON treated as plain text prompt
- [ ] FR-005/006: Vault auto-detected from cwd; missing vault → empty stdout, exit 0
- [ ] FR-007/008/009: Keyword tokenization lowercases, deduplicates, filters all listed stop words
- [ ] FR-010: All-stop-word prompt → fallback list(6) path taken (not search path)
- [ ] FR-011: SearchBm25 called with keywords joined by space, limit=6
- [ ] FR-012: GetAll() takes first 3 results for secondary (sorted by weight DESC)
- [ ] FR-013: Dedup — list results with ids already in search_results are NOT appended
- [ ] FR-014: Fallback list(6) triggered when keywords empty OR search returns 0 hits
- [ ] FR-015: Zero combined results → empty stdout, exit 0
- [ ] FR-016/017/018: Output format: header + `### {title}\n{content[..500]...}\n\n`
- [ ] FR-018: Content exactly 500 chars — no truncation; 501 chars — truncated + `...`
- [ ] FR-019/020: Output written to stdout; dry-run produces same output
- [ ] FR-021/022/023: Exit 0 in all paths; no error messages on stdout; outer try/catch present
- [ ] FR-024: docs/memctl.md contains UserPromptSubmit hook config example
- [ ] Memory rule: auto_session_id_double_date — N/A (no file paths generated in this command)
- [ ] Memory rule: sqlite_upsert_weight_not_inserted — N/A (read-only command, no upsert)
- [ ] NFR-001: Smoke test confirms exit 0 with non-empty output for a vault with notes
- [ ] NFR-003: No debug/error lines appear on stdout
