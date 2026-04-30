using System.Text.Json.Serialization;

namespace Memctl.Boundary;

public sealed class MemctlResult
{
    [JsonPropertyName("success")] public bool    Success { get; init; }
    [JsonPropertyName("action")]  public string  Action  { get; init; } = "";
    [JsonPropertyName("message")] public string  Message { get; init; } = "";
    [JsonPropertyName("data")]    public object? Data    { get; init; }
}

public sealed class NoteDto
{
    [JsonPropertyName("id")]           public string   Id          { get; init; } = "";
    [JsonPropertyName("file")]         public string   File        { get; init; } = "";
    [JsonPropertyName("title")]        public string   Title       { get; init; } = "";
    [JsonPropertyName("content")]      public string?  Content     { get; init; }
    [JsonPropertyName("snippet")]      public string?  Snippet     { get; init; }
    [JsonPropertyName("tags")]         public string[] Tags        { get; init; } = [];
    [JsonPropertyName("links")]        public string[] Links       { get; init; } = [];
    [JsonPropertyName("created")]      public string?  Created     { get; init; }
    [JsonPropertyName("modified")]     public string?  Modified    { get; init; }
    [JsonPropertyName("weight")]       public float?   Weight      { get; init; }
    [JsonPropertyName("access_count")] public int?     AccessCount { get; init; }
    [JsonPropertyName("score")]        public float?   Score       { get; init; }
}

public sealed class SearchResultDto
{
    [JsonPropertyName("query")]   public string   Query   { get; init; } = "";
    [JsonPropertyName("count")]   public int      Count   { get; init; }
    [JsonPropertyName("results")] public NoteDto[] Results { get; init; } = [];
}

public sealed class TagDto
{
    [JsonPropertyName("tag")]   public string Tag   { get; init; } = "";
    [JsonPropertyName("count")] public int    Count { get; init; }
}

public sealed class StatsDto
{
    [JsonPropertyName("note_count")]  public int  NoteCount  { get; init; }
    [JsonPropertyName("tag_count")]   public int  TagCount   { get; init; }
    [JsonPropertyName("link_count")]  public int  LinkCount  { get; init; }
    [JsonPropertyName("index_bytes")] public long IndexBytes { get; init; }
    [JsonPropertyName("vault_path")]  public string VaultPath { get; init; } = "";
}

public sealed class GrepHitDto
{
    [JsonPropertyName("file")]    public string File    { get; init; } = "";
    [JsonPropertyName("line")]    public int    Line    { get; init; }
    [JsonPropertyName("content")] public string Content { get; init; } = "";
}

public sealed class SearchTagsResultDto
{
    [JsonPropertyName("tags")]      public string[]  Tags     { get; init; } = [];
    [JsonPropertyName("match_all")] public bool      MatchAll { get; init; }
    [JsonPropertyName("count")]     public int       Count    { get; init; }
    [JsonPropertyName("results")]   public NoteDto[] Results  { get; init; } = [];
}

public sealed class SearchLinksResultDto
{
    [JsonPropertyName("source_id")] public string    SourceId { get; init; } = "";
    [JsonPropertyName("depth")]     public int       Depth    { get; init; }
    [JsonPropertyName("count")]     public int       Count    { get; init; }
    [JsonPropertyName("results")]   public NoteDto[] Results  { get; init; } = [];
}

public sealed class SearchDateResultDto
{
    [JsonPropertyName("from")]    public string?   From    { get; init; }
    [JsonPropertyName("to")]      public string?   To      { get; init; }
    [JsonPropertyName("count")]   public int       Count   { get; init; }
    [JsonPropertyName("results")] public NoteDto[] Results { get; init; } = [];
}

public sealed class NoteListResultDto
{
    [JsonPropertyName("count")] public int       Count { get; init; }
    [JsonPropertyName("notes")] public NoteDto[] Notes { get; init; } = [];
}

