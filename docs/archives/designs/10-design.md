# Technical Design: G5 Temporal Decay — memctl decay

**Spec:** docs/specs/10-spec.md
**Task:** 10
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Architecture Overview

The `decay` command follows the same operator pattern as `list` and `weight`:
Bootstrap (`Program.cs`) → `DecayOperator` → `INoteIndex` port → `SqliteNoteIndex`.

Key structural changes:
- `Note` entity gains two fields: `Archived` and `LastWeightSet`
- `INoteIndex.GetAll()` gains a `bool includeArchived = false` parameter
- `INoteIndex` gains `ApplyDecay(noteId, newWeight, archived)` for atomic per-note decay writes
- `INoteIndex` gains `ApplyDecayBatch(IEnumerable<(string, float, bool)>)` for single-transaction bulk update
- `SqliteNoteIndex` gets two column migrations: `archived` and `last_weight_set`
- `SqliteNoteIndex.SetWeight` also writes `last_weight_set = UtcNow`
- `WeightOperator` clamp expanded from `[0, 1]` to `[0, 2]`
- `ListOperator.Execute` gains `bool includeArchived = false` parameter
- New file: `src/memctl/Operators/DecayOperator.cs`

Idempotency: `metadata` table already exists. Key `last_decay_date` (ISO date string `yyyy-MM-dd`) is written on each non-dry-run completion. On next run, if `last_decay_date == UtcNow.Date`, exit early with same report structure and `already_run_today: true` in data.

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/DecayOperator.cs` | Temporal decay business logic | `DecayOperator`, `DecayReport` |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/CoreAbstractions/Entities/Note.cs` | Add `Archived`, `LastWeightSet` | FR-030, FR-031 |
| `src/memctl/CoreAbstractions/Ports/INoteIndex.cs` | `GetAll(bool)`, `ApplyDecay`, `ApplyDecayBatch` | FR-046, FR-047 |
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs` | Schema migrations, `ReadNote` map, `GetAll` filter, `SetWeight` update, `ApplyDecay`/`ApplyDecayBatch` impl | FR-040–FR-047 |
| `src/memctl/Operators/ListOperator.cs` | `Execute` gains `includeArchived` param | FR-062 |
| `src/memctl/Operators/WeightOperator.cs` | Clamp to `[0, 2]`, update help text var | FR-080, FR-081 |
| `src/memctl/Bootstrap/Program.cs` | Register `decay` subcommand, add `--include-archived` to `list`, update `weight` description | FR-001, FR-061 |

### Deleted Files

None.

---

### Integration Code Blocks

#### `src/memctl/CoreAbstractions/Entities/Note.cs` — complete new file

```
old_string:
public sealed record Note
{
    public string   Id         { get; init; } = "";
    public string   FilePath   { get; init; } = "";   // relative to vault
    public string   Title      { get; init; } = "";
    public string   Content    { get; init; } = "";
    public string[] Tags       { get; init; } = [];
    public string[] Links      { get; init; } = [];   // wikilink targets (no brackets)
    public DateTime Created    { get; init; }
    public DateTime Modified   { get; init; }
    public float[]? Embedding    { get; init; }
    public float    Weight       { get; init; } = 0.0f;
    public int      AccessCount  { get; init; } = 0;
}

new_string:
public sealed record Note
{
    public string    Id            { get; init; } = "";
    public string    FilePath      { get; init; } = "";   // relative to vault
    public string    Title         { get; init; } = "";
    public string    Content       { get; init; } = "";
    public string[]  Tags          { get; init; } = [];
    public string[]  Links         { get; init; } = [];   // wikilink targets (no brackets)
    public DateTime  Created       { get; init; }
    public DateTime  Modified      { get; init; }
    public float[]?  Embedding     { get; init; }
    public float     Weight        { get; init; } = 0.0f;
    public int       AccessCount   { get; init; } = 0;
    public bool      Archived      { get; init; } = false;
    public DateTime? LastWeightSet { get; init; }
}
```

#### `src/memctl/CoreAbstractions/Ports/INoteIndex.cs` — complete new file

```
old_string:
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
    void    SetWeight(string noteId, float weight);
    void    IncrementAccess(string noteId);
    void    SetMetadata(string key, string value);
    string? GetMetadata(string key);
}

