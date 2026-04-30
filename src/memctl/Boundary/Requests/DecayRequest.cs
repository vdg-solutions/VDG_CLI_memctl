using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Memctl.Boundary.Requests;

public sealed class DecayRequest
{
    [JsonPropertyName("days")]
    [Range(1, 3650, ErrorMessage = "days must be in [1, 3650]")]
    public int Days { get; init; }

    [JsonPropertyName("decay_factor")]
    [Range(0.01, 0.99, ErrorMessage = "decay_factor must be in (0.01, 0.99)")]
    public double DecayFactor { get; init; } = 0.9;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; init; }
}
