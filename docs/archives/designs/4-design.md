# Technical Design: Add Importance Weight Field to Notes

**Spec:** docs/specs/4-spec.md
**Task:** 4
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

Cross-layer change touching all 5 layers: entity fields (CoreAbstractions), new port methods (CoreAbstractions), SQLite migration + new methods (Implementations), GetOperator update + new WeightOperator (Operators), and new `weight` CLI command (Bootstrap).

Data flow for weight:
- `memctl weight <id> <value>` → WeightOperator → INoteIndex.SetWeight(id, clamp(value))
- `memctl get <id>` → GetOperator → note found → INoteIndex.IncrementAccess(id) → return NoteToData (includes weight + access_count)

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/WeightOperator.cs` | Handle `weight` command: parse value, find note, call SetWeight | `WeightOperator` |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/CoreAbstractions/Entities/Note.cs` | Add `Weight float` + `AccessCount int` properties | FR-001, FR-002 |
| `src/memctl/CoreAbstractions/Ports/INoteIndex.cs` | Add `SetWeight` + `IncrementAccess` methods | FR-016, FR-017 |
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs` | Migration, Upsert fix, ReadNote update, GetAll sort, new methods | FR-003–006, FR-015–017 |
| `src/memctl/Operators/GetOperator.cs` | Call IncrementAccess after get; add weight/access_count to NoteToData | FR-012, FR-014 |
| `src/memctl/Bootstrap/Program.cs` | Wire `weight` command | FR-007, FR-009–011 |

### Integration Code Blocks

```
// INTEGRATION: Note.cs → record body
// old_string:
    public float[]? Embedding  { get; init; }
}

// new_string:
    public float[]? Embedding    { get; init; }
    public float    Weight       { get; init; } = 0.0f;
    public int      AccessCount  { get; init; } = 0;
}
```

```
// INTEGRATION: INoteIndex.cs → interface body
// old_string:
    void   SetMetadata(string key, string value);
    string? GetMetadata(string key);
}

// new_string:
    void    SetWeight(string noteId, float weight);
    void    IncrementAccess(string noteId);
    void    SetMetadata(string key, string value);
    string? GetMetadata(string key);
}
```

```
// INTEGRATION: SqliteNoteIndex.cs → GetAll()
// old_string:
    public IReadOnlyList<Note> GetAll() =>
        QueryMany("SELECT * FROM notes ORDER BY created_at DESC");

// new_string:
    public IReadOnlyList<Note> GetAll() =>
        QueryMany("SELECT * FROM notes ORDER BY weight DESC, access_count DESC");
```

```
// INTEGRATION: SqliteNoteIndex.cs → Upsert()
// old_string (full Upsert method):
    public void Upsert(Note note)
    {
        var tagsJson     = JsonSerializer.Serialize(note.Tags);
        var linksJson    = JsonSerializer.Serialize(note.Links);
        var embeddingBytes = EmbeddingToBytes(note.Embedding);

        Exec(@"
            INSERT INTO notes (id, file_path, title, content, tags, links, created_at, modified_at, embedding)
            VALUES (@id, @fp, @title, @content, @tags, @links, @created, @modified, @emb)
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
            ("@emb",     (object?)embeddingBytes ?? DBNull.Value));
    }

// new_string:
    public void Upsert(Note note)
    {
        var tagsJson       = JsonSerializer.Serialize(note.Tags);
        var linksJson      = JsonSerializer.Serialize(note.Links);
        var embeddingBytes = EmbeddingToBytes(note.Embedding);

        // weight and access_count excluded from UPDATE — preserve user-set values on re-ingest
        Exec(@"
            INSERT INTO notes (id, file_path, title, content, tags, links, created_at, modified_at, embedding)
            VALUES (@id, @fp, @title, @content, @tags, @links, @created, @modified, @emb)
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
            ("@emb",     (object?)embeddingBytes ?? DBNull.Value));
    }
```

```
// INTEGRATION: SqliteNoteIndex.cs → ApplySchema() — add migration calls at end
// old_string:
        ExecOne(@"CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
            INSERT INTO notes_fts(notes_fts, rowid, id, title, content)
            VALUES ('delete', old.rowid, old.id, old.title, old.content);
        END");
    }