new_string:
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
    IReadOnlyList<Note> SearchByTags(string[] tags, bool matchAll, int limit);
    IReadOnlyList<Note> SearchByDate(DateTime? from, DateTime? to, int limit);
    IReadOnlyList<Note> GetLinked(string noteId, int depth);
    IReadOnlyList<(string Tag, int Count)> GetTagStats();
    (int NoteCount, int TagCount, int LinkCount, long IndexBytes) GetStats();
    void    SetWeight(string noteId, float weight);
    void    ApplyDecay(string noteId, float newWeight, bool archived);
    void    ApplyDecayBatch(IEnumerable<(string NoteId, float NewWeight, bool Archived)> updates);
    void    IncrementAccess(string noteId);
    void    SetMetadata(string key, string value);
    string? GetMetadata(string key);
}
```

#### `src/memctl/Implementations/Index/SqliteNoteIndex.cs` — changed sections

**ApplySchema — add column migrations:**
```
old_string:
        // idempotent column migrations for existing databases
        MigrateAddColumn("weight",       "REAL    NOT NULL DEFAULT 0.0");
        MigrateAddColumn("access_count", "INTEGER NOT NULL DEFAULT 0");

new_string:
        // idempotent column migrations for existing databases
        MigrateAddColumn("weight",          "REAL    NOT NULL DEFAULT 0.0");
        MigrateAddColumn("access_count",    "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumn("archived",        "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumn("last_weight_set", "TEXT");
```

**GetAll — add includeArchived filter:**
```
old_string:
    public IReadOnlyList<Note> GetAll() =>
        QueryMany("SELECT * FROM notes ORDER BY weight DESC, access_count DESC");

new_string:
    public IReadOnlyList<Note> GetAll(bool includeArchived = false)
    {
        var sql = includeArchived
            ? "SELECT * FROM notes ORDER BY weight DESC, access_count DESC"
            : "SELECT * FROM notes WHERE archived = 0 ORDER BY weight DESC, access_count DESC";
        return QueryMany(sql);
    }
```

**SetWeight — also write last_weight_set:**
```
old_string:
    public void SetWeight(string noteId, float weight)
    {
        var clamped = Math.Clamp(weight, 0.0f, 2.0f);
        Exec("UPDATE notes SET weight = @w WHERE id = @id", ("@w", clamped), ("@id", noteId));
    }

new_string:
    public void SetWeight(string noteId, float weight)
    {
        var clamped = Math.Clamp(weight, 0.0f, 2.0f);
        var now     = DateTime.UtcNow.ToString("O");
        Exec("UPDATE notes SET weight = @w, last_weight_set = @lws WHERE id = @id",
            ("@w", clamped), ("@lws", now), ("@id", noteId));
    }
```

**ApplyDecay and ApplyDecayBatch — new methods (add after SetWeight):**
```
old_string:
    public void IncrementAccess(string noteId) =>
        Exec("UPDATE notes SET access_count = access_count + 1 WHERE id = @id", ("@id", noteId));

new_string:
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
```

**ReadNote — map new columns:**
```
old_string:
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

new_string:
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
```

#### `src/memctl/Operators/ListOperator.cs` — complete new file

```
old_string:
public sealed class ListOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string? tag, int limit)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var notes = tag is not null
            ? index.SearchByTags([tag], matchAll: false, limit)
            : index.GetAll().Take(limit).ToList();

        return MemctlOutcome.Ok("list", $"{notes.Count} notes",
            new { count = notes.Count, notes = notes.Select(n => GetOperator.NoteToData(n)) });
    }
}

