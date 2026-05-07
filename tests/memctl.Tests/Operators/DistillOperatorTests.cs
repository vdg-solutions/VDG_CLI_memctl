using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class DistillOperatorTests : IDisposable
{
    private readonly string            _vaultPath;
    private readonly ObsidianVaultReader _vault;
    private readonly SqliteNoteIndex   _index;

    public DistillOperatorTests()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), "memctl-test-distill-" + Guid.NewGuid());
        _vault     = new ObsidianVaultReader();
        _index     = new SqliteNoteIndex();

        Directory.CreateDirectory(_vaultPath);
        foreach (var d in new[] { "chats", "decisions", "patterns", "lessons",
                                   ".obsidian", Path.Combine(".obsidian", "memctl") })
            Directory.CreateDirectory(Path.Combine(_vaultPath, d));
    }

    public void Dispose()
    {
        _index.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(_vaultPath)) Directory.Delete(_vaultPath, recursive: true); break; }
            catch { Thread.Sleep(50); }
        }
    }

    private Note WriteConvNote(string fileName, bool distilled = false)
    {
        var content = distilled
            ? "---\ndistilled: true\n---\n\n# Already distilled"
            : "# Conversation 2026-05-07\n\nSome content worth remembering.";

        var absPath = Path.Combine(_vaultPath, "chats", fileName);
        File.WriteAllText(absPath, content);
        var note = _vault.ParseNote(absPath, _vaultPath);
        _index.Initialize(Path.Combine(_vaultPath, ".obsidian", "memctl", "index.db"));
        _index.Upsert(note);
        return note;
    }

    [Fact]
    public async Task Distill_SkipsAlreadyDistilledConversations()
    {
        WriteConvNote("2026-05-07-abc.md", distilled: true);
        var callCount = 0;
        var llm       = new CountingLlmClient(() => callCount++);
        var op        = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task Distill_WeightClampedToMaxBound()
    {
        WriteConvNote("2026-05-07-xyz.md");
        var llm = new FixedResultLlmClient(new DistillResult([
            new DistilledNote("decision", "Test decision", "Some content",
                              ["tag1"], [], Weight: 3.0f, "important")
        ]));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false);

        var written = Directory.GetFiles(Path.Combine(_vaultPath, "decisions"), "*.md");
        Assert.Single(written);
        var note = _vault.ParseNote(written[0], _vaultPath);
        Assert.True(note.Weight <= 1.5f,
            $"Expected weight ≤ 1.5 but was {note.Weight}. " +
            "Note: weight is stored in index, not file frontmatter.");
        // Verify index stores clamped weight
        _index.Initialize(Path.Combine(_vaultPath, ".obsidian", "memctl", "index.db"));
        var indexed = _index.GetAll().FirstOrDefault(n => n.FilePath.StartsWith("decisions/"));
        Assert.NotNull(indexed);
        Assert.True(indexed.Weight <= 1.5f);
    }

    [Fact]
    public async Task Distill_DryRunWritesNoFiles()
    {
        WriteConvNote("2026-05-07-dry.md");
        var llm = new FixedResultLlmClient(new DistillResult([
            new DistilledNote("lesson", "Dry run lesson", "Content", ["tag"], [], 1.2f, "test")
        ]));
        var op              = new DistillOperator(_vault, _index, llm);
        var filesBefore     = Directory.GetFiles(_vaultPath, "*.md", SearchOption.AllDirectories).Length;

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: true);

        var filesAfter = Directory.GetFiles(_vaultPath, "*.md", SearchOption.AllDirectories).Length;
        Assert.Equal(filesBefore, filesAfter);
        // Source conversation note must NOT have distilled: true written
        var convContent = File.ReadAllText(Path.Combine(_vaultPath, "chats", "2026-05-07-dry.md"));
        Assert.DoesNotContain("distilled: true", convContent);
    }

    // --- minimal LLM stubs ---

    private sealed class CountingLlmClient(Action onDistill) : ILlmClient
    {
        public Task<NoteEnrichment> EnrichAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new NoteEnrichment());

        public Task<DistillResult> DistillAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
        {
            onDistill();
            return Task.FromResult(new DistillResult([]));
        }

        public Task<ContradictionResult> CheckContradictionAsync(DistilledNote d, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, ""));
    }

    private sealed class FixedResultLlmClient(DistillResult result) : ILlmClient
    {
        public Task<NoteEnrichment> EnrichAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new NoteEnrichment());

        public Task<DistillResult> DistillAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(result);

        public Task<ContradictionResult> CheckContradictionAsync(DistilledNote d, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, ""));
    }
}
