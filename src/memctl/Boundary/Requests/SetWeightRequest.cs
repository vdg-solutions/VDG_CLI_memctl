using System.Text.Json.Serialization;

namespace Memctl.Boundary.Requests;

public sealed class SetWeightRequest
{
    [JsonPropertyName("id")]     public string Id     { get; init; } = "";
    [JsonPropertyName("weight")] public float  Weight { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))   errs.Add("id: required");
        else if (Id.Length > 256)            errs.Add("id: max 256 chars");
        if (Weight is < 0.0f or > 2.0f)      errs.Add("weight: must be in [0.0, 2.0]");
        return errs;
    }
}
