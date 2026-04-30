using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record SearchTagsHitsResult(
    string[]            Tags,
    bool                MatchAll,
    IReadOnlyList<Note> Notes);