// new_string:
        ExecOne(@"CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
            INSERT INTO notes_fts(notes_fts, rowid, id, title, content)
            VALUES ('delete', old.rowid, old.id, old.title, old.content);
        END");

        // idempotent column migrations for existing databases
        MigrateAddColumn("weight",       "REAL    NOT NULL DEFAULT 0.0");
        MigrateAddColumn("access_count", "INTEGER NOT NULL DEFAULT 0");
    }

    private void MigrateAddColumn(string column, string definition)
    {
        using var check = Cmd(
            "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name = @col",
            ("@col", column));
        if ((long)check.ExecuteScalar()! > 0) return;
        ExecOne($"ALTER TABLE notes ADD COLUMN {column} {definition}");
    }
```

```
// INTEGRATION: SqliteNoteIndex.cs — add SetWeight + IncrementAccess after GetStats()
// old_string:
    public void Dispose() => _db?.Dispose();

// new_string:
    public void SetWeight(string noteId, float weight)
    {
        var clamped = Math.Clamp(weight, 0.0f, 1.0f);
        Exec("UPDATE notes SET weight = @w WHERE id = @id", ("@w", clamped), ("@id", noteId));
    }

    public void IncrementAccess(string noteId) =>
        Exec("UPDATE notes SET access_count = access_count + 1 WHERE id = @id", ("@id", noteId));

    public void Dispose() => _db?.Dispose();
```

```
// INTEGRATION: SqliteNoteIndex.cs → ReadNote() — add Weight and AccessCount
// old_string:
        return new Note
        {
            Id        = r.GetString(r.GetOrdinal("id")),
            FilePath  = r.GetString(r.GetOrdinal("file_path")),
            Title     = r.GetString(r.GetOrdinal("title")),
            Content   = r.GetString(r.GetOrdinal("content")),
            Tags      = tags,
            Links     = links,
            Created   = DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))).ToUniversalTime(),
            Modified  = DateTime.Parse(r.GetString(r.GetOrdinal("modified_at"))).ToUniversalTime(),
            Embedding = embedding,
        };

// new_string:
        var weightCol      = r.GetOrdinal("weight");
        var accessCountCol = r.GetOrdinal("access_count");

        return new Note
        {
            Id          = r.GetString(r.GetOrdinal("id")),
            FilePath    = r.GetString(r.GetOrdinal("file_path")),
            Title       = r.GetString(r.GetOrdinal("title")),
            Content     = r.GetString(r.GetOrdinal("content")),
            Tags        = tags,
            Links       = links,
            Created     = DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))).ToUniversalTime(),
            Modified    = DateTime.Parse(r.GetString(r.GetOrdinal("modified_at"))).ToUniversalTime(),
            Embedding   = embedding,
            Weight      = r.IsDBNull(weightCol)      ? 0.0f : (float)r.GetDouble(weightCol),
            AccessCount = r.IsDBNull(accessCountCol) ? 0    : r.GetInt32(accessCountCol),
        };
```

```
// INTEGRATION: GetOperator.cs → Execute() — add IncrementAccess call
// old_string:
        if (note is null)
            return MemctlOutcome.Fail("get", $"Note not found: {idOrPath}");

        return MemctlOutcome.Ok("get", "Note found", NoteToData(note));

// new_string:
        if (note is null)
            return MemctlOutcome.Fail("get", $"Note not found: {idOrPath}");

        index.IncrementAccess(note.Id);
        return MemctlOutcome.Ok("get", "Note found", NoteToData(note));
```

```
// INTEGRATION: GetOperator.cs → NoteToData() — add weight and access_count
// old_string:
    internal static object NoteToData(Note n, float? score = null) => new
    {
        id       = n.Id,
        file     = n.FilePath,
        title    = n.Title,
        content  = n.Content,
        tags     = n.Tags,
        links    = n.Links,
        created  = n.Created.ToString("O"),
        modified = n.Modified.ToString("O"),
        score,
    };

// new_string:
    internal static object NoteToData(Note n, float? score = null) => new
    {
        id           = n.Id,
        file         = n.FilePath,
        title        = n.Title,
        content      = n.Content,
        tags         = n.Tags,
        links        = n.Links,
        created      = n.Created.ToString("O"),
        modified     = n.Modified.ToString("O"),
        weight       = (float)Math.Round(n.Weight, 2),
        access_count = n.AccessCount,
        score,
    };
```

```
// INTEGRATION: Program.cs — add weight command before `return await root.InvokeAsync(args);`
// old_string:
return await root.InvokeAsync(args);

