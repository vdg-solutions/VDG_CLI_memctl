using System.Text.Json.Serialization;

namespace Memctl.Boundary;

public sealed record CapturePayload(
    [property: JsonPropertyName("session_id")] string?           SessionId,
    [property: JsonPropertyName("cwd")]        string?           Cwd,
    [property: JsonPropertyName("transcript")] TranscriptTurn[]? Transcript);

public sealed record TranscriptTurn(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);
