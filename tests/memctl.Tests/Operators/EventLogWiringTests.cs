using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

/// <summary>
/// Verifies EventLog wiring for write-path operators (FR-001..FR-007)
/// and SearchBm25 archived filter fix (FR-008, FR-009).
/// </summary>
public class EventLogWiringTests : IDisposable
{
    private readonly string          _tmpRoot;
    private readonly string          _vaultPath;
    private readonly ObsidianVaultReader _vault;
    private readonly SqliteNoteIndex _index;

    public EventLogWiringTests()
    {
        _tmpRoot   = Path.Combine(Path.GetTempPath(), "memctl-test-evtwiring-" + Guid.NewGuid());
        _vaultPath = Path.Combine(_tmpRoot, ".memctl");
        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian", "memctl"));

        _vault = new ObsidianVaultReader();
        _index = new SqliteNoteIndex();
        _index.Initialize(Path.Combine(_vaultPath, ".obsidian", "memctl", "index.db"));
    }

    public void Dispose()
    {
        _index.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, recursive: true); break; }
            catch { Thread.Sleep(50); }
        }
    }

    private string[] EventFiles() =>
        Directory.Exists(Path.Combine(_vaultPath, "events"))
            ? Directory.GetFiles(Path.Combine(_vaultPath, "events"), "*.md")
            : [];

    private static Note MakeNote(string id, string filePath, string title, bool archived = false) => new()
    {
        Id       = id,
        FilePath = filePath,
        Title    = title,
        Content  = $"# {title}",
        Tags     = [],
        Links    = [],
        Created  = DateTime.UtcNow,
        Modified = DateTime.UtcNow,
        Archived = archived,
    };

    // ── FR-001: AddOperator ──────────────────────────────────────────────────

    [Fact]
    public async Task Add_WritesEventFile()
    {
        var op = new AddOperator(_vault, _index, embedding: null);
        await op.ExecuteAsync(_vaultPath, "My Note", title: "My Note", tags: ["golden-rule"], fileName: null, llm: null);

        var files = EventFiles();
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.Contains("source: add", content);
        Assert.Contains("type: operator_run", content);
        Assert.Contains("severity: info", content);
    }

    // ── FR-002: DeleteOperator ───────────────────────────────────────────────

    [Fact]
    public void Delete_WritesEventFile()
    {
        var note = MakeNote(Guid.NewGuid().ToString("N")[..16], "del-me.md", "Del Me");
        _vault.WriteNote(note, _vaultPath, note.FilePath);
        new IngestOperator(_vault, _index, null).Execute(_vaultPath);

        new DeleteOperator(_vault, _index).Execute(_vaultPath, note.Id);

        // IngestOperator also fires EventLog — assert the delete event exists (not exact count)
        Assert.Contains(EventFiles(), f => File.ReadAllText(f).Contains("source: delete"));
    }

    // ── FR-003: WeightOperator ───────────────────────────────────────────────

    [Fact]
    public void Weight_WritesEventFile()
    {
        var note = MakeNote(Guid.NewGuid().ToString("N")[..16], "wt-note.md", "Weight Note");
        _vault.WriteNote(note, _vaultPath, note.FilePath);
        new IngestOperator(_vault, _index, null).Execute(_vaultPath);

        new WeightOperator(_vault, _index).Execute(_vaultPath, note.Id, "1.2");

        Assert.Contains(EventFiles(), f => File.ReadAllText(f).Contains("source: weight"));
    }

    // ── FR-004: DecayOperator ─────────────────────────────────────────────────

    [Fact]
    public void Decay_DryRun_NoEventFile()
    {
        new DecayOperator(_vault, _index).Execute(_vaultPath, days: 0, decayFactor: 0.9f, dryRun: true);
        Assert.Empty(EventFiles());
    }

    [Fact]
    public void Decay_NonDryRun_WritesEventFile()
    {
        var note = MakeNote(Guid.NewGuid().ToString("N")[..16], "old.md", "Old Note");
        note = note with { Modified = DateTime.UtcNow.AddDays(-60) };
        _vault.WriteNote(note, _vaultPath, note.FilePath);
        new IngestOperator(_vault, _index, null).Execute(_vaultPath);
        _index.Upsert(note with { Modified = DateTime.UtcNow.AddDays(-60) });

        new DecayOperator(_vault, _index).Execute(_vaultPath, days: 0, decayFactor: 0.9f, dryRun: false);

        Assert.Contains(EventFiles(), f => File.ReadAllText(f).Contains("source: decay"));
    }

    // ── FR-005: OrganizeOperator ──────────────────────────────────────────────

    [Fact]
    public async Task Organize_WritesEventFile()
    {
        // Organize with a mock LLM that returns empty enrichment — updated=0 but still completes
        var op = new OrganizeOperator(_vault, _index, new NoOpLlmClient());
        await op.ExecuteAsync(_vaultPath, since: null);

        var files = EventFiles();
        Assert.Single(files);
        Assert.Contains("source: organize", File.ReadAllText(files[0]));
    }

    // ── FR-006: MigrateTagsOperator ──────────────────────────────────────────

    [Fact]
    public void MigrateTags_DryRun_NoEventFile()
    {
        new MigrateTagsOperator(_vault, _index).Execute(
            _vaultPath,
            replaceExact:  new Dictionary<string, string> { ["old"] = "new" },
            replacePrefix: new Dictionary<string, string>(),
            removeExact:   [],
            removePrefix:  [],
            dryRun:        true);

        Assert.Empty(EventFiles());
    }

    [Fact]
    public void MigrateTags_NonDryRun_WritesEventFile()
    {
        new MigrateTagsOperator(_vault, _index).Execute(
            _vaultPath,
            replaceExact:  new Dictionary<string, string> { ["old"] = "new" },
            replacePrefix: new Dictionary<string, string>(),
            removeExact:   [],
            removePrefix:  [],
            dryRun:        false);

        var files = EventFiles();
        Assert.Single(files);
        Assert.Contains("source: migrate-tags", File.ReadAllText(files[0]));
    }

    // ── FR-008 + FR-009: SearchBm25 archived filter ──────────────────────────

    [Fact]
    public void SearchBm25_ExcludesArchivedNotes()
    {
        var archived = MakeNote(Guid.NewGuid().ToString("N")[..16], "evt.md", "operator_run archived", archived: true);
        _index.Upsert(archived);

        var hits = _index.SearchBm25("operator_run", 10);

        Assert.Empty(hits);
    }

    [Fact]
    public void SearchBm25_IncludesNonArchivedNotes()
    {
        var active = MakeNote(Guid.NewGuid().ToString("N")[..16], "active.md", "operator_run active");
        _index.Upsert(active);

        var hits = _index.SearchBm25("operator_run", 10);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.False(h.Note.Archived));
    }

    [Fact]
    public void SearchBm25_ConsistentWithGetAll()
    {
        var a = MakeNote(Guid.NewGuid().ToString("N")[..16], "a.md", "unique-keyword note alpha");
        var b = MakeNote(Guid.NewGuid().ToString("N")[..16], "b.md", "unique-keyword note beta", archived: true);
        _index.Upsert(a);
        _index.Upsert(b);

        var bm25Ids  = _index.SearchBm25("unique-keyword", 10).Select(h => h.Note.Id).ToHashSet();
        var getAllIds = _index.GetAll(includeArchived: false).Select(n => n.Id).ToHashSet();

        // All BM25 results must be in GetAll(non-archived)
        Assert.True(bm25Ids.IsSubsetOf(getAllIds));
        // Archived note b must not appear in either
        Assert.DoesNotContain(b.Id, bm25Ids);
        Assert.DoesNotContain(b.Id, getAllIds);
    }
}