// new_string:
// --- weight ---
var weightIdArg  = new Argument<string>("id", "Note ID or file path");
var weightValArg = new Argument<string>("value", "Weight value (0.0–1.0)");
var weightCmd    = new Command("weight", "Set importance weight for a note (0.0–1.0)");
weightCmd.AddArgument(weightIdArg);
weightCmd.AddArgument(weightValArg);
weightCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var pr      = ctx.ParseResult;
    var outcome = new WeightOperator(vaultReader, noteIndex).Execute(
        vault,
        pr.GetValueForArgument(weightIdArg),
        pr.GetValueForArgument(weightValArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(weightCmd);

return await root.InvokeAsync(args);
```

---

## 3. Data Model

### Changes to notes table

```sql
ALTER TABLE notes ADD COLUMN weight       REAL    NOT NULL DEFAULT 0.0;
ALTER TABLE notes ADD COLUMN access_count INTEGER NOT NULL DEFAULT 0;
```

Applied idempotently via `MigrateAddColumn()` in `ApplySchema()`.

---

## 4. New File: WeightOperator.cs

```csharp
using Memctl.CoreAbstractions.Ports;
using Memctl.Operators;

namespace Memctl.Operators;

public sealed class WeightOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string idOrPath, string rawValue)
    {
        if (!float.TryParse(rawValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return MemctlOutcome.Fail("weight", $"Invalid weight value: '{rawValue}' — must be a number");

        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var note = index.GetById(idOrPath) ?? index.GetByFilePath(idOrPath);
        if (note is null)
            return MemctlOutcome.Fail("weight", $"Note not found: {idOrPath}");

        var clamped = Math.Clamp(parsed, 0.0f, 1.0f);
        index.SetWeight(note.Id, clamped);

        return MemctlOutcome.Ok("weight", $"Weight set to {(float)Math.Round(clamped, 2)}", new
        {
            id     = note.Id,
            file   = note.FilePath,
            weight = (float)Math.Round(clamped, 2),
        });
    }
}
```

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected | FR |
|----------|---------|----------|-----|
| weight set valid | `weight docs/specs/4-spec.md 0.8 --vault .` | exit 0, `"weight": 0.8` | FR-007 |
| weight clamping | `weight docs/specs/4-spec.md 1.5 --vault .` | exit 0, `"weight": 1.0` | FR-008 |
| weight path accept | `weight docs/specs/4-spec.md 0.5 --vault .` | exit 0 | FR-009 |
| weight not found | `weight nonexistent-note-id-xyz 0.5 --vault .` | exit 1, `success: false` | FR-010 |
| weight bad value | `weight docs/specs/4-spec.md abc --vault .` | exit 1, parse error message | FR-011 |
| get access_count | `get docs/specs/4-spec.md --vault .` (twice) | JSON contains `access_count` and `weight` fields | FR-014 |

---

## 12. Implementation Order

1. `Note.cs` — add Weight + AccessCount properties
2. `INoteIndex.cs` — add SetWeight + IncrementAccess methods
3. `SqliteNoteIndex.cs` — MigrateAddColumn in ApplySchema, fix Upsert, update GetAll, update ReadNote, add SetWeight + IncrementAccess
4. `GetOperator.cs` — IncrementAccess call + NoteToData fields
5. `WeightOperator.cs` — new file
6. `Program.cs` — wire weight command

---

## 14. Traceability Matrix

| Requirement | Files | Notes |
|-------------|-------|-------|
| FR-001/002 | Note.cs | Weight + AccessCount properties |
| FR-003/004/005 | SqliteNoteIndex.cs ApplySchema + MigrateAddColumn | Idempotent migration |
| FR-006 | SqliteNoteIndex.cs Upsert | ON CONFLICT excludes weight/access_count |
| FR-007/008/009/010/011 | WeightOperator.cs, Program.cs | weight command |
| FR-012/013 | GetOperator.cs Execute | IncrementAccess only on success |
| FR-014 | GetOperator.cs NoteToData | weight + access_count in all outputs |
| FR-015 | SqliteNoteIndex.cs GetAll | ORDER BY weight DESC, access_count DESC |
| FR-016 | INoteIndex.cs, SqliteNoteIndex.cs | SetWeight |
| FR-017 | INoteIndex.cs, SqliteNoteIndex.cs | IncrementAccess |