new_string:
public sealed class ListOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string? tag, int limit, bool includeArchived = false)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var notes = tag is not null
            ? index.SearchByTags([tag], matchAll: false, limit)
            : index.GetAll(includeArchived).Take(limit).ToList();

        return MemctlOutcome.Ok("list", $"{notes.Count} notes",
            new { count = notes.Count, notes = notes.Select(n => GetOperator.NoteToData(n)) });
    }
}
```

#### `src/memctl/Operators/WeightOperator.cs` — clamp update

```
old_string:
        var clamped = Math.Clamp(parsed, 0.0f, 1.0f);
        index.SetWeight(note.Id, clamped);

        return MemctlOutcome.Ok("weight", $"Weight set to {(float)Math.Round(clamped, 2)}", new
        {
            id     = note.Id,
            file   = note.FilePath,
            weight = (float)Math.Round(clamped, 2),
        });

new_string:
        var clamped = Math.Clamp(parsed, 0.0f, 2.0f);
        index.SetWeight(note.Id, clamped);

        return MemctlOutcome.Ok("weight", $"Weight set to {(float)Math.Round(clamped, 2)}", new
        {
            id     = note.Id,
            file   = note.FilePath,
            weight = (float)Math.Round(clamped, 2),
        });
```

#### `src/memctl/Bootstrap/Program.cs` — list command update

```
old_string:
// --- list ---
var listTagOpt = new Option<string?>("--tag", "Filter by tag");
var listCmd    = new Command("list", "List notes");
listCmd.AddOption(listTagOpt);
listCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new ListOperator(vaultReader, noteIndex);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForOption(listTagOpt), g.Limit));
});
root.AddCommand(listCmd);

new_string:
// --- list ---
var listTagOpt            = new Option<string?>("--tag",              "Filter by tag");
var listIncludeArchiveOpt = new Option<bool>   ("--include-archived", "Include archived notes");
var listCmd               = new Command("list", "List notes");
listCmd.AddOption(listTagOpt);
listCmd.AddOption(listIncludeArchiveOpt);
listCmd.SetHandler(ctx =>
{
    var g              = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var includeArchived = ctx.ParseResult.GetValueForOption(listIncludeArchiveOpt);
    var op             = new ListOperator(vaultReader, noteIndex);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForOption(listTagOpt), g.Limit, includeArchived));
});
root.AddCommand(listCmd);
```

#### `src/memctl/Bootstrap/Program.cs` — weight command description update

```
old_string:
var weightValArg = new Argument<string>("value", "Weight value (0.0–1.0)");
var weightCmd    = new Command("weight", "Set importance weight for a note (0.0–1.0)");

new_string:
var weightValArg = new Argument<string>("value", "Weight value (0.0–2.0)");
var weightCmd    = new Command("weight", "Set importance weight for a note (0.0–2.0)");
```

#### `src/memctl/Bootstrap/Program.cs` — decay subcommand (add before `// --- identity ---`)

```
old_string:
// --- identity ---

new_string:
// --- decay ---
var decayDaysOpt   = new Option<int>   ("--days",         "Age threshold in days") { IsRequired = true };
var decayFactorOpt = new Option<double>("--decay-factor", () => 0.9, "Multiplicative decay factor (default 0.9)");
var decayDryRunOpt = new Option<bool>  ("--dry-run",      "Simulate — no weight changes");
var decayCmd       = new Command("decay", "Apply temporal decay to stale vault notes");
decayCmd.AddOption(decayDaysOpt);
decayCmd.AddOption(decayFactorOpt);
decayCmd.AddOption(decayDryRunOpt);
decayCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var pr         = ctx.ParseResult;
    var days       = pr.GetValueForOption(decayDaysOpt);
    var factor     = (float)pr.GetValueForOption(decayFactorOpt);
    var dryRun     = pr.GetValueForOption(decayDryRunOpt);
    var outcome    = new DecayOperator(vaultReader, noteIndex).Execute(vault, days, factor, dryRun);
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(decayCmd);

// --- identity ---
```

#### `src/memctl/Operators/DecayOperator.cs` — complete new file

