using Memctl.CoreAbstractions.Entities;

namespace Memctl.CoreAbstractions.Ports;

public sealed class NoteEnrichment
{
    public string   Title { get; init; } = "";
    public string[] Tags  { get; init; } = [];
    public string[] Links { get; init; } = [];  // link targets (no brackets)
}

public interface ILlmClient
{
    Task<NoteEnrichment> EnrichAsync(string content, IReadOnlyList<Note> existingNotes, CancellationToken ct = default);
}
