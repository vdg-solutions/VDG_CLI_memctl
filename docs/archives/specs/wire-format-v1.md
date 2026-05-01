# memctl Wire Format v1

**Status:** Stable
**First shipped:** 2026-04-30 (task #22)

Every CLI command and MCP tool response shares the envelope below. Any change that removes a field or changes a key name is a breaking change and must bump `schema_version` to `2`.

## Envelope

```json
{
  "schema_version": 1,
  "success":        true,
  "action":         "<command-name>",
  "message":        "<human-readable>",
  "data":           <typed payload | null>
}
```

| Field | Type | Notes |
|---|---|---|
| `schema_version` | int | Always `1` for this version. Reserved for forward compatibility. |
| `success`        | bool | `false` only when the operation could not be completed. |
| `action`         | string | Echoes the command/tool name (e.g. `add`, `search`, `set_weight`). |
| `message`        | string | Free-form summary; not part of the contract for parsers. |
| `data`           | object \| null | Typed payload — see DTO catalog below. |

## DTO catalog

All DTOs live in [`src/memctl/Boundary/MemctlResult.cs`](../../src/memctl/Boundary/MemctlResult.cs). Field names are wire-locked via `[JsonPropertyName]`.

| Action(s) | Data type |
|---|---|
| `init` | `VaultRefDto` |
| `add`, `get`, `create`, `update`, `append`, `delete`, `identity (set/get)`, `get_identity` | `NoteDto` |
| `list` | `NoteListResultDto` |
| `tags` | `TagsListResultDto` |
| `grep` | `GrepListResultDto` |
| `stats` | `StatsDto` |
| `status` | `VaultStatusDto` |
| `search`, `search-semantic` / `search_semantic`, `search-text` | `SearchResultDto` |
| `search-tags` / `search_tags` | `SearchTagsResultDto` |
| `search-links` / `search_links` | `SearchLinksResultDto` |
| `search-date` / `search_date` | `SearchDateResultDto` |
| `weight`, `set_weight` | `WeightChangeDto` |
| `decay` | `DecayReportDto` |
| `capture` | `CaptureReportDto` |
| `ingest` | `IngestReportDto` |
| `organize` | `OrganizeReportDto` |
| `model-download` | `ModelInfoDto` |
| `model-list` | `ModelListDto` |
| `model-use` | `ModelSelectionDto` |
| `lint` | `LintReportDto` |
| `hook-status`, `hook_status` | `HookStatusDto` |
| `fetch` | `string` (raw markdown / HTML) |
| `search_help` | `null` (markdown table delivered via `message`) |

## Evolution rules

- Adding a new optional field with a sensible default does **not** bump the version.
- Removing or renaming a field, changing a type, or making an optional field required **does** bump the version.
- Operators may introduce new `data` shapes; the mapper must be extended at the same time. The mapper throws if it sees an unknown runtime type for `outcome.Data` — broken Operators surface fast in tests rather than silently shipping anonymous data.

## Consumers

- CLI: `Bootstrap/ResultPrinter.cs` writes the envelope to stdout via `MemctlResultMapper.ToResult`.
- MCP: `Operators/McpServerOperator.cs` `ToolResult` runs the same mapper before framing the JSON-RPC response.

Both code paths share the same envelope, so a Claude Code MCP client and a shell parser see identical schemas.
