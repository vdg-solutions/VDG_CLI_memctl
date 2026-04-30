namespace Memctl.CoreAbstractions.Entities;

public sealed record CaptureReport(
    bool    DryRun,
    string  FilePath,
    int     Turns,
    float?  Weight);
