using System.Text.Json.Serialization;

namespace Memctl.Boundary.Requests;

public sealed class DecayRequest
{
    [JsonPropertyName("days")]         public int    Days        { get; init; }
    [JsonPropertyName("decay_factor")] public double DecayFactor { get; init; } = 0.9;
    [JsonPropertyName("dry_run")]      public bool   DryRun      { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errs = new List<string>();
        if (Days is < 1 or > 3650)             errs.Add("days: must be in [1, 3650]");
        if (DecayFactor is <= 0.01 or >= 0.99) errs.Add("decay_factor: must be in (0.01, 0.99)");
        return errs;
    }
}
