using System.Runtime.InteropServices;
using System.Text.Json;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Microsoft.Data.Sqlite;

namespace Memctl.Implementations.Index;

public sealed class SqliteNoteIndex : INoteIndex
{
    private SqliteConnection? _db;

    public void Initialize(string dbPath)
    {
        if (_db is not null) return;  // already initialized (e.g. MCP long-running server)
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        ApplySchema();
    }

    public void Upsert(Note note)
    {
        var tagsJson       = JsonSerializer.Serialize(note.Tags);
        var linksJson      = JsonSerializer.Serialize(note.Links);
        var embeddingBytes = EmbeddingToBytes(note.Embedding);

        // weight in INSERT (sets initial value) but excluded from UPDATE — preserve user-set values on re-ingest
        Exec(@"
            INSERT INTO notes (id, file_path, title, content, tags, links, created_at, modified_at, embedding, weight)
            VALUES (@id, @fp, @title, @content, @tags, @links, @created, @modified, @emb, @weight)
            ON CONFLICT(id) DO UPDATE SET
                file_path   = excluded.file_path,
                title       = excluded.title,
                content     = excluded.content,
                tags        = excluded.tags,
                links       = excluded.links,
                modified_at = excluded.modified_at,
                embedding   = excluded.embedding",
            ("@id",      note.Id),
            ("@fp",      note.FilePath),
            ("@title",   note.Title),
            ("@content", note.Content),
            ("@tags",    tagsJson),
            ("@links",   linksJson),
            ("@created", note.Created.ToString("O")),
            ("@modified",note.Modified.ToString("O")),
            ("@emb",     (object?)embeddingBytes ?? DBNull.Value),
            ("@weight",  note.Weight));
    }

    public void Delete(string noteId) =>
        Exec("DELETE FROM notes WHERE id = @id", ("@id", noteId));

    public Note? GetById(string noteId) =>
        QueryOne("SELECT * FROM notes WHERE id = @id", ("@id", noteId));

    public Note? GetByFilePath(string filePath) =>
        QueryOne("SELECT * FROM notes WHERE file_path = @fp", ("@fp", filePath));

    public IReadOnlyList<Note> GetAll(bool includeArchived = false)
    {
        var sql = includeArchived
            ? "SELECT * FROM notes ORDER BY weight DESC, access_count DESC"
            : "SELECT * FROM notes WHERE archived = 0 ORDER BY weight DESC, access_count DESC";
        return QueryMany(sql);
    }

