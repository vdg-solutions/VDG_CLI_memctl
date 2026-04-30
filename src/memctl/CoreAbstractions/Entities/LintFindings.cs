using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record LintOrphan(string Id, string Title, string FilePath);

public sealed record LintBrokenLink(string NoteId, string NoteTitle, string BrokenLink);

public sealed record LintDuplicate(
    string NoteAId,
    string NoteATitle,
    string NoteBId,
    string NoteBTitle,
    double Similarity);

public sealed record LintDecayRisk(
    string Id,
    string Title,
    float  Weight,
    int    DaysSinceModified,
    int    InboundLinkCount);

public sealed record LintStructural(
    IReadOnlyList<LintOrphan>     Orphans,
    IReadOnlyList<LintBrokenLink> BrokenLinks,
    IReadOnlyList<LintDuplicate>  Duplicates,
    IReadOnlyList<LintDecayRisk>  DecayRisk);
