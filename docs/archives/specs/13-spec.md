# Requirements Spec: G3 Lint Two-Tier — memctl lint

**Task:** 13
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Overview

`memctl lint` is a two-tier vault health check command. Tier 1 (structural) runs without an LLM using the existing index; Tier 2 (semantic) optionally calls an external OpenAI-compatible LLM or prints a self-contained prompt to stdout for the user to paste manually. Both tiers produce JSON output by default, with `--format md` producing human-readable markdown.

---

## 2. User Stories

- As a developer, I want `memctl lint` to detect orphan notes, broken links, duplicate candidates, and decay-risk notes, so that I can maintain a clean, healthy vault without running an LLM.
- As a developer, I want `memctl lint --semantic --llm-url ... --llm-model ...` to run LLM-based deeper analysis over vault notes, so that I can catch contradictions, stale claims, and missing cross-references.
- As a developer without LLM access, I want `memctl lint --semantic --self` to print a structured prompt to stdout so I can paste it into any LLM manually.
- As a developer running `memctl ingest`, I want a hint in the output if semantic lint hasn't run in over 14 days, so that I am reminded to run `--semantic` periodically.
- As a developer, I want to persist a lint report as a vault note with `--save`, so that the report is accessible inside the vault.
- As a developer automating semantic lint in a cron job, I want `--update-timestamp` to record that semantic lint ran without re-running it, so that I can suppress the ingest hint after manual LLM review.

---

## 3. Functional Requirements

### 3.1 Command & Flags

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | Register `memctl lint` subcommand | Must | [e2e] | `memctl lint --help` prints usage; command accepted by root |
| FR-002 | `--semantic` flag enables Tier 2; absent → Tier 1 only | Must | [unit] | Tier 2 code path not entered without `--semantic` |
| FR-003 | `--self` flag prints structured prompt to stdout; incompatible with `--llm-url` | Must | [unit] | With `--self`, no HTTP call made; prompt written to stdout; structural results written to stderr-equivalent JSON |
| FR-004 | `--format` option accepts `json` (default) or `md` | Must | [unit] | `--format json` serializes to JSON; `--format md` produces markdown header+sections |
| FR-005 | `--save` flag persists structural lint report as vault note `lint/<date>-structural.md` | Should | [unit] | File created at correct path; note upserted to index |
| FR-006 | `--update-timestamp` updates `last_semantic_lint` in metadata table; skips lint run | Must | [unit] | Only `SetMetadata` called; `GetAll` not called; outcome `action = "lint"` |
| FR-007 | `--llm-url`, `--llm-model`, `--llm-key` configure external LLM call | Must | [unit] | Client constructed with provided values; request sent to `{url}/chat/completions` |
| FR-008 | `--dry-run` flag: structural lint runs, no side effects (no note written, no timestamp updated) | Should | [unit] | `--save` + `--dry-run` does not write file; timestamp not updated in dry-run |
| FR-009 | Vault auto-detected from cwd (same pattern as other commands) | Must | [e2e] | `memctl lint` from vault cwd works without `--vault` |

### 3.2 Structural Tier (Tier 1)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-010 | Structural lint reads from existing index — does not re-ingest | Must | [unit] | No vault file I/O during structural lint; only `index.GetAll()` called |
| FR-011 | Detect **orphan notes**: notes with zero inbound links from any other note | Must | [unit] | Note with no other note's `Links[]` containing its title is listed in `orphans` array |
| FR-012 | Inbound link count uses `Note.Links[]` field already stored in index | Must | [unit] | Computation derived from `note.Links` arrays across all notes; no separate SQL query |
| FR-013 | Detect **broken links**: entries in `Note.Links[]` that resolve to no existing note title (case-insensitive) | Must | [unit] | Returns `{note_id, note_title, broken_link}` per broken reference |
| FR-014 | Detect **duplicate candidates**: pairs of notes with embedding cosine similarity > 0.92 | Must | [unit] | Each unordered pair reported once; `{note_a, note_b, similarity}` |
| FR-015 | If a note has no embedding, it is excluded from duplicate detection | Must | [unit] | Notes with `Embedding == null` skipped silently |
| FR-016 | Duplicate detection uses Note.Embedding[] directly (no extra index queries) | Must | [unit] | O(n²) cosine comparison over in-memory embeddings; no `SearchSemantic` calls |
| FR-017 | Detect **decay-risk** notes: `Weight in [0.05, 0.30]` AND `Modified < (now - 60 days)` AND `inbound_link_count >= 2` | Must | [unit] | Each criterion independently testable; note in `decay_risk` list only when all three met |
| FR-018 | Structural result object shape: `{ orphans: [], broken_links: [], duplicates: [], decay_risk: [] }` | Must | [unit] | JSON keys match exactly; empty arrays when no issues found |
| FR-019 | `orphans` items: `{ id, title, file_path }` | Must | [unit] | Each orphan entry has exactly these three fields |
| FR-020 | `broken_links` items: `{ note_id, note_title, broken_link }` | Must | [unit] | Each broken link entry has exactly these three fields |
| FR-021 | `duplicates` items: `{ note_a_id, note_a_title, note_b_id, note_b_title, similarity }` | Must | [unit] | Similarity rounded to 4 decimal places; `note_a_id < note_b_id` for determinism |
| FR-022 | `decay_risk` items: `{ id, title, weight, days_since_modified, inbound_link_count }` | Must | [unit] | Each decay-risk entry has exactly these five fields; `days_since_modified` is integer |

