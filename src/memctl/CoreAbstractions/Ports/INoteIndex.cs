using Memctl.CoreAbstractions.Entities;

namespace Memctl.CoreAbstractions.Ports;

public interface INoteIndex : IDisposable
{
    void Initialize(string dbPath);
    void Upsert(Note note);
    void Delete(string noteId);
    Note? GetById(string noteId);
    Note? GetByFilePath(string filePath);
    IReadOnlyList<Note> GetAll();
    IReadOnlyList<SearchHit> SearchBm25(string query, int limit, string? folderPrefix = null);
    IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null, string? folderPrefix = null);
    IReadOnlyList<Note> SearchByTags(string[] tags, bool matchAll, int limit);
    IReadOnlyList<Note> SearchByDate(DateTime? from, DateTime? to, int limit);
    IReadOnlyList<Note> GetLinked(string noteId, int depth);
    IReadOnlyList<(string Tag, int Count)> GetTagStats();
    (int NoteCount, int TagCount, int LinkCount, long IndexBytes) GetStats();
    void   SetMetadata(string key, string value);
    string? GetMetadata(string key);
}
