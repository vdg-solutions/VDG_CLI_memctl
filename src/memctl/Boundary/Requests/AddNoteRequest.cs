using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Memctl.Boundary.Requests;

public sealed class AddNoteRequest
{
    [JsonPropertyName("text")]
    [Required, StringLength(1_000_000, MinimumLength = 1, ErrorMessage = "text must be 1..1_000_000 chars")]
    public string Text { get; init; } = "";

    [JsonPropertyName("title")]
    [StringLength(512)]
    public string? Title { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("file")]
    [StringLength(512)]
    public string? File { get; init; }
}