```csharp
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

internal sealed record DecayReport(int Decayed, int Archived, int Unchanged, int AlreadyArchived);

public sealed class DecayOperator(IVaultReader vaultReader, INoteIndex index)
{
    private const float  ArchiveThreshold     = 0.05f;
    private const float  ProtectedDecayExp    = 1.0f / 3.0f;
    private const string LastDecayDateKey     = "last_decay_date";

    public MemctlOutcome Execute(string vaultPath, int days, float decayFactor, bool dryRun)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        // idempotency: skip if already ran today (non-dry-run only)
        var todayStr = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        if (!dryRun && index.GetMetadata(LastDecayDateKey) == todayStr)
        {
            return MemctlOutcome.Ok("decay", "Already ran today — skipped",
                new { decayed = 0, archived = 0, unchanged = 0, already_archived = 0, already_run_today = true });
        }

        var now    = DateTime.UtcNow;
        var notes  = index.GetAll(includeArchived: true);

        var updates          = new List<(string NoteId, float NewWeight, bool Archived)>();
        int decayed          = 0;
        int newlyArchived    = 0;
        int unchanged        = 0;
        int alreadyArchived  = 0;

        foreach (var note in notes)
        {
            // already archived before this run
            if (note.Archived) { alreadyArchived++; continue; }

            // recency guards — skip if modified or weight-set within --days
            var daysSinceModified = (now - note.Modified).TotalDays;
            if (daysSinceModified <= days) { unchanged++; continue; }

            if (note.LastWeightSet.HasValue)
            {
                var daysSinceWeightSet = (now - note.LastWeightSet.Value).TotalDays;
                if (daysSinceWeightSet <= days) { unchanged++; continue; }
            }

            // floor guard — already at zero
            if (note.Weight <= 0.0f) { unchanged++; continue; }

            // compute new weight
            float newWeight;
            if (note.Weight > 1.0f)
                newWeight = note.Weight * MathF.Pow(decayFactor, ProtectedDecayExp);
            else
                newWeight = note.Weight * decayFactor;

            var willArchive = newWeight < ArchiveThreshold;
            if (willArchive) newWeight = 0.0f;

            updates.Add((note.Id, newWeight, willArchive));
            decayed++;
            if (willArchive) newlyArchived++;
        }

        if (!dryRun)
        {
            if (updates.Count > 0)
                index.ApplyDecayBatch(updates);
            index.SetMetadata(LastDecayDateKey, todayStr);
        }

        var report = new DecayReport(decayed, newlyArchived, unchanged, alreadyArchived);
        return MemctlOutcome.Ok("decay", $"Decayed {decayed} notes, archived {newlyArchived}",
            new
            {
                decayed          = report.Decayed,
                archived         = report.Archived,
                unchanged        = report.Unchanged,
                already_archived = report.AlreadyArchived,
                dry_run          = dryRun,
            });
    }
}
```

---

## 3. Data Model

### `notes` table — new columns

| Column | Type | Default | Nullable | Notes |
|--------|------|---------|----------|-------|
| `archived` | `INTEGER` | `0` | No | 0 = active, 1 = archived |
| `last_weight_set` | `TEXT` | `NULL` | Yes | ISO 8601 UTC, set by `SetWeight` |

Migration: added via `MigrateAddColumn` in `ApplySchema`, idempotent (checks `pragma_table_info`).

### `metadata` table — new keys

| Key | Value | Set By |
|-----|-------|--------|
| `last_decay_date` | `"yyyy-MM-dd"` (UTC) | `DecayOperator.Execute` on non-dry-run |

`metadata` table already exists — no schema change needed.

---

## 4. API Design

Not applicable — CLI command.

---

## 5. UI Components

Not applicable.

---

## 6. Business Logic

### Decay Algorithm (FR-010–FR-018)