    public IReadOnlyList<SearchHit> SearchBm25(string query, int limit, string? folderPrefix = null)
    {
        var prefix       = NormalizePrefix(folderPrefix);
        var folderClause = prefix is not null ? " AND n.file_path LIKE @prefix" : "";
        using var cmd = Cmd($@"
            SELECT n.*, bm25(notes_fts) AS rank
            FROM notes_fts f
            JOIN notes n ON n.rowid = f.rowid
            WHERE notes_fts MATCH @q{folderClause}
            ORDER BY rank
            LIMIT @limit",
            ("@q", EscapeFts(query)),
            ("@limit", limit));
        if (prefix is not null) cmd.Parameters.AddWithValue("@prefix", prefix);

        var hits = new List<SearchHit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var note = ReadNote(r);
            hits.Add(new SearchHit
            {
                Note    = note,
                Score   = r.IsDBNull(r.GetOrdinal("rank")) ? 0f : (float)Math.Abs(r.GetDouble(r.GetOrdinal("rank"))),
                Snippet = Snippet(note.Content, query),
            });
        }
        return hits;
    }

    public IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null, string? folderPrefix = null)
    {
        var prefix = NormalizePrefix(folderPrefix);
        var conditions = new List<string> { "embedding IS NOT NULL" };
        if (prefix is not null) conditions.Add("file_path LIKE @prefix");
        if (scopeIds is { Count: > 0 }) conditions.Add($"id IN ({string.Join(',', scopeIds.Select((_, i) => $"@id{i}"))})");

        var sql = $"SELECT * FROM notes WHERE {string.Join(" AND ", conditions)}";
        using var cmd = Cmd(sql);
        if (prefix is not null) cmd.Parameters.AddWithValue("@prefix", prefix);
        if (scopeIds is { Count: > 0 })
            for (var i = 0; i < scopeIds.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", scopeIds[i]);

        var candidates = new List<(Note note, float score)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var note = ReadNote(r);
            if (note.Embedding is null) continue;
            var score = CosineSimilarity(queryEmbedding, note.Embedding);
            candidates.Add((note, score));
        }

        return [.. candidates
            .OrderByDescending(c => c.score)
            .Take(limit)
            .Select(c => new SearchHit { Note = c.note, Score = c.score, Snippet = null })];
    }

    public IReadOnlyList<Note> SearchByTags(string[] tags, bool matchAll, int limit)
    {
        if (tags.Length == 0) return [];

        // json_each on tags column
        var conditions = tags.Select((_, i) => $"lower(t{i}.value) = lower(@tag{i})");
        var joins      = tags.Select((_, i) => $"JOIN json_each(n.tags) t{i}").ToList();
        var where      = matchAll
            ? string.Join(" AND ", conditions)
            : string.Join(" OR ", conditions);

        var sql = $"SELECT DISTINCT n.* FROM notes n {string.Join(' ', joins)} WHERE {where} LIMIT @limit";
        using var cmd = Cmd(sql);
        for (var i = 0; i < tags.Length; i++) cmd.Parameters.AddWithValue($"@tag{i}", tags[i]);
        cmd.Parameters.AddWithValue("@limit", limit);

        var notes = new List<Note>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) notes.Add(ReadNote(r));
        return notes;
    }

    public IReadOnlyList<Note> SearchByDate(DateTime? from, DateTime? to, int limit)
    {
        var sql = "SELECT * FROM notes WHERE 1=1";
        var ps  = new List<(string, object)>();

        if (from.HasValue) { sql += " AND created_at >= @from"; ps.Add(("@from", from.Value.ToString("O"))); }
        if (to.HasValue)   { sql += " AND created_at <= @to";   ps.Add(("@to",   to.Value.ToString("O"))); }
        sql += " ORDER BY created_at DESC LIMIT @limit";
        ps.Add(("@limit", limit));

        return QueryMany(sql, [.. ps]);
    }

    public IReadOnlyList<Note> GetLinked(string noteId, int depth)
    {
        var visited = new HashSet<string> { noteId };
        var result  = new List<Note>();
        var queue   = new Queue<string>();
        queue.Enqueue(noteId);

        for (var d = 0; d < depth && queue.Count > 0; d++)
        {
            var level = queue.Count;
            for (var i = 0; i < level; i++)
            {
                var current = queue.Dequeue();
                var note    = GetById(current);
                if (note is null) continue;

                // Notes that this note links to
                foreach (var link in note.Links)
                {
                    using var cmd = Cmd(
                        "SELECT * FROM notes WHERE lower(title) = lower(@title) LIMIT 1",
                        ("@title", link));
                    using var r = cmd.ExecuteReader();
                    if (!r.Read()) continue;
                    var linked = ReadNote(r);
                    if (visited.Add(linked.Id)) { result.Add(linked); queue.Enqueue(linked.Id); }
                }

                // Notes that link TO this note
                using var back = Cmd(
                    "SELECT n.* FROM notes n, json_each(n.links) l WHERE lower(l.value) = lower(@title)",
                    ("@title", note.Title));
                using var br = back.ExecuteReader();
                while (br.Read())
                {
                    var backlinked = ReadNote(br);
                    if (visited.Add(backlinked.Id)) { result.Add(backlinked); queue.Enqueue(backlinked.Id); }
                }
            }
        }

        return result;
    }

    public IReadOnlyList<(string Tag, int Count)> GetTagStats()
    {
        using var cmd = Cmd(@"
            SELECT lower(t.value) AS tag, COUNT(*) AS cnt
            FROM notes n, json_each(n.tags) t
            GROUP BY lower(t.value)
            ORDER BY cnt DESC");

        var stats = new List<(string, int)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) stats.Add((r.GetString(0), r.GetInt32(1)));
        return stats;
    }

    public (int NoteCount, int TagCount, int LinkCount, long IndexBytes) GetStats()
    {
        using var cmd = Cmd(@"
            SELECT
                (SELECT COUNT(*) FROM notes),
                (SELECT COUNT(DISTINCT lower(t.value)) FROM notes n, json_each(n.tags) t),
                (SELECT COUNT(*) FROM notes n, json_each(n.links) l)");

        using var r = cmd.ExecuteReader();
        r.Read();
        var noteCount = r.GetInt32(0);
        var tagCount  = r.GetInt32(1);
        var linkCount = r.GetInt32(2);

        var dbPath = _db!.DataSource;
        var bytes  = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0L;

        return (noteCount, tagCount, linkCount, bytes);
    }

    public void SetWeight(string noteId, float weight)
    {
        var clamped = Math.Clamp(weight, 0.0f, 2.0f);
        var now     = DateTime.UtcNow.ToString("O");
        Exec("UPDATE notes SET weight = @w, last_weight_set = @lws WHERE id = @id",
            ("@w", clamped), ("@lws", now), ("@id", noteId));
    }

    public void ApplyDecay(string noteId, float newWeight, bool archived)
    {
        var arch = archived ? 1 : 0;
        Exec("UPDATE notes SET weight = @w, archived = @arch WHERE id = @id",
            ("@w", newWeight), ("@arch", arch), ("@id", noteId));
    }

    public void ApplyDecayBatch(IEnumerable<(string NoteId, float NewWeight, bool Archived)> updates)
    {
        using var tx = _db!.BeginTransaction();
        foreach (var (noteId, newWeight, archived) in updates)
        {
            var arch = archived ? 1 : 0;
            using var cmd = Cmd("UPDATE notes SET weight = @w, archived = @arch WHERE id = @id",
                ("@w", newWeight), ("@arch", arch), ("@id", noteId));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void IncrementAccess(string noteId) =>
        Exec("UPDATE notes SET access_count = access_count + 1 WHERE id = @id", ("@id", noteId));

    public void Dispose() => _db?.Dispose();

    public void SetMetadata(string key, string value) =>
        Exec("INSERT INTO metadata (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("@k", key), ("@v", value));

    public string? GetMetadata(string key)
    {
        using var cmd = Cmd("SELECT value FROM metadata WHERE key = @k", ("@k", key));
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    // --- schema ---

    private void ApplySchema()
    {
        ExecOne("CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        ExecOne(@"CREATE TABLE IF NOT EXISTS notes (
            id          TEXT PRIMARY KEY,
            file_path   TEXT NOT NULL UNIQUE,
            title       TEXT NOT NULL,
            content     TEXT NOT NULL,
            tags        TEXT NOT NULL DEFAULT '[]',
            links       TEXT NOT NULL DEFAULT '[]',
            created_at  TEXT NOT NULL,
            modified_at TEXT NOT NULL,
            embedding   BLOB
        )");
        ExecOne(@"CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
            id UNINDEXED, title, content,
            content='notes', content_rowid='rowid'
        )");
        ExecOne(@"CREATE TRIGGER IF NOT EXISTS notes_ai AFTER INSERT ON notes BEGIN
            INSERT INTO notes_fts(rowid, id, title, content)
            VALUES (new.rowid, new.id, new.title, new.content);
        END");
        ExecOne(@"CREATE TRIGGER IF NOT EXISTS notes_au AFTER UPDATE ON notes BEGIN
            INSERT INTO notes_fts(notes_fts, rowid, id, title, content)
            VALUES ('delete', old.rowid, old.id, old.title, old.content);
            INSERT INTO notes_fts(rowid, id, title, content)
            VALUES (new.rowid, new.id, new.title, new.content);
        END");
        ExecOne(@"CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
            INSERT INTO notes_fts(notes_fts, rowid, id, title, content)
            VALUES ('delete', old.rowid, old.id, old.title, old.content);
        END");

        // idempotent column migrations for existing databases
        MigrateAddColumn("weight",          "REAL    NOT NULL DEFAULT 0.0");
        MigrateAddColumn("access_count",    "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumn("archived",        "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumn("last_weight_set", "TEXT");
    }

    private void MigrateAddColumn(string column, string definition)
    {
        using var check = Cmd(
            "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name = @col",
            ("@col", column));
        if ((long)check.ExecuteScalar()! > 0) return;
        ExecOne($"ALTER TABLE notes ADD COLUMN {column} {definition}");
    }

    // --- helpers ---

    private static string? NormalizePrefix(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.TrimEnd('/') + "/%";

    private Note? QueryOne(string sql, params (string, object)[] ps)
    {
        using var cmd = Cmd(sql, ps);
        using var r   = cmd.ExecuteReader();
        return r.Read() ? ReadNote(r) : null;
    }

    private IReadOnlyList<Note> QueryMany(string sql, params (string, object)[] ps)
    {
        using var cmd = Cmd(sql, ps);
        using var r   = cmd.ExecuteReader();
        var notes = new List<Note>();
        while (r.Read()) notes.Add(ReadNote(r));
        return notes;
    }

    private void ExecOne(string sql, params (string, object)[] ps)
    {
        using var cmd = Cmd(sql, ps);
        cmd.ExecuteNonQuery();
    }

    private void Exec(string sql, params (string, object)[] ps)
    {
        foreach (var stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            using var cmd = Cmd(stmt, ps);
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteCommand Cmd(string sql, params (string Name, object Value)[] ps)
    {
        var cmd = _db!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    private static Note ReadNote(SqliteDataReader r)
    {
        var tags  = JsonSerializer.Deserialize<string[]>(r.GetString(r.GetOrdinal("tags")))  ?? [];
        var links = JsonSerializer.Deserialize<string[]>(r.GetString(r.GetOrdinal("links"))) ?? [];

        float[]? embedding = null;
        var embCol = r.GetOrdinal("embedding");
        if (!r.IsDBNull(embCol))
            embedding = BytesToEmbedding((byte[])r.GetValue(embCol));

        var weightCol      = r.GetOrdinal("weight");
        var accessCountCol = r.GetOrdinal("access_count");

        DateTime? lastWeightSet = null;
        try
        {
            var lwsCol = r.GetOrdinal("last_weight_set");
            if (!r.IsDBNull(lwsCol))
                lastWeightSet = DateTime.Parse(r.GetString(lwsCol)).ToUniversalTime();
        }
        catch { /* column absent in legacy schema — ignored */ }

        bool archived = false;
        try
        {
            var archCol = r.GetOrdinal("archived");
            if (!r.IsDBNull(archCol)) archived = r.GetInt32(archCol) != 0;
        }
        catch { /* column absent in legacy schema — ignored */ }

        return new Note
        {
            Id            = r.GetString(r.GetOrdinal("id")),
            FilePath      = r.GetString(r.GetOrdinal("file_path")),
            Title         = r.GetString(r.GetOrdinal("title")),
            Content       = r.GetString(r.GetOrdinal("content")),
            Tags          = tags,
            Links         = links,
            Created       = DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))).ToUniversalTime(),
            Modified      = DateTime.Parse(r.GetString(r.GetOrdinal("modified_at"))).ToUniversalTime(),
            Embedding     = embedding,
            Weight        = r.IsDBNull(weightCol)      ? 0.0f : (float)r.GetDouble(weightCol),
            AccessCount   = r.IsDBNull(accessCountCol) ? 0    : r.GetInt32(accessCountCol),
            Archived      = archived,
            LastWeightSet = lastWeightSet,
        };
    }

    private static byte[]? EmbeddingToBytes(float[]? v)
    {
        if (v is null) return null;
        var bytes = new byte[v.Length * sizeof(float)];
        MemoryMarshal.Cast<float, byte>(v).CopyTo(bytes);
        return bytes;
    }

    private static float[] BytesToEmbedding(byte[] bytes)
    {
        var v = new float[bytes.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(bytes).CopyTo(v);
        return v;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        // Both vectors are already L2-normalized by GemmaEmbeddingEngine
        var dot = 0f;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++) dot += a[i] * b[i];
        return dot;
    }

    private static string EscapeFts(string query) =>
        // Wrap in quotes for exact phrase, fall back to prefix search
        $"\"{query.Replace("\"", "\"\"")}\"";

    private static string? Snippet(string content, string query)
    {
        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = Math.Max(0, idx - 60);
        var end   = Math.Min(content.Length, idx + query.Length + 60);
        return (start > 0 ? "..." : "") + content[start..end].Trim() + (end < content.Length ? "..." : "");
    }
}
