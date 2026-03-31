namespace Memctl.Boundary.Options;

public sealed class GlobalOptions
{
    public string? Vault  { get; init; }
    public string? LlmUrl    { get; init; }
    public string? LlmModel  { get; init; }
    public string? LlmKey    { get; init; }
    public int     Limit     { get; init; } = 10;
    public string? ModelDir  { get; init; }
}
