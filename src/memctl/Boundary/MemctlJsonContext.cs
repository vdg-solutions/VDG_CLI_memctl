using System.Text.Json;
using System.Text.Json.Serialization;
using Memctl.Boundary.Llm;
using Memctl.Boundary.Mcp;
using Memctl.Boundary.Requests;
using Memctl.Implementations.Config;

namespace Memctl.Boundary;

[JsonSerializable(typeof(MemctlResult))]
[JsonSerializable(typeof(NoteDto))]
[JsonSerializable(typeof(SearchResultDto))]
[JsonSerializable(typeof(TagDto))]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(GrepHitDto))]
[JsonSerializable(typeof(SearchTagsResultDto))]
[JsonSerializable(typeof(SearchLinksResultDto))]
[JsonSerializable(typeof(SearchDateResultDto))]
[JsonSerializable(typeof(NoteListResultDto))]
[JsonSerializable(typeof(TagsListResultDto))]
[JsonSerializable(typeof(GrepListResultDto))]
[JsonSerializable(typeof(WeightChangeDto))]
[JsonSerializable(typeof(DecayReportDto))]
[JsonSerializable(typeof(MaintainResultDto))]
[JsonSerializable(typeof(VaultStatusDto))]
[JsonSerializable(typeof(CaptureReportDto))]
[JsonSerializable(typeof(IngestReportDto))]
[JsonSerializable(typeof(OrganizeReportDto))]
[JsonSerializable(typeof(ModelInfoDto))]
[JsonSerializable(typeof(ModelEntryDto))]
[JsonSerializable(typeof(ModelListDto))]
[JsonSerializable(typeof(ModelSelectionDto))]
[JsonSerializable(typeof(VaultRefDto))]
[JsonSerializable(typeof(LintReportDto))]
[JsonSerializable(typeof(LintStructuralDto))]
[JsonSerializable(typeof(LintOrphanDto))]
[JsonSerializable(typeof(LintBrokenLinkDto))]
[JsonSerializable(typeof(LintDuplicateDto))]
[JsonSerializable(typeof(LintDecayRiskDto))]
[JsonSerializable(typeof(HookStatusDto))]
[JsonSerializable(typeof(HookLogEntryDto))]
[JsonSerializable(typeof(MigrateTagsReportDto))]
[JsonSerializable(typeof(SetWeightRequest))]
[JsonSerializable(typeof(DecayRequest))]
[JsonSerializable(typeof(AddNoteRequest))]
[JsonSerializable(typeof(MemctlConfig))]
[JsonSerializable(typeof(CapturePayload))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(McpResponse<InitializeResult>))]
[JsonSerializable(typeof(McpResponse<ToolsListResult>))]
[JsonSerializable(typeof(McpResponse<ToolCallResult>))]
[JsonSerializable(typeof(McpResponse<EmptyResult>))]
[JsonSerializable(typeof(McpErrorResponse))]
[JsonSourceGenerationOptions(
    WriteIndented        = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class MemctlJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(McpResponse<InitializeResult>))]
[JsonSerializable(typeof(McpResponse<ToolsListResult>))]
[JsonSerializable(typeof(McpResponse<ToolCallResult>))]
[JsonSerializable(typeof(McpResponse<EmptyResult>))]
[JsonSerializable(typeof(McpErrorResponse))]
[JsonSerializable(typeof(MemctlResult))]
[JsonSerializable(typeof(JsonElement))]
[JsonSourceGenerationOptions(
    WriteIndented        = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class McpJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class OpenAiJsonContext : JsonSerializerContext { }
