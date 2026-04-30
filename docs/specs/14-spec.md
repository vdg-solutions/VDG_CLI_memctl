# Requirements Spec: A.D.D V3 Contract First — Operators return MemctlResult Boundary DTO

**Task:** 14
**Date:** 2026-04-30
**Status:** Draft

---

## 1. Overview

memctl currently violates A.D.D V3 layered architecture and the **Contract First** principle. Operators bypass the existing Boundary DTO contract (`Boundary/MemctlResult.cs` with 5 typed sub-DTOs), instead returning `MemctlOutcome` (Core Abstractions Entity) whose `Data` field carries **anonymous objects** whose JSON shape depends on local C# variable names. This task introduces a typed, layer-respecting wire format pipeline: Operators populate `MemctlOutcome.Data` with Entities, a static mapper in `Operators/Mapping/MemctlResultMapper.cs` converts the Outcome into a typed `MemctlResult` (Boundary), and both `ResultPrinter` (CLI) and `McpServerOperator` (MCP) emit the Boundary DTO. Existing JSON wire keys are preserved (backward compat).

---

## 2. User Stories

- As an external consumer (Claude Code, AI agent, shell script) of memctl JSON output, I want a typed, stable schema, so that I can parse `result.data.id`, `result.data.file`, etc. without guessing keys per command or breaking when the Operator's internal variable names change.
- As a memctl maintainer, I want a single mapping point from Entity to Boundary DTO, so that adding a new field to a wire-format response means editing one mapper, not 25 Operators.
- As a contributor refactoring an Operator, I want to rename internal variables freely without breaking external consumers, so that internal evolution does not leak through the wire format.
- As an MCP client (Claude Code memory plugin), I want every tool response to follow the same `{ success, action, message, data }` envelope with typed `data` payloads, so that I can rely on a documented contract instead of probing per tool.
- As a future task author (#22 versioning, #23 validation), I want the Boundary contract to be the single source of truth, so that schema evolution and request validation can hook into Boundary DTOs without operator-by-operator changes.

---

## 3. Functional Requirements

### 3.1 Boundary DTO contract

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | `Boundary/MemctlResult.cs` is referenced from production code, not dead | Must | [unit] | `grep -r "MemctlResult" src/memctl/` returns ≥ 2 distinct production files (mapper + printer) |
| FR-002 | `MemctlResult` envelope retains `success`, `action`, `message`, `data` fields with `[JsonPropertyName]` lowercase keys | Must | [unit] | JSON output of any command contains exactly those 4 top-level keys (lowercase) |
| FR-003 | `NoteDto` JSON shape: `id`, `file`, `title`, `content?`, `snippet?`, `tags`, `links`, `created?`, `modified?`, `score?` with current `[JsonPropertyName]` lowercase keys | Must | [unit] | Existing key names preserved; serialize a populated DTO and assert key set |
| FR-004 | `SearchResultDto` JSON shape: `query`, `count`, `results: NoteDto[]` | Must | [unit] | Serialize and assert key set |
| FR-005 | `TagDto` JSON shape: `tag`, `count` | Must | [unit] | Serialize and assert key set |
| FR-006 | `StatsDto` JSON shape: `note_count`, `tag_count`, `link_count`, `index_bytes`, `vault_path` | Must | [unit] | Serialize and assert key set |
| FR-007 | `GrepHitDto` JSON shape: `file`, `line`, `content` | Must | [unit] | Serialize and assert key set |

### 3.2 Internal Entities (Core Abstractions)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-008 | New Entity `GrepHit` in `CoreAbstractions/Entities/GrepHit.cs` with `FilePath`, `LineNumber`, `Content` | Must | [unit] | File exists; record/class with these fields |
| FR-009 | New Entity `TagCount` in `CoreAbstractions/Entities/TagCount.cs` with `Tag`, `Count` | Must | [unit] | File exists; record with these fields |
| FR-010 | New Entity `VaultStats` in `CoreAbstractions/Entities/VaultStats.cs` with `NoteCount`, `TagCount`, `LinkCount`, `IndexBytes`, `VaultPath` | Must | [unit] | File exists; record with these fields |
| FR-011 | Entities depend on nothing outside Core Abstractions BCL | Must | [unit] | grep `using Memctl\.(Boundary\|Implementations\|Operators\|Bootstrap)` in new entity files = 0 hits |

### 3.3 Mapper (Operators/Mapping)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-012 | `Operators/Mapping/MemctlResultMapper.cs` exists as static class | Must | [unit] | File exists; `public static class MemctlResultMapper` |
| FR-013 | Public method `MemctlResult ToResult(MemctlOutcome outcome)` | Must | [unit] | Method signature exact |
| FR-014 | `ToResult` dispatches `outcome.Data` by runtime type via switch expression | Must | [unit] | switch handles `Note`, `IEnumerable<SearchHit>`, `IEnumerable<TagCount>`, `VaultStats`, `IEnumerable<GrepHit>`, `IEnumerable<Note>`, plus null and fallback |
| FR-015 | `Note → NoteDto` mapping preserves all 10 wire keys | Must | [unit] | Map a populated Note, serialize, assert key set + values for `id`, `file`, `title`, `content`, `tags`, `links`, `created`, `modified` |
| FR-016 | `IEnumerable<SearchHit> → SearchResultDto` includes query string from outcome message or dedicated field | Must | [unit] | Map a list with 2 hits, assert `count = 2`, `results.length = 2`, each item is NoteDto with optional `score` and `snippet` |
| FR-017 | `IEnumerable<TagCount> → TagDto[]` | Must | [unit] | Map a list of 3, assert array length 3, each has `tag` + `count` |
| FR-018 | `VaultStats → StatsDto` | Must | [unit] | Map a populated stats, assert all 5 keys |
| FR-019 | `IEnumerable<GrepHit> → GrepHitDto[]` | Must | [unit] | Map a list of 2, assert array of 2 with `file`, `line`, `content` |
| FR-020 | Unknown `Data` runtime type: pass through as-is to `MemctlResult.Data` (graceful fallback for incremental migration) | Must | [unit] | Map an outcome with `Data = "string scalar"` does not throw; `Data` field round-trips |
| FR-021 | `null` Data: `MemctlResult.Data = null` | Must | [unit] | Map outcome with no Data, assert `Data == null` after mapping |

### 3.4 Operator output normalization

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-022 | No production Operator constructs anonymous object inside `MemctlOutcome.Ok(...)` Data argument | Must | [unit] | `grep -rE "MemctlOutcome\.Ok\([^)]*new \{" src/memctl/Operators/` returns 0 matches |
| FR-023 | `AddOperator`, `VaultWriteOperator.ExecuteCreate/ExecuteUpdate/ExecuteAppend` set `Data = Note` Entity | Must | [unit] | Inspect each return; Data is `Note` instance |
| FR-024 | `GetOperator` sets `Data = Note` | Must | [unit] | Inspect return |
| FR-025 | `ListOperator` sets `Data = IEnumerable<Note>` (or array) | Must | [unit] | Inspect return |
| FR-026 | All search Operators (`SearchOperator`, `SearchSemanticOperator`, `SearchTextOperator`, `SearchTagsOperator`, `SearchLinksOperator`, `SearchDateOperator`) set `Data = IEnumerable<SearchHit>` and propagate query string via outcome message or carrier record | Must | [unit] | Each return inspected |
| FR-027 | `TagsOperator` sets `Data = IEnumerable<TagCount>` | Must | [unit] | Inspect return |
| FR-028 | `StatsOperator` sets `Data = VaultStats` | Must | [unit] | Inspect return |
| FR-029 | `GrepOperator` sets `Data = IEnumerable<GrepHit>` | Must | [unit] | Inspect return |
| FR-030 | Operators with non-DTO outcomes (`StatusOperator`, `IdentityOperator`, `ModelListOperator`, `ModelUseOperator`, `ModelDownloadOperator`, `WeightOperator`, `DecayOperator`, `OrganizeOperator`, `IngestOperator`, `LintOperator`, `FetchOperator`, `CaptureOperator`, `ContextInjectOperator`) either use an Entity or continue passing through (covered by FR-020 graceful fallback) | Should | [unit] | Per-Operator inspection; if anonymous still used, fallback path of FR-020 must serialize identical wire format |

### 3.5 Wire-format adapters

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-031 | `Bootstrap/ResultPrinter.cs` calls `MemctlResultMapper.ToResult(outcome)` and serializes the resulting `MemctlResult` (no manual key mapping) | Must | [unit] | grep ResultPrinter for `new { success = ...` returns 0; grep for `MemctlResultMapper.ToResult` returns ≥ 1 |
| FR-032 | `McpServerOperator` MCP tool responses are routed through `MemctlResultMapper.ToResult` before being framed in the JSON-RPC envelope | Must | [unit] | Inspect McpServerOperator dispatch; result is `MemctlResult`, not `MemctlOutcome` |
| FR-033 | Both adapters serialize via `System.Text.Json` with `WriteIndented = true` (CLI) and compact (MCP) — JSON content identical | Should | [unit] | CLI vs MCP output of the same Operator command produce structurally equivalent JSON (ignoring whitespace) |

### 3.6 Behavioral preservation (backward compat)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-034 | Existing top-level wire keys (`success`, `action`, `message`, `data`) unchanged | Must | [unit] | Snapshot test: pre-refactor output of `memctl add hello` and post-refactor output have identical top-level keys |
| FR-035 | Existing `data` keys for `add` (`id`, `file`, `title`) unchanged | Must | [unit] | Snapshot diff |
| FR-036 | Existing `data` keys for `search*` (`query`, `count`, `results[]` with note fields) unchanged | Must | [unit] | Snapshot diff |
| FR-037 | Exit codes (0 success, 1 fail, 2 not-found per existing semantics) unchanged | Must | [unit] | Run each command pre/post, compare `$LASTEXITCODE` |
| FR-038 | Error path (`MemctlOutcome.Fail(action, message)` with no Data) maps to `MemctlResult { success=false, action, message, data=null }` | Must | [unit] | Trigger fail in unit test, assert mapped result |

---

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Build | Project builds with 0 warning, 0 error | `dotnet build` exits 0 with no `warning` lines |
| NFR-002 | Backward compat | No external consumer needs to change parsing | Snapshot tests of every CLI command JSON output diff zero against a pre-refactor baseline (only ordering / whitespace differences allowed) |
| NFR-003 | A.D.D V3 dependency rules | Boundary depends on nothing outside BCL; CoreAbstractions depends on nothing; Operators depends only on Boundary + CoreAbstractions; Implementations depends only on CoreAbstractions (+ Boundary conditionally) | grep imports per layer; Boundary files contain no `using Memctl.{CoreAbstractions,Operators,Implementations,Bootstrap}`; Core entities contain no `using Memctl.Boundary` etc. |
| NFR-004 | No new dependencies | `memctl.csproj` `<PackageReference>` list unchanged | `git diff src/memctl/memctl.csproj` returns no `<PackageReference>` lines |
| NFR-005 | Test coverage | Mapper has unit tests for every `Data` runtime type listed in FR-014 | `MemctlResultMapperTests` contains test methods covering each branch of the switch |
| NFR-006 | Snapshot baseline | Snapshot baseline files committed under `tests/memctl.Tests/snapshots/wire-format-baseline/` | Directory exists with one JSON file per CLI command tested |
| NFR-007 | Mapping is pure | `MemctlResultMapper` static methods have no I/O, no static mutable state | Manual code review |

---

## 5. Edge Cases & Error Scenarios

1. **Operator returns `MemctlOutcome.Fail(...)`** — Mapper produces `MemctlResult { success=false, action, message, data=null }`. No exception thrown in mapper. Verified by FR-038.
2. **Operator returns `MemctlOutcome.Ok(action, message)` with no Data argument** — Mapper produces `MemctlResult` with `data = null`. Verified by FR-021.
3. **Operator returns `MemctlOutcome` with empty collection (e.g. `[]` SearchHit list)** — Mapper produces `SearchResultDto { query, count: 0, results: [] }`. Empty arrays preserved.
4. **Operator passes scalar value (e.g. `Data = 42` or string)** — Fallback path of FR-020 keeps Data as-is in `MemctlResult.Data: object?`. JSON serialization handles primitive types natively.
5. **Operator passes Entity not yet covered by mapper switch** — Fallback path of FR-020 keeps Data as-is. `MemctlResult.Data: object?` is permissive. Future mapping additions are non-breaking.
6. **`Note.Embedding` field present on Entity** — When mapping `Note → NoteDto`, embedding is omitted (existing NoteDto has no `embedding` field). Wire format does not leak internal vector data.
7. **CLI command outputs nothing on stdout (exit-only flow like `context-inject` returning context to stdout, or `mcp` long-running)** — `ResultPrinter.Print` is not called for these commands; mapper is irrelevant. Verified by inspection.
8. **MCP `initialize` response** — Not a Operator outcome, uses MCP protocol envelope. Not affected by this refactor.
9. **`fetch` command writes raw markdown to stdout, not JSON** — `FetchOperator` returns outcome but Bootstrap calls `Console.Write(outcome.Data?.ToString())` directly. Wire path bypasses ResultPrinter on success — preserved as-is (out of scope for mapper refactor).
10. **`lint --format md`** — Lint already short-circuits ResultPrinter when format=md success. Preserved as-is.

---

## 6. Out of Scope

- Refactor `McpServerOperator` from `Operators/` to `Implementations/Mcp/` (tracked as task #21 — A.D.D Web Adapter pattern).
- Adding `schema_version` field to `MemctlResult` envelope (tracked as task #22).
- Adding validation attributes (`[Required]`, `[Range]`) to Boundary Request DTOs (tracked as task #23).
- Renaming or restructuring existing wire keys — backward compat is mandatory (NFR-002).
- Removing `MemctlOutcome` entirely — kept as internal Entity for future Operator-to-Operator composition.
- Auto-converting MCP tool `outputSchema` declarations to reference Boundary DTOs (tracked as task #15).
- Migrating user vault data (legacy tags etc., tracked as task #19).

---

## 7. Dependencies

- No backlog tasks block this work.
- Internal: depends on existing `Boundary/MemctlResult.cs` content (`MemctlResult` envelope + 5 sub-DTOs already defined with `[JsonPropertyName]`).
- Internal: depends on existing `CoreAbstractions/Entities/Note.cs`, `SearchHit.cs`, and the new entities created by FR-008–010.

---

## 8. Open Questions

- [ ] Should `MemctlResultMapper` live as static class or be exposed via an `IMemctlResultMapper` Port to enable swapping? Decision deferred to design phase. Static recommended for v1 (pure function, no need for indirection).
- [ ] Should query string be carried in `MemctlOutcome` via a new field or via `outcome.Message`? Either works; design phase to pick.
- [ ] Should `SearchHit.Score` round-trip as `score` field in `NoteDto` (current schema) or be lifted to a wrapping `SearchHitDto`? Current `NoteDto` has optional `score` — reuse to avoid wire change.

---

## 9. QC Checklist (Auto-Generated)

- [ ] FR-001: `MemctlResult` referenced ≥ 2 production files
- [ ] FR-002: 4 envelope keys preserved
- [ ] FR-003 to FR-007: each sub-DTO key set preserved
- [ ] FR-008 to FR-010: 3 new Entities added
- [ ] FR-011: New entities depend only on BCL / Core Abstractions
- [ ] FR-012 to FR-014: Mapper class + ToResult + dispatch
- [ ] FR-015 to FR-019: each Entity → DTO mapping correct
- [ ] FR-020: Unknown type fallback graceful
- [ ] FR-021: null Data preserved
- [ ] FR-022: zero `MemctlOutcome.Ok(..., new {)` patterns in Operators
- [ ] FR-023 to FR-029: per-Operator Data type matches Entity contract
- [ ] FR-030: legacy Operators either upgraded or fall through cleanly
- [ ] FR-031: ResultPrinter delegates to mapper
- [ ] FR-032: McpServerOperator delegates to mapper
- [ ] FR-033: CLI / MCP wire identical (modulo formatting)
- [ ] FR-034 to FR-037: snapshot diff zero for all preserved keys
- [ ] FR-038: error path maps cleanly
- [ ] NFR-001: build clean
- [ ] NFR-002: snapshot baseline regression test green
- [ ] NFR-003: layer dependency grep clean
- [ ] NFR-004: no new NuGet packages
- [ ] NFR-005: mapper unit tests cover every dispatch branch
- [ ] NFR-006: snapshot baseline directory committed
- [ ] NFR-007: mapper purity (no I/O, no mutable state)
- [ ] Memory rule (golden_rules / integration test): for any modified Operator, integration test exists exercising the new wire format
