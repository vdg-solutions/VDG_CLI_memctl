namespace Memctl.CoreAbstractions.Entities;

public sealed record IngestReport(
    int     Indexed,
    int     Total,
    string  Vault,
    string  Model,
    string? SemanticLintHint);
