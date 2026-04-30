namespace Memctl.CoreAbstractions.Entities;

public sealed record GrepHit(string FilePath, int LineNumber, string Content);
