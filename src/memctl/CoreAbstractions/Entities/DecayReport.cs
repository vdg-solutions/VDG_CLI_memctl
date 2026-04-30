namespace Memctl.CoreAbstractions.Entities;

public sealed record DecayReport(
    int   Decayed,
    int   Archived,
    int   Unchanged,
    int   AlreadyArchived,
    bool? AlreadyRunToday,
    bool? DryRun);
