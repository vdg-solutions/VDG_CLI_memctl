using System.Text.Json.Serialization;

namespace Memctl.Boundary.Requests;

public sealed class AddNoteRequest
{
    [JsonPropertyName("text")]  public string    Text  { get; init; } = "";
    [JsonPropertyName("title")] public string?   Title { get; init; }
    [JsonPropertyName("tags")]  public string[]? Tags  { get; init; }
    [JsonPropertyName("type")]  public string?   Type  { get; init; }
    [JsonPropertyName("file")]  public string?   File  { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errs = new List<string>();
        if (string.IsNullOrEmpty(Text))      errs.Add("text: required");
        else if (Text.Length > 1_000_000)    errs.Add("text: max 1_000_000 chars");
        if (Title is { Length: > 512 })      errs.Add("title: max 512 chars");
        if (File  is { Length: > 512 })      errs.Add("file: max 512 chars");
        if (!Memctl.CoreAbstractions.Entities.NoteTypes.IsValid(Type))
            errs.Add($"type: must be one of {string.Join(", ", Memctl.CoreAbstractions.Entities.NoteTypes.All)} (got '{Type}')");
        return errs;
    }
}