```
for each note in GetAll(includeArchived: true):
    if note.Archived:
        alreadyArchived++
        continue

    daysSinceModified = (UtcNow - note.Modified).TotalDays
    if daysSinceModified <= days:
        unchanged++
        continue

    if note.LastWeightSet != null:
        daysSinceWeightSet = (UtcNow - note.LastWeightSet).TotalDays
        if daysSinceWeightSet <= days:
            unchanged++
            continue

    if note.Weight <= 0.0:
        unchanged++
        continue

    if note.Weight > 1.0:
        newWeight = note.Weight * pow(decayFactor, 1/3)   // protected tier
    else:
        newWeight = note.Weight * decayFactor              // normal tier

    willArchive = newWeight < 0.05
    if willArchive: newWeight = 0.0

    add to batch: (note.Id, newWeight, willArchive)
    decayed++
    if willArchive: archived++

if not dryRun:
    ApplyDecayBatch(batch)               // single SQLite transaction
    SetMetadata("last_decay_date", today)
```

### Counters semantics

| Counter | Meaning |
|---------|---------|
| `decayed` | Notes whose weight was reduced (includes newly archived) |
| `archived` | Notes that crossed below 0.05 threshold in this run |
| `unchanged` | Notes skipped (too recent, already zero weight, or no eligible condition) |
| `already_archived` | Notes that had `archived = 1` at the START of this run |

### Idempotency

`last_decay_date` written in `metadata` after each live run. Second call same calendar day (UTC) returns immediately with all counters = 0 and `already_run_today = true`. Dry-run never reads or writes `last_decay_date`.

### `already_archived` skip logic (Q2 resolution)

Already-archived notes are skipped entirely — their weight is not further decayed. Rationale: they are already deprioritized; further weight reduction has no visible effect since `list` already excludes them.

---

## 7. Error Handling Strategy

| Scenario | Behavior |
|----------|---------|
| Vault not found | `RequireVault` in Program.cs returns null → exit 1 (consistent with all other commands) |
| `--days` omitted | `{ IsRequired = true }` on option → System.CommandLine prints error, exits non-zero automatically |
| `--decay-factor` < 0 or > 1 | Not validated — spec says values > 1 are valid (weight inflation), values < 0 not clamped at operator level |
| Empty vault (0 notes) | Returns `{decayed:0, archived:0, unchanged:0, already_archived:0}`, exits 0 |
| Clock skew (`daysSinceModified` < 0) | Treated as not eligible (future-dated note), counted as `unchanged` |
| `ApplyDecayBatch` SQL error | Propagates as unhandled exception — SQLite constraint violations not expected here; transaction rolls back automatically on disposal |

---

## 8. Security Considerations

No new attack surface. Decay only modifies `weight` and `archived` columns of existing rows — no file writes, no external I/O. `--dry-run` opens no write transaction (FR-NFR-004).

---

## 9. Performance Considerations

- NFR-002: 10,000 notes < 5 seconds. `ApplyDecayBatch` wraps all writes in a single `BeginTransaction` / `Commit` cycle — avoids per-note auto-commit overhead.
- `GetAll(includeArchived: true)` loads all notes into memory. At 10k notes with no embeddings, this is ~5–10 MB — acceptable.
- The decay loop is O(n) with no index lookups.
- Dry-run opens no write transaction (NFR-004).

---

## 10. Testing Strategy

### Unit tests (planned)