// Minimal LLM stub for OrganizeOperator — returns empty enrichment
internal sealed class NoOpLlmClient : Memctl.CoreAbstractions.Ports.ILlmClient
{
    public Task<Memctl.CoreAbstractions.Ports.NoteEnrichment> EnrichAsync(
        string content,
        IReadOnlyList<Memctl.CoreAbstractions.Entities.Note> existingNotes,
        CancellationToken ct = default)
        => Task.FromResult(new Memctl.CoreAbstractions.Ports.NoteEnrichment());

    public Task<Memctl.CoreAbstractions.Entities.DistillResult> DistillAsync(
        string conversationContent,
        IReadOnlyList<Memctl.CoreAbstractions.Entities.Note> contextNotes,
        CancellationToken ct = default)
        => Task.FromResult(new Memctl.CoreAbstractions.Entities.DistillResult([]));

    public Task<Memctl.CoreAbstractions.Entities.ContradictionResult> CheckContradictionAsync(
        Memctl.CoreAbstractions.Entities.DistilledNote candidate,
        IReadOnlyList<Memctl.CoreAbstractions.Entities.Note> existingNotes,
        CancellationToken ct = default)
        => Task.FromResult(new Memctl.CoreAbstractions.Entities.ContradictionResult(
            false, null, Memctl.CoreAbstractions.Entities.ContradictionResolution.KeepNew, null, "no contradiction"));
}
