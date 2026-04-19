namespace Memctl.CoreAbstractions.Entities;

public sealed record Note
{
    public string   Id         { get; init; } = "";
    public string   FilePath   { get; init; } = "";   // relative to vault
    public string   Title      { get; init; } = "";
    public string   Content    { get; init; } = "";
    public string[] Tags       { get; init; } = [];
    public string[] Links      { get; init; } = [];   // wikilink targets (no brackets)
    public DateTime Created    { get; init; }
    public DateTime Modified   { get; init; }
    public float[]? Embedding    { get; init; }
    public float     Weight        { get; init; } = 0.0f;
    public int       AccessCount   { get; init; } = 0;
    public bool      Archived      { get; init; } = false;
    public DateTime? LastWeightSet { get; init; }
}
