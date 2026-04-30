using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record ModelList(string DefaultModel, IReadOnlyList<ModelEntry> Models);
