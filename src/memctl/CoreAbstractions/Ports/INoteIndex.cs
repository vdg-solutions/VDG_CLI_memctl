using Memctl.CoreAbstractions.Entities;

namespace Memctl.CoreAbstractions.Ports;

public interface INoteIndex : IDisposable
{
    void Initialize(string dbPath);
    void Upsert(Note note);
    void Delete(string noteId);
    Note? GetById(string noteId);
    Note? GetByFilePath(string filePath);
    IReadOnlyList<Note> GetAll(bool includeArchived = false);
    IReadOnlyList<SearchHit> SearchBm25(string query, int limit, string? folderPrefix = null);
    IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null, string? folderPrefix = null);
    // Hybrid retrieval: runs SearchBm25 + SearchSemantic and fuses results via Reciprocal Rank Fusion.
    // Each list contributes 1/(RrfK + rank) per shared hit; results sorted by combined score desc.
    IReadOnlyList<SearchHit> SearchHybrid(string query, float[] queryEmbedding, int limit, string? folderPrefix = null);
    IReadOnlyList<Note> SearchByTags(string[] tags, bool matchAll, int limit);
    IReadOnlyList<Note> SearchByDate(DateTime? from, DateTime? to, int limit);
    IReadOnlyList<Note> GetLinked(string noteId, int depth);
    IReadOnlyList<(string Tag, int Count)> GetTagStats();
    (int NoteCount, int TagCount, int LinkCount, long IndexBytes) GetStats();
    void    SetWeight(string noteId, float weight);
    void    ApplyDecay(string noteId, float newWeight, bool archived);
    void    ApplyDecayBatch(IEnumerable<(string NoteId, float NewWeight, bool Archived)> updates);
    void    IncrementAccess(string noteId);
    IReadOnlyList<(string Id, string FilePath)> GetAllFilePaths();
    void    SetMetadata(string key, string value);
    string? GetMetadata(string key);
}