| Test | FR |
|------|----|
| weight=1.0, days=30, note modified 31 days ago → newWeight=0.9 | FR-013 |
| weight=1.5, days=30, note modified 31 days ago → newWeight≈1.447 | FR-014 |
| weight=0.04, factor=0.9, 31 days → archived=true, newWeight=0.0 | FR-015 |
| note modified 5 days ago, days=30 → unchanged | FR-012 |
| note LastWeightSet 5 days ago, days=30 → unchanged | FR-012 |
| note.Archived=true → alreadyArchived++, weight unchanged | FR-016 |
| dryRun=true → no `ApplyDecayBatch` call | FR-004 |
| Second call same day → already_run_today=true | FR-018 |
| `GetAll()` excludes archived notes | FR-045 |
| `GetAll(includeArchived:true)` returns all | FR-046 |
| `SetWeight` writes `last_weight_set` | FR-044 |
| `SetWeight(id, 2.5f)` stores 2.0 | FR-080 |
| `SetWeight(id, 1.5f)` stores 1.5 | FR-080 |
| Empty vault → all counters 0 | spec edge case |

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected Output | FR |
|----------|---------|----------------|-----|
| Basic decay run | `memctl decay --vault ./vault --days 30` | exits 0, JSON `{success:true, action:"decay", data:{decayed:N, archived:M, unchanged:K, already_archived:P}}` | FR-001, FR-020, FR-021 |
| Dry run | `memctl decay --vault ./vault --days 30 --dry-run` | exits 0, same JSON structure, `dry_run:true`; follow-up `list` unchanged | FR-004 |
| No `--days` | `memctl decay --vault ./vault` | exits non-zero, error message | FR-002 |
| Custom factor | `memctl decay --vault ./vault --days 30 --decay-factor 0.8` | exits 0, weights reduced by factor 0.8 | FR-003 |
| List excludes archived | `memctl list --vault ./vault` (after decay archived a note) | archived note absent | FR-060 |
| List includes archived | `memctl list --vault ./vault --include-archived` | archived note present | FR-061 |
| Search finds archived | `memctl search --vault ./vault "keyword"` | archived note matching query appears | FR-070 |
| Weight range extended | `memctl weight <id> 1.5 --vault ./vault` | exits 0, weight=1.5 in output | FR-080 |

---

## 11. Dependencies

| Dependency | Notes |
|------------|-------|
| `Note.cs` — `Archived`, `LastWeightSet` added | All `ReadNote` callers get new fields automatically |
| `INoteIndex.cs` — `GetAll(bool)` signature change | All callers: `ListOperator` (updated), `ContextInjectOperator` (no args → uses default `false`), `StatsOperator` if it calls `GetAll` |
| `SqliteNoteIndex.cs` — schema migration | Existing databases: `MigrateAddColumn` adds columns safely |
| `WeightOperator.cs` — clamp 1.0→2.0 | Breaking for callers relying on hard cap at 1.0 (none found in codebase) |
| No new NuGet packages needed | `System.Math` / `MathF` already available |

### Check: ContextInjectOperator GetAll usage

`ContextInjectOperator` likely calls `GetAll()` — it uses the no-arg form, which defaults to `includeArchived: false`, so no behavior change required. Same for `StatsOperator`.

---

## 12. Implementation Order

1. `Note.cs` — add `Archived`, `LastWeightSet` (unblocks everything)
2. `INoteIndex.cs` — add `GetAll(bool)`, `ApplyDecay`, `ApplyDecayBatch`
3. `SqliteNoteIndex.cs` — schema migrations, `ReadNote` mapper, `GetAll` filter, `SetWeight` update, new methods
4. `ListOperator.cs` — add `includeArchived` param
5. `WeightOperator.cs` — clamp update
6. `DecayOperator.cs` — new file
7. `Program.cs` — list `--include-archived`, decay subcommand, weight description

Step 1–3 must complete before 4–7. Steps 4–7 are independent of each other.

---

## 13. Assumptions & Open Design Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| Q1 | Idempotency via `metadata.last_decay_date` (UTC date string) | Prevents double-decay within same calendar day. `metadata` table already exists — zero schema cost. |
| Q2 | Already-archived notes: skip decay, increment `already_archived` | No visible effect from further reducing an archived note's weight. Clean separation. |
| Q3 | `WeightOperator` clamp updated to `[0, 2]` to match `SqliteNoteIndex.SetWeight` | Index already clamps to 2.0 (verified in code). Operator clamp was stale at 1.0. |
| Q4 | `search-tags` and `search-links` do NOT add `archived` filter | Spec FR-071 says "no `WHERE archived = 0` in search queries". `SearchByTags` and `GetLinked` included by "all search methods" interpretation. |
| Q5 | `--decay-factor` not validated at CLI level | Spec explicitly permits >1.0 (weight inflation) and ≤0.0. Operator receives raw value. |
| — | `ApplyDecayBatch` wraps in single transaction vs `ApplyDecay` per-note | Single transaction for atomicity + NFR-002 performance |
| — | `newWeight = 0.0f` when archiving (not stored as `< 0.05` value) | Clean floor; archived notes show weight 0 |
| — | `last_weight_set` stored as ISO 8601 TEXT (not Unix int) | Consistent with `created_at`/`modified_at` pattern in existing schema |

