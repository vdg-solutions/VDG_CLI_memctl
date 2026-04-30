namespace Memctl.CoreAbstractions.Entities;

public sealed record VaultStats(
    int    NoteCount,
    int    TagCount,
    int    LinkCount,
    long   IndexBytes,
    string VaultPath);
