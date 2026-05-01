# Requirements Spec: Add MCP Server Mode

**Task:** #3
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Overview

Add a `memctl mcp` command that starts a stdio-based MCP (Model Context Protocol) server, exposing the vault as an AI memory layer. The server speaks JSON-RPC 2.0 over stdin/stdout per the MCP spec, advertising 7 tools that map 1:1 to existing Operators. AI clients (Claude Desktop, agents, etc.) can connect and query the vault without needing direct CLI invocation.

The `weight` command (task #4) and `--folder` filter (task #2) are prerequisites; both are complete. `GetAll()` already returns notes sorted by `weight DESC, access_count DESC`.

---

## 2. User Stories

- As an AI agent, I want to call `search` via MCP to retrieve semantically-ranked notes from the vault, so I can build grounded responses from the user's personal knowledge base.
- As an AI agent, I want to call `list` with a `limit` parameter to get the top-N most important notes first, so I can implement tiered context loading without retrieving the full vault.
- As an AI agent, I want to call `get` with a note ID to retrieve full note content including wikilinks, so I can follow the knowledge graph.
- As a user, I want `memctl mcp --vault <path>` to start the MCP server pointed at my vault, so Claude Desktop or any MCP-compatible client can connect without additional configuration.
- As a developer, I want each MCP tool to return the same `NoteToData` shape used by the CLI, so there is a single canonical note representation.

---

## 3. Functional Requirements

### 3.1 CLI Command

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-001 | `memctl mcp` subcommand exists and is wired in `Program.cs` | Must | [e2e] | `memctl mcp --help` exits 0 and prints usage without error |
| FR-002 | `memctl mcp --vault <path>` starts a stdio MCP server; process blocks until stdin is closed | Must | [e2e] | Process started with `--vault .` does not immediately exit; it reads from stdin |
| FR-003 | `--vault` option is required for `mcp`; omitting it exits 1 with a clear error | Must | [e2e] | `memctl mcp` (no vault) exits 1; stderr/stdout contains `--vault is required` |
| FR-004 | `--model-dir` global option is forwarded to the embedding engine (same as other commands) | Must | [unit] | MCP server uses the model resolved via `MemctlConfig.ResolveModelDir` |

### 3.2 MCP Transport & Protocol

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-005 | Server uses stdio transport (stdin/stdout); all JSON-RPC messages are newline-delimited | Must | [e2e] | Sending `{"jsonrpc":"2.0","id":1,"method":"initialize","params":{...}}` on stdin produces a valid `initialize` response on stdout |
| FR-006 | Use the `ModelContextProtocol` NuGet package (latest preview, target `0.2.0-preview.3` or newer) for MCP host/server scaffolding | Must | [unit] | `memctl.csproj` references `ModelContextProtocol`; server starts without manual JSON-RPC parsing |
| FR-007 | Server responds to `initialize` with `serverInfo.name = "memctl"` and `capabilities.tools = {}` | Must | [e2e] | `initialize` response JSON contains `serverInfo.name == "memctl"` |
| FR-008 | Server responds to `tools/list` with all 7 registered tools and their input schemas | Must | [e2e] | `tools/list` response contains entries for: `search`, `get`, `list`, `search_semantic`, `search_tags`, `search_date`, `search_links` |
| FR-009 | Server responds to `tools/call` by invoking the corresponding Operator and returning the result as MCP tool content | Must | [e2e] | `tools/call` with `name: "get"` and valid `arguments.id` returns content with the note JSON |
| FR-010 | Unrecognized `tools/call` name returns a JSON-RPC error response (code -32601 Method Not Found) | Must | [e2e] | `tools/call` with `name: "nonexistent"` returns error response, not a crash |

### 3.3 Vault Initialization

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-011 | Vault path is resolved once at server startup and stored; all tool handlers use this fixed path | Must | [unit] | MCP server class receives vault path in constructor; tool handlers do not accept vault as a tool parameter |
| FR-012 | Auto-ingest is triggered on first tool call if `IngestOperator.NeedsIngest(vaultPath)` returns true (same as existing operators) | Must | [unit] | Starting server against un-ingested vault and calling any tool succeeds; ingest runs transparently |
| FR-013 | Embedding engine is initialized lazily on first call to a semantic tool (`search`, `search_semantic`) | Should | [unit] | Starting the server does not load the ONNX model; model loads only when a semantic tool is first called |

### 3.4 Tool: `search` (hybrid RRF)

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-014 | Tool `search` maps to `SearchOperator.Execute` | Must | [e2e] | `tools/call search {"query":"ethereum","limit":5}` returns `count` and `results` array |
| FR-015 | Input schema: `query` (string, required), `limit` (integer, optional, default 10), `folder` (string, optional) | Must | [unit] | `tools/list` shows these three parameters with correct JSON Schema types |
| FR-016 | Tool result is the `data` portion of `MemctlOutcome` serialized as a JSON string in MCP `TextContent` | Must | [e2e] | Content item is `{"type":"text","text":"{...json...}"}` where inner JSON contains `query`, `count`, `results` |
| FR-017 | If `SearchOperator` returns `Success = false` (e.g. model mismatch), tool returns `isError: true` with the message | Must | [e2e] | Calling `search` without the embedding model downloaded returns an MCP error content item |

### 3.5 Tool: `get`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-018 | Tool `get` maps to `GetOperator.Execute` | Must | [e2e] | `tools/call get {"id":"<valid-id>"}` returns full note content including `content`, `tags`, `links`, `weight`, `access_count` |
| FR-019 | Input schema: `id` (string, required) — accepts note ID or relative file path | Must | [unit] | `tools/list` shows `id` as required string |
| FR-020 | Note not found returns `isError: true` with message `Note not found: <id>` | Must | [e2e] | `tools/call get {"id":"nonexistent"}` returns error content, not a crash |
| FR-021 | Successful `get` increments `access_count` (delegated to `GetOperator` which calls `IncrementAccess`) | Must | [unit] | After 3 MCP `get` calls on same note, `access_count` = 3 |

### 3.6 Tool: `list`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-022 | Tool `list` maps to `ListOperator.Execute` | Must | [e2e] | `tools/call list {}` returns `count` and `notes` array |
| FR-023 | Input schema: `limit` (integer, optional, default 10), `tag` (string, optional) | Must | [unit] | `tools/list` shows these two parameters |
| FR-024 | When `limit` is omitted, defaults to 10 | Must | [e2e] | `tools/call list {}` returns at most 10 notes |
| FR-025 | Notes are returned sorted by `weight DESC, access_count DESC` (delegated to `GetAll()` ordering in `ListOperator`) | Must | [e2e] | Highest-weight notes appear first in `notes` array |
| FR-026 | `limit` parameter enables tiered loading: `tools/call list {"limit":5}` returns top-5 most important notes | Must | [e2e] | Response `notes` array has length ≤ 5; notes are the highest-weight/access in vault |

### 3.7 Tool: `search_semantic`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-027 | Tool `search_semantic` maps to `SearchSemanticOperator.Execute` | Must | [e2e] | `tools/call search_semantic {"query":"bitcoin","limit":5}` returns semantic results |
| FR-028 | Input schema: `query` (string, required), `limit` (integer, optional, default 10), `folder` (string, optional), `scope` (string, optional, comma-separated note IDs) | Must | [unit] | `tools/list` shows these four parameters |
| FR-029 | `scope` parameter maps to `scopeIds` in `SearchSemanticOperator` — parsed by splitting on `,` | Must | [unit] | `tools/call search_semantic {"query":"q","scope":"id1,id2"}` passes `["id1","id2"]` to operator |

### 3.8 Tool: `search_tags`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-030 | Tool `search_tags` maps to `SearchTagsOperator.Execute` | Must | [e2e] | `tools/call search_tags {"tags":"crypto,defi"}` returns notes matching those tags |
| FR-031 | Input schema: `tags` (string, required, comma-separated), `match` (string, optional, `"any"` or `"all"`, default `"any"`), `limit` (integer, optional, default 10) | Must | [unit] | `tools/list` shows these three parameters |
| FR-032 | `tags` string is split on `,` before passing to operator | Must | [unit] | `"crypto,defi"` → `["crypto","defi"]` passed to `SearchTagsOperator` |

### 3.9 Tool: `search_date`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-033 | Tool `search_date` maps to `SearchDateOperator.Execute` | Must | [e2e] | `tools/call search_date {"from":"2025-01-01"}` returns notes created after that date |
| FR-034 | Input schema: `from` (string, optional, ISO 8601), `to` (string, optional, ISO 8601), `limit` (integer, optional, default 10) | Must | [unit] | `tools/list` shows these three parameters |
| FR-035 | `from`/`to` strings are parsed with `DateTime.Parse(...).ToUniversalTime()` — same as CLI handler | Must | [unit] | `"2025-01-01"` parses to UTC `2025-01-01T00:00:00Z` |
| FR-036 | Both `from` and `to` may be omitted independently; omitted means no bound | Must | [e2e] | `tools/call search_date {}` returns all notes up to `limit` |

### 3.10 Tool: `search_links`

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-037 | Tool `search_links` maps to `SearchLinksOperator.Execute` | Must | [e2e] | `tools/call search_links {"id":"<valid-id>","depth":2}` returns linked notes at depth 2 |
| FR-038 | Input schema: `id` (string, required, note ID or file path), `depth` (integer, optional, default 1) | Must | [unit] | `tools/list` shows these two parameters |
| FR-039 | Source note not found returns `isError: true` with message `Note not found: <id>` | Must | [e2e] | `tools/call search_links {"id":"nonexistent"}` returns error content |

### 3.11 Tool Result Format

| ID | Requirement | Priority | Test Level | Acceptance Criteria |
|----|------------|----------|------------|-------------------|
| FR-040 | All successful tool results are serialized as a single `TextContent` item with `type: "text"` and `text` containing the `MemctlOutcome.Data` serialized as JSON | Must | [e2e] | MCP content array has exactly 1 item; `item.type == "text"` |
| FR-041 | The JSON in `text` is indented (consistent with `ResultPrinter.JsonOpts`) | Should | [unit] | Tool result text is multi-line JSON, not minified |
| FR-042 | Note fields in all results use `NoteToData` shape: `id`, `file`, `title`, `content`, `tags`, `links`, `created`, `modified`, `weight`, `access_count`, and optional `score` | Must | [e2e] | Tool result for any note-returning call contains all listed fields |

---

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|-------------------|
| NFR-001 | Architecture | MCP server lives in `Bootstrap/` layer; tool handler methods call Operators only — no direct index or vault reader access | Code review: `McpServer` class imports Operators, not `INoteIndex` or `IVaultReader` directly |
| NFR-002 | Architecture | No new abstraction layer for MCP — handlers are thin wrappers that invoke existing Operators and format output | McpServer.cs has no business logic beyond parameter parsing and Operator delegation |
| NFR-003 | Startup time | Server starts (is ready to receive `initialize`) within 2 seconds on a machine with the ONNX model already downloaded | Measured: time from process start to first stdout write ≤ 2s |
| NFR-004 | Robustness | Unhandled exceptions in tool handlers are caught and returned as `isError: true` content, not as process crashes | Any exception from an Operator surfaces as MCP error content; process remains alive |
| NFR-005 | Compatibility | MCP protocol version negotiated with client; server accepts `2024-11-05` protocol version | `initialize` response includes `protocolVersion` matching the SDK default |
| NFR-006 | Naming | Tool names use `snake_case` to match MCP convention (`search_semantic`, `search_tags`, `search_date`, `search_links`) | `tools/list` response uses underscored names |
| NFR-007 | No stdout pollution | No log output, ingest progress, or debug messages are written to stdout while the MCP server is running; only JSON-RPC is on stdout | Ingest progress (if any) goes to stderr or is suppressed; stdout is clean JSON-RPC |

---

## 5. Edge Cases & Error Scenarios

1. **Vault does not exist**: `--vault /nonexistent` — auto-ingest will fail; tool call returns `isError: true` with the ingest failure message.
2. **Embedding model not downloaded**: `search` or `search_semantic` called before model exists — `ModelGuard.Check` returns failure; tool returns `isError: true` with `"embedding model not found"` message.
3. **Invalid JSON from client**: MCP SDK handles malformed input at transport layer; server does not crash.
4. **Missing required parameter**: `tools/call get {}` (no `id`) — return `isError: true` with `"id is required"` before calling Operator.
5. **`limit` = 0 or negative**: normalize to 1 (avoid empty or nonsensical results).
6. **`depth` = 0 for `search_links`**: returns empty `results` — valid per `GetLinked` semantics; not an error.
7. **Very large `limit`** (e.g. 10000): pass through to Operator unchanged; SQL handles it via `LIMIT` clause.
8. **Concurrent `tools/call` requests**: MCP SDK may pipeline calls; all Operators are stateless per-call (index is initialized once); no data race on `SqliteNoteIndex` assuming single-writer SQLite WAL mode.
9. **Client sends `initialize` twice**: MCP SDK handles; second `initialize` is a protocol error at SDK level.
10. **`search_date` with `from > to`**: pass through to `SearchByDate`; SQL `WHERE created >= from AND created <= to` naturally returns 0 results — no error.

---

## 6. Out of Scope

- MCP resources (file:// or note:// URIs) — only tools are exposed.
- MCP prompts — not in this task.
- WebSocket or HTTP transport — stdio only.
- Authentication or authorization on the MCP server.
- Streaming results (MCP progress notifications).
- `add`, `add-turn`, `organize`, `weight` as MCP tools — read-only tools only in this task.
- Multi-vault support (single `--vault` per server instance).
- `grep`, `tags`, `stats`, `status` tools — out of scope for initial MCP surface.

---

## 7. Dependencies

- All existing Operators: `GetOperator`, `SearchOperator`, `SearchSemanticOperator`, `SearchTextOperator`, `SearchTagsOperator`, `SearchDateOperator`, `SearchLinksOperator`, `ListOperator`
- `GemmaEmbeddingEngine` and `MemctlConfig.ResolveModelDir` (for lazy embedding init)
- `IngestOperator.NeedsIngest` and `IngestOperator.DbPath`
- `GetOperator.NoteToData` (internal static — may need `internal` visibility or relocation)
- **New NuGet**: `ModelContextProtocol` package (v0.2.0-preview.3 or latest preview) — add to `memctl.csproj`
- `Program.cs` — wire `mcp` subcommand in `Bootstrap/`
- Task #4 (weight/access_count) — complete, fields already in `NoteToData` output
- Task #2 (`--folder`) — complete, `folderPrefix` parameter available on search operators

---

## 8. Open Questions

1. **`GetOperator.NoteToData` visibility**: currently `internal static`. MCP tool handlers in `Bootstrap/` cannot call it directly. Options: (a) move to a shared `NoteMapper` class in `Boundary/`, (b) make it `public`, (c) duplicate in MCP layer. Prefer option (a) — move to `Boundary/NoteMapper.cs` and update all callers. Needs confirmation.

2. **Ingest progress to stderr vs. suppressed**: ingest prints nothing via `ResultPrinter` (it only returns a `MemctlOutcome`), but `GemmaEmbeddingEngine.CreateAsync` may log. Confirm that no stdout pollution occurs from embedding model loading.

3. **`ModelContextProtocol` SDK API surface**: the SDK is in preview; the exact API for registering tools and returning `isError` content needs to be confirmed against the `0.2.0-preview.3` package. Implementer should verify the correct `McpServerTool`, `ToolResponse`, and `CallToolResult` types before coding.

4. **`search_semantic` — does it need embedding model even for `scope`-only calls?**: yes, it still embeds the query string; no special case needed.

---

## 9. QC Checklist

- [ ] FR-001: `memctl mcp --help` exits 0
- [ ] FR-002: `memctl mcp --vault .` blocks on stdin
- [ ] FR-003: `memctl mcp` without `--vault` exits 1 with error message
- [ ] FR-005: `initialize` request on stdin produces valid response on stdout
- [ ] FR-007: `initialize` response has `serverInfo.name == "memctl"`
- [ ] FR-008: `tools/list` returns exactly 7 tools with correct names
- [ ] FR-009: `tools/call get` with valid ID returns note JSON
- [ ] FR-010: `tools/call` with unknown name returns JSON-RPC error response
- [ ] FR-011: Vault path fixed at startup; tool handlers use it without re-reading CLI args
- [ ] FR-012: First tool call on un-ingested vault triggers ingest transparently
- [ ] FR-013: ONNX model not loaded until first semantic tool call
- [ ] FR-014: `search` with `query` and `limit` returns correct result shape
- [ ] FR-015: `tools/list` shows `search` with `query`, `limit`, `folder` params
- [ ] FR-016: `search` result content is `TextContent` with JSON in `text`
- [ ] FR-017: `search` without embedding model returns `isError: true`
- [ ] FR-018: `get` returns full note including `weight`, `access_count`
- [ ] FR-019: `tools/list` shows `get` with `id` as required string
- [ ] FR-020: `get` on nonexistent ID returns `isError: true`
- [ ] FR-021: MCP `get` 3x → `access_count` increments to 3
- [ ] FR-022: `list` returns `count` and `notes` array
- [ ] FR-023: `tools/list` shows `list` with `limit` and `tag` params
- [ ] FR-024: `list {}` returns ≤ 10 notes
- [ ] FR-025: `list` result notes sorted weight DESC, access_count DESC
- [ ] FR-026: `list {"limit":5}` returns ≤ 5 top-importance notes
- [ ] FR-027: `search_semantic` returns vector-ranked results
- [ ] FR-028: `tools/list` shows `search_semantic` with `query`, `limit`, `folder`, `scope`
- [ ] FR-029: `scope` string split on `,` and passed as `scopeIds`
- [ ] FR-030: `search_tags` returns tag-matched notes
- [ ] FR-031: `tools/list` shows `search_tags` with `tags`, `match`, `limit`
- [ ] FR-032: `tags` string split on `,` before operator call
- [ ] FR-033: `search_date {"from":"2025-01-01"}` returns notes from that date
- [ ] FR-034: `tools/list` shows `search_date` with `from`, `to`, `limit`
- [ ] FR-035: date strings parsed to UTC
- [ ] FR-036: `search_date {}` returns all notes up to limit
- [ ] FR-037: `search_links` returns linked notes at requested depth
- [ ] FR-038: `tools/list` shows `search_links` with `id`, `depth`
- [ ] FR-039: `search_links` on nonexistent ID returns `isError: true`
- [ ] FR-040: All tool results are single `TextContent` item
- [ ] FR-042: All note results contain `id`, `file`, `title`, `content`, `tags`, `links`, `created`, `modified`, `weight`, `access_count`
- [ ] NFR-001: McpServer class imports Operators only, not ports directly
- [ ] NFR-004: No Operator exception crashes the process; exception surfaces as `isError: true`
- [ ] NFR-007: No log/progress text on stdout while server is running
