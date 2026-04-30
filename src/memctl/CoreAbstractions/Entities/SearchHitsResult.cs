using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record SearchHitsResult(
    string                   Query,
    IReadOnlyList<SearchHit> Hits);
