using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record SearchDateHitsResult(
    string?              From,
    string?              To,
    IReadOnlyList<Note>  Notes);
