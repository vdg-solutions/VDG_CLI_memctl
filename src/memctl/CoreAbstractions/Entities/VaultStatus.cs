using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record VaultStatus(
    bool                    ModelReady,
    string                  ModelPath,
    int                     ModelSizeMb,
    bool                    VaultExists,
    bool                    VaultIndexed,
    int                     NoteCount,
    string                  IndexPath,
    bool                    VaultFound       = true,
    string?                 SearchPath       = null,
    string?                 SearchStrategy   = null,
    IReadOnlyList<string>?  CheckedPaths     = null,
    string?                 Hint             = null);