---

## 14. Traceability Matrix

| FR | File(s) | Section/Method |
|----|---------|----------------|
| FR-001 | Program.cs | `decay` subcommand handler |
| FR-002 | Program.cs | `decayDaysOpt` with `IsRequired = true` |
| FR-003 | Program.cs, DecayOperator | `decayFactorOpt` default 0.9 |
| FR-004 | DecayOperator | `if (!dryRun)` guard |
| FR-005 | Program.cs | `RequireVault` |
| FR-010 | DecayOperator | `GetAll(includeArchived: true)` |
| FR-011 | DecayOperator | `(now - note.Modified).TotalDays` |
| FR-012 | DecayOperator | recency guard block |
| FR-013 | DecayOperator | `note.Weight * decayFactor` |
| FR-014 | DecayOperator | `note.Weight * MathF.Pow(decayFactor, 1f/3f)` |
| FR-015 | DecayOperator | `newWeight < ArchiveThreshold` |
| FR-016 | DecayOperator | `if (note.Archived) { alreadyArchived++; continue; }` |
| FR-017 | SqliteNoteIndex | `ApplyDecayBatch` |
| FR-018 | DecayOperator, SqliteNoteIndex | `last_decay_date` metadata check |
| FR-020 | DecayOperator | `MemctlOutcome.Ok("decay", ...)` |
| FR-021 | DecayOperator | anonymous data object |
| FR-022 | DecayOperator | `decayed++` counter |
| FR-023 | DecayOperator | `newlyArchived++` counter |
| FR-024 | DecayOperator | `unchanged++` counter |
| FR-025 | DecayOperator | `alreadyArchived++` counter |
| FR-030 | Note.cs | `Archived` property |
| FR-031 | Note.cs | `LastWeightSet` property |
| FR-040 | SqliteNoteIndex | `MigrateAddColumn("archived", ...)` |
| FR-041 | SqliteNoteIndex | `MigrateAddColumn("last_weight_set", ...)` |
| FR-042 | SqliteNoteIndex | `ReadNote` `archived` mapping |
| FR-043 | SqliteNoteIndex | `ReadNote` `last_weight_set` mapping |
| FR-044 | SqliteNoteIndex | `SetWeight` + `last_weight_set = now` |
| FR-045 | SqliteNoteIndex | `GetAll(false)` WHERE clause |
| FR-046 | SqliteNoteIndex | `GetAll(bool includeArchived)` |
| FR-047 | INoteIndex.cs | interface signature |
| FR-050 | DecayOperator.cs | class + constructor |
| FR-051 | DecayOperator.cs | `Execute` signature |
| FR-052 | DecayOperator.cs | `IngestOperator.NeedsIngest` check |
| FR-053 | DecayOperator.cs | `GetAll(includeArchived: true)` |
| FR-060 | SqliteNoteIndex, ListOperator | `GetAll(false)` default |
| FR-061 | Program.cs, ListOperator | `--include-archived` option |
| FR-062 | ListOperator.cs | `Execute(... bool includeArchived)` |
| FR-070 | SqliteNoteIndex | no archive filter in search methods |
| FR-071 | SqliteNoteIndex | `SearchBm25`, `SearchSemantic`, `SearchByTags`, `SearchByDate` unchanged |
| FR-080 | WeightOperator, SqliteNoteIndex | clamp to 2.0 |
| FR-081 | Program.cs | `weightValArg` description, `weightCmd` description |
