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
    public string?   Tier          { get; init; }   // null | L0 | L1 | L2 | L3 (see NoteTiers); null = pre-tier note treated as L0 by distill
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

// Tier classifies notes by abstraction level on the memory pyramid (TencentDB-Agent-Memory
// inspired). DistillOperator promotes notes upward (L0 raw -> L1 atoms -> L2 scenarios -> L3
// persona) via LLM passes. Distinct from NoteTypes (which classifies content category) and
// from cascade scope layers (cwd / agent instance / machine shared, which are folder paths).
public static class NoteTiers
{
    public const string L0Raw      = "L0";
    public const string L1Atom     = "L1";
    public const string L2Scenario = "L2";
    public const string L3Persona  = "L3";

    public static readonly string[] All = [L0Raw, L1Atom, L2Scenario, L3Persona];

    public static bool IsValid(string? tier) =>
        tier is null || All.Contains(tier, StringComparer.OrdinalIgnoreCase);
}
