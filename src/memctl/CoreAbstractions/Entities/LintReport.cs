namespace Memctl.CoreAbstractions.Entities;

public sealed record LintReport(
    LintStructural Structural,
    object?        Semantic);
