using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record SearchLinksHitsResult(
    string              SourceId,
    int                 Depth,
    IReadOnlyList<Note> Notes);
