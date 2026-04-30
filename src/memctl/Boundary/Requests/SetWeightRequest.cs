using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Memctl.Boundary.Requests;

public sealed class SetWeightRequest
{
    [JsonPropertyName("id")]
    [Required, StringLength(256, MinimumLength = 1)]
    public string Id { get; init; } = "";

    [JsonPropertyName("weight")]
    [Range(0.0, 2.0, ErrorMessage = "Weight must be in [0.0, 2.0]")]
    public float Weight { get; init; }
}
