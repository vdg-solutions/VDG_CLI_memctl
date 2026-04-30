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
    [JsonPropertyName("id")]       public string   Id       { get; init; } = "";
    [JsonPropertyName("file")]     public string   File     { get; init; } = "";
    [JsonPropertyName("title")]    public string   Title    { get; init; } = "";
    [JsonPropertyName("content")]  public string?  Content  { get; init; }
    [JsonPropertyName("snippet")]  public string?  Snippet  { get; init; }
    [JsonPropertyName("tags")]     public string[] Tags     { get; init; } = [];
    [JsonPropertyName("links")]    public string[] Links    { get; init; } = [];
    [JsonPropertyName("created")]  public string?  Created  { get; init; }
    [JsonPropertyName("modified")] public string?  Modified { get; init; }
    [JsonPropertyName("score")]    public float?   Score    { get; init; }
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
    [JsonPropertyName("count")] public int          Count { get; init; }
    [JsonPropertyName("hits")]  public GrepHitDto[] Hits  { get; init; } = [];
}