public sealed class TagsListResultDto
{
    [JsonPropertyName("count")] public int      Count { get; init; }
    [JsonPropertyName("tags")]  public TagDto[] Tags  { get; init; } = [];
}

public sealed class GrepListResultDto
{
    [JsonPropertyName("pattern")] public string?      Pattern { get; init; }
    [JsonPropertyName("count")]   public int          Count   { get; init; }
    [JsonPropertyName("hits")]    public GrepHitDto[] Hits    { get; init; } = [];
}

public sealed class WeightChangeDto
{
    [JsonPropertyName("id")]     public string Id     { get; init; } = "";
    [JsonPropertyName("file")]   public string File   { get; init; } = "";
    [JsonPropertyName("weight")] public float  Weight { get; init; }
}

public sealed class DecayReportDto
{
    [JsonPropertyName("decayed")]           public int   Decayed          { get; init; }
    [JsonPropertyName("archived")]          public int   Archived         { get; init; }
    [JsonPropertyName("unchanged")]         public int   Unchanged        { get; init; }
    [JsonPropertyName("already_archived")]  public int   AlreadyArchived  { get; init; }
    [JsonPropertyName("already_run_today")] public bool? AlreadyRunToday  { get; init; }
    [JsonPropertyName("dry_run")]           public bool? DryRun           { get; init; }
}

public sealed class VaultStatusDto
{
    [JsonPropertyName("model_ready")]     public bool      ModelReady     { get; init; }
    [JsonPropertyName("model_path")]      public string    ModelPath      { get; init; } = "";
    [JsonPropertyName("model_size_mb")]   public int       ModelSizeMb    { get; init; }
    [JsonPropertyName("vault_exists")]    public bool      VaultExists    { get; init; }
    [JsonPropertyName("vault_indexed")]   public bool      VaultIndexed   { get; init; }
    [JsonPropertyName("note_count")]      public int       NoteCount      { get; init; }
    [JsonPropertyName("index_path")]      public string    IndexPath      { get; init; } = "";
    [JsonPropertyName("vault_found")]     public bool      VaultFound     { get; init; } = true;
    [JsonPropertyName("search_path")]     public string?   SearchPath     { get; init; }
    [JsonPropertyName("search_strategy")] public string?   SearchStrategy { get; init; }
    [JsonPropertyName("checked_paths")]   public string[]? CheckedPaths   { get; init; }
    [JsonPropertyName("hint")]            public string?   Hint           { get; init; }
}

public sealed class CaptureReportDto
{
    [JsonPropertyName("dryRun")] public bool   DryRun { get; init; }
    [JsonPropertyName("file")]   public string File   { get; init; } = "";
    [JsonPropertyName("turns")]  public int    Turns  { get; init; }
    [JsonPropertyName("weight")] public float? Weight { get; init; }
}

public sealed class IngestReportDto
{
    [JsonPropertyName("indexed")]            public int     Indexed          { get; init; }
    [JsonPropertyName("total")]              public int     Total            { get; init; }
    [JsonPropertyName("vault")]              public string  Vault            { get; init; } = "";
    [JsonPropertyName("model")]              public string  Model            { get; init; } = "";
    [JsonPropertyName("semantic_lint_hint")] public string? SemanticLintHint { get; init; }
}

public sealed class OrganizeReportDto
{
    [JsonPropertyName("updated")] public int    Updated { get; init; }
    [JsonPropertyName("errors")]  public int    Errors  { get; init; }
    [JsonPropertyName("vault")]   public string Vault   { get; init; } = "";
}

public sealed class ModelInfoDto
{
    [JsonPropertyName("model_path")]    public string ModelPath   { get; init; } = "";
    [JsonPropertyName("model_size_mb")] public int    ModelSizeMb { get; init; }
}

public sealed class ModelEntryDto
{
    [JsonPropertyName("name")]       public string Name      { get; init; } = "";
    [JsonPropertyName("ready")]      public bool   Ready     { get; init; }
    [JsonPropertyName("size_mb")]    public int    SizeMb    { get; init; }
    [JsonPropertyName("is_default")] public bool   IsDefault { get; init; }
}