### 3.3 Semantic Tier (Tier 2)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-023 | Semantic tier only runs with `--semantic` flag | Must | [unit] | Without `--semantic`, semantic fields absent from output |
| FR-024 | `--self` mode: format all vault notes as structured markdown prompt; write to stdout | Must | [unit] | Output contains `# Vault Self-Analysis Request` header; each note included as `### {title}\n{content}` block |
| FR-025 | `--self` output ends with `## Instructions` section listing analysis categories | Must | [unit] | Instructions section present; mentions Contradictions, Stale Claims, Missing Links, Summary Gaps |
| FR-026 | `--self` output ends with hint: `When done, run: memctl lint --update-timestamp` | Must | [unit] | Exact command included in output |
| FR-027 | External LLM: POST to `{llm-url}/chat/completions` with all notes in batches of 50 | Must | [unit] | `SemanticBatchSize = 50`; multiple requests if notes > 50 |
| FR-028 | LLM request uses `response_format: { type: "json_object" }` | Must | [unit] | Request body includes response_format field |
| FR-029 | LLM timeout: 30 seconds per request | Must | [unit] | `HttpClient.Timeout = TimeSpan.FromSeconds(30)` |
| FR-030 | On LLM timeout: output structural results, set exit code 1 | Must | [unit] | Structural JSON printed before LLM call timeout; exit 1 after timeout |
| FR-031 | LLM response parsed for sections: `contradictions`, `stale_claims`, `missing_links`, `summary_gaps` | Must | [unit] | Each field parsed from LLM JSON response; missing fields default to `[]` |
| FR-032 | Semantic result merged into output alongside structural result | Must | [unit] | Output has both `structural` and `semantic` keys in `data` |
| FR-033 | After successful LLM semantic lint (non-dry-run): set `last_semantic_lint` in metadata table | Must | [unit] | `index.SetMetadata("last_semantic_lint", iso8601)` called |

### 3.4 Output Format

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-034 | Default output: standard `MemctlOutcome` JSON via `ResultPrinter.Print` | Must | [unit] | `{ success, action, message, data: { structural, semantic? } }` |
| FR-035 | `--format md` output: markdown with `# Vault Lint Report` heading, dated | Must | [unit] | Sections for each structural category; bullet list per issue; counts in section headers |
| FR-036 | `--format md` includes summary line: `N issues found (orphans: X, broken: Y, dupes: Z, decay: W)` | Must | [unit] | Counts accurate; zero-issue summary shows `0 issues found` |
| FR-037 | `--format md` with `--semantic` appends `## Semantic Analysis` section | Should | [unit] | Semantic subsections present when `--semantic` used |

### 3.5 Ingest Integration

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-038 | After `ingest` completes, check `last_semantic_lint` from metadata table | Must | [unit] | `index.GetMetadata("last_semantic_lint")` called in `IngestOperator.Execute` |
| FR-039 | If `last_semantic_lint` is null or more than 14 days ago: add `semantic_lint_hint` to ingest outcome data | Must | [unit] | `ingest` JSON output includes `semantic_lint_hint: "Semantic lint not run in X days. Run: memctl lint --semantic"` |
| FR-040 | If `last_semantic_lint` is within 14 days: no hint in ingest output | Must | [unit] | `semantic_lint_hint` field absent when lint is recent |

