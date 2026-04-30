namespace Memctl.CoreAbstractions.Entities;

public sealed record ModelEntry(string Name, bool Ready, int SizeMb, bool IsDefault);
