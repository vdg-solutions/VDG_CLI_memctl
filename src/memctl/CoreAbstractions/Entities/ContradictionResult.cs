namespace Memctl.CoreAbstractions.Entities;

public enum ContradictionResolution { KeepNew, KeepExisting, Merge }

public sealed record ContradictionResult(
    bool                    Contradicts,
    string?                 ExistingId,
    ContradictionResolution Resolution,
    string?                 MergedContent,
    string                  Rationale);
