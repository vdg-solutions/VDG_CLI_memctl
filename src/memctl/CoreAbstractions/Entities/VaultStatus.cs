namespace Memctl.CoreAbstractions.Entities;

public sealed record VaultStatus(
    bool   ModelReady,
    string ModelPath,
    int    ModelSizeMb,
    bool   VaultExists,
    bool   VaultIndexed,
    int    NoteCount,
    string IndexPath);
