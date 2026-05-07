namespace Memctl.CoreAbstractions.Entities;

public sealed record DistilledNote(
    string   Type,
    string   Title,
    string   Content,
    string[] Tags,
    string[] Links,
    float    Weight,
    string   Rationale);

public sealed record DistillResult(DistilledNote[] Extractions);
