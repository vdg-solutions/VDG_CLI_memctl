namespace Memctl.CoreAbstractions.Entities;

public sealed class SearchHit
{
    public Note   Note    { get; init; } = null!;
    public float  Score   { get; init; }
    public string? Snippet { get; init; }
}
