using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record GrepResult(string Pattern, IReadOnlyList<GrepHit> Hits);