### 3.6 --save Feature

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-041 | `--save` writes structural lint report as markdown to `{vault}/lint/{date}-structural.md` | Must | [unit] | File path follows pattern `lint/2026-04-19-structural.md` |
| FR-042 | `--save` also upserts note to index | Must | [unit] | `index.Upsert()` called for the saved note |
| FR-043 | `--save` in `--dry-run` mode: skip file write and upsert | Must | [unit] | No file created in dry-run mode |

### 3.7 Error Handling

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-044 | Vault not found: return `MemctlOutcome.Fail` with exit code 1 | Must | [e2e] | Error message includes vault path |
| FR-045 | `--semantic` without `--llm-url`/`--llm-model` and without `--self`: return `MemctlOutcome.Fail` | Must | [unit] | Error: "Semantic lint requires --llm-url and --llm-model, or --self" |
| FR-046 | `--self` and `--llm-url` combined: return `MemctlOutcome.Fail` | Must | [unit] | Error: "--self and --llm-url are mutually exclusive" |
| FR-047 | LLM HTTP error (non-timeout): log warning in message, return structural results with `semantic: null`; exit 0 | Should | [unit] | Non-timeout HTTP errors do not block structural output |
| FR-048 | Empty vault (no notes): return structural result with all empty arrays | Must | [unit] | No crash; all arrays empty; message indicates note count |

---

## 4. Non-Functional Requirements

| ID | Requirement |
|----|------------|
| NFR-001 | Structural lint runs in O(n²) time for duplicate detection; acceptable for vaults up to 10,000 notes |
| NFR-002 | Structural lint does not re-ingest; only reads from SQLite index |
| NFR-003 | No new NuGet packages; LLM calls use `System.Net.Http.HttpClient` |
| NFR-004 | Exit code 0 for all successful runs (structural or semantic); exit code 1 only on LLM timeout or vault not found |
| NFR-005 | `--self` output goes to `Console.Out`; normal JSON outcome goes to `Console.Out` (same as all other commands) |
| NFR-006 | LintOperator follows existing Operator constructor pattern: `(IVaultReader vaultReader, INoteIndex index)` |
| NFR-007 | No new INoteIndex methods required; existing `GetAll()`, `SetMetadata()`, `GetMetadata()`, `Upsert()` suffice |

---

## 5. Edge Cases

| Scenario | Expected Behavior |
|----------|------------------|
| Vault has 0 notes | Structural result: all empty arrays; message: "Lint complete: 0 notes, 0 issues" |
| All notes have no embeddings | `duplicates: []`; no crash; message indicates embedding coverage |
| Note links to itself | Not reported as broken link (title resolves to self) |
| Two notes with identical titles | Both match each other's links; not a broken link |
| `--save` when `lint/` folder doesn't exist | Folder created by `IVaultReader.WriteNote` (same as existing write behavior) |
| `last_semantic_lint` in metadata is a malformed date string | Treat as null → show hint |
| All notes are orphans | `orphans` list = all notes; not an error |
| Embeddings are L2-normalized (existing behavior) | Cosine similarity = dot product (same as SqliteNoteIndex.CosineSimilarity) |
| `--update-timestamp` + `--dry-run` | Dry-run takes precedence; timestamp not written |

---

## 6. QC Checklist

- [ ] `memctl lint` from vault cwd returns structural JSON
- [ ] `memctl lint --format md` returns markdown report
- [ ] `memctl lint --semantic --self` prints self-analysis prompt to stdout; no HTTP calls
- [ ] `memctl lint --semantic --llm-url ... --llm-model ...` makes HTTP POST to LLM endpoint
- [ ] `memctl lint --semantic` without `--self` or `--llm-url` returns Fail outcome
- [ ] `memctl lint --self --llm-url ...` returns Fail outcome
- [ ] `memctl lint --save` writes `lint/<date>-structural.md` to vault
- [ ] `memctl lint --dry-run --save` does not write file
- [ ] `memctl lint --update-timestamp` updates metadata without running lint
- [ ] `memctl ingest` shows semantic_lint_hint when last_semantic_lint > 14 days
- [ ] `memctl ingest` does not show hint when last_semantic_lint within 14 days
- [ ] Orphan detection: note with no inbound links appears in orphans
- [ ] Broken link detection: `[[NonExistent]]` in note.Links[] reported as broken
- [ ] Duplicate detection: pair with cosine > 0.92 reported; pair with cosine ≤ 0.92 not reported
- [ ] Decay-risk: note meeting all three criteria appears; note missing any criterion does not
- [ ] LLM timeout: structural results printed; exit code 1
- [ ] Empty vault: all arrays empty; exit 0
