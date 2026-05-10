namespace Memctl.CoreAbstractions.Entities;

public sealed record Note
{
    public string   Id         { get; init; } = "";
    public string   FilePath   { get; init; } = "";   // relative to vault
    public string   Title      { get; init; } = "";
    public string   Content    { get; init; } = "";
    public string[] Tags       { get; init; } = [];
    public string[] Links      { get; init; } = [];   // wikilink targets (no brackets)
    public string?  Type       { get; init; }         // user|feedback|project|reference (Claude-Code-compat)
    public DateTime Created    { get; init; }
    public DateTime Modified   { get; init; }
    public float[]? Embedding    { get; init; }
    public float     Weight        { get; init; } = 0.0f;
    public int       AccessCount   { get; init; } = 0;
    public bool      Archived      { get; init; } = false;
    public DateTime? LastWeightSet { get; init; }
}

public static class NoteTypes
{
    public const string User      = "user";
    public const string Feedback  = "feedback";
    public const string Project   = "project";
    public const string Reference = "reference";

    public static readonly string[] All = [User, Feedback, Project, Reference];

    public static bool IsValid(string? type) =>
        type is null || All.Contains(type, StringComparer.OrdinalIgnoreCase);
}