public sealed class ModelListDto
{
    [JsonPropertyName("default_model")] public string          DefaultModel { get; init; } = "";
    [JsonPropertyName("models")]        public ModelEntryDto[] Models       { get; init; } = [];
}

public sealed class ModelSelectionDto
{
    [JsonPropertyName("model")] public string Model { get; init; } = "";
}

public sealed class LintOrphanDto
{
    [JsonPropertyName("id")]        public string Id       { get; init; } = "";
    [JsonPropertyName("title")]     public string Title    { get; init; } = "";
    [JsonPropertyName("file_path")] public string FilePath { get; init; } = "";
}

public sealed class LintBrokenLinkDto
{
    [JsonPropertyName("note_id")]     public string NoteId     { get; init; } = "";
    [JsonPropertyName("note_title")]  public string NoteTitle  { get; init; } = "";
    [JsonPropertyName("broken_link")] public string BrokenLink { get; init; } = "";
}

public sealed class LintDuplicateDto
{
    [JsonPropertyName("note_a_id")]    public string NoteAId     { get; init; } = "";
    [JsonPropertyName("note_a_title")] public string NoteATitle  { get; init; } = "";
    [JsonPropertyName("note_b_id")]    public string NoteBId     { get; init; } = "";
    [JsonPropertyName("note_b_title")] public string NoteBTitle  { get; init; } = "";
    [JsonPropertyName("similarity")]   public double Similarity  { get; init; }
}

public sealed class LintDecayRiskDto
{
    [JsonPropertyName("id")]                  public string Id                { get; init; } = "";
    [JsonPropertyName("title")]               public string Title             { get; init; } = "";
    [JsonPropertyName("weight")]              public float  Weight            { get; init; }
    [JsonPropertyName("days_since_modified")] public int    DaysSinceModified { get; init; }
    [JsonPropertyName("inbound_link_count")]  public int    InboundLinkCount  { get; init; }
}

public sealed class LintStructuralDto
{
    [JsonPropertyName("orphans")]      public LintOrphanDto[]     Orphans     { get; init; } = [];
    [JsonPropertyName("broken_links")] public LintBrokenLinkDto[] BrokenLinks { get; init; } = [];
    [JsonPropertyName("duplicates")]   public LintDuplicateDto[]  Duplicates  { get; init; } = [];
    [JsonPropertyName("decay_risk")]   public LintDecayRiskDto[]  DecayRisk   { get; init; } = [];
}

public sealed class LintReportDto
{
    [JsonPropertyName("structural")] public LintStructuralDto Structural { get; init; } = new();
    [JsonPropertyName("semantic")]   public object?           Semantic   { get; init; }
}

public sealed class VaultRefDto
{
    [JsonPropertyName("vault")] public string Vault { get; init; } = "";
}

public sealed class HookLogEntryDto
{
    [JsonPropertyName("timestamp")] public string  Timestamp { get; init; } = "";
    [JsonPropertyName("action")]    public string  Action    { get; init; } = "";
    [JsonPropertyName("success")]   public bool    Success   { get; init; }
    [JsonPropertyName("error")]     public string? Error     { get; init; }
}

public sealed class HookStatusDto
{
    [JsonPropertyName("log_path")]       public string             LogPath       { get; init; } = "";
    [JsonPropertyName("log_exists")]     public bool               LogExists     { get; init; }
    [JsonPropertyName("recent_success")] public int                RecentSuccess { get; init; }
    [JsonPropertyName("recent_fail")]    public int                RecentFail    { get; init; }
    [JsonPropertyName("last_error")]     public string?            LastError     { get; init; }
    [JsonPropertyName("last_error_at")]  public string?            LastErrorAt   { get; init; }
    [JsonPropertyName("last_entries")]   public HookLogEntryDto[]  LastEntries   { get; init; } = [];
}
