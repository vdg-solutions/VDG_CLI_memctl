using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class DistillContradictionTests : IDisposable
{
    private readonly string              _vaultPath;
    private readonly ObsidianVaultReader _vault;
    private readonly SqliteNoteIndex     _index;

    public DistillContradictionTests()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), "memctl-test-contra-" + Guid.NewGuid());
        _vault     = new ObsidianVaultReader();
        _index     = new SqliteNoteIndex();

        foreach (var d in new[] { "chats", "decisions", "patterns", "lessons",
                                   ".obsidian", Path.Combine(".obsidian", "memctl") })
            Directory.CreateDirectory(Path.Combine(_vaultPath, d));

        _index.Initialize(Path.Combine(_vaultPath, ".obsidian", "memctl", "index.db"));
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

    // AC-1: no flag → zero CheckContradiction calls
    [Fact]
    public async Task NoFlag_ZeroCheckContradictionCalls()
    {
        WriteConvNote("2026-05-07-ac1.md");
        WriteExistingDecision("existing-ac1", "Always use X");

        var llm = new TrackingLlmClient(new DistillResult([MakeExtraction("decision", "Always use X")]));
        var op  = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: false);

        Assert.Equal(0, llm.CheckCallCount);
    }

    // AC-2: flag set → CheckContradiction called when candidates found
    [Fact]
    public async Task WithFlag_CheckContradictionCalledWhenCandidatesExist()
    {
        WriteConvNote("2026-05-07-ac2.md");
        WriteExistingDecision("existing-ac2", "Always use X");  // title "Always use X"

        // Extraction title matches existing note title exactly — BM25 phrase search will find it
        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("decision", "Always use X")]),
            checkResult: new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, ""));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        Assert.True(llm.CheckCallCount >= 1, $"Expected CheckContradiction to be called, but call count was {llm.CheckCallCount}");
    }

    // AC-3: KeepNew → existing note archived in file AND excluded from index.GetAll(false)
    [Fact]
    public async Task KeepNew_ExistingNoteArchived()
    {
        WriteConvNote("2026-05-07-ac3.md");
        var existingId = WriteExistingDecision("old-decision", "Old approach is correct");

        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("decision", "Old approach is correct")]),
            checkResult: new ContradictionResult(true, existingId, ContradictionResolution.KeepNew, null, "new is better"));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        var oldFile = Path.Combine(_vaultPath, "decisions", "old-decision.md");
        Assert.True(File.Exists(oldFile));
        var content = File.ReadAllText(oldFile);
        Assert.Contains("archived: true", content);
        Assert.Contains("superseded", content);

        var allActive = _index.GetAll(includeArchived: false).ToList();
        Assert.DoesNotContain(allActive, n => n.Id == existingId);
    }

    // AC-4: KeepExisting → extracted note not written
    [Fact]
    public async Task KeepExisting_ExtractedNoteNotWritten()
    {
        WriteConvNote("2026-05-07-ac4.md");
        var existingId   = WriteExistingDecision("keep-this-one", "The correct approach");
        var filesBefore  = Directory.GetFiles(Path.Combine(_vaultPath, "decisions"), "*.md").Length;

        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("decision", "The correct approach")]),
            checkResult: new ContradictionResult(true, existingId, ContradictionResolution.KeepExisting, null, "existing is better"));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        var filesAfter = Directory.GetFiles(Path.Combine(_vaultPath, "decisions"), "*.md").Length;
        Assert.Equal(filesBefore, filesAfter);
    }

    // AC-5: Merge → new file written with merged content, existing archived
    [Fact]
    public async Task Merge_WritesNewFile_ArchivesExisting()
    {
        WriteConvNote("2026-05-07-ac5.md");
        var existingId    = WriteExistingDecision("merge-target", "Partial approach");
        var mergedContent = "# Combined approach\n\nThis merges old and new insights.";

        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("decision", "Partial approach")]),
            checkResult: new ContradictionResult(true, existingId, ContradictionResolution.Merge, mergedContent, "merge both"));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        var oldFile = Path.Combine(_vaultPath, "decisions", "merge-target.md");
        Assert.Contains("archived: true", File.ReadAllText(oldFile));

        var newFiles = Directory.GetFiles(Path.Combine(_vaultPath, "decisions"), "*.md")
            .Where(f => !f.Contains("merge-target"))
            .ToList();
        Assert.Single(newFiles);
        Assert.Contains("Combined approach", File.ReadAllText(newFiles[0]));
    }

    // AC-6: no contradiction → extracted note written normally
    [Fact]
    public async Task NoContradiction_ExtractedNoteWritten()
    {
        WriteConvNote("2026-05-07-ac6.md");

        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("lesson", "New unique lesson")]),
            checkResult: new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, "no conflict"));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        var files = Directory.GetFiles(Path.Combine(_vaultPath, "lessons"), "*.md");
        Assert.NotEmpty(files);
    }

    // AC-7: archived notes have Weight=0 in index
    [Fact]
    public async Task ArchivedNote_HasWeightZeroInIndex()
    {
        WriteConvNote("2026-05-07-ac7.md");
        var existingId = WriteExistingDecision("weight-test", "Will be archived");

        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("decision", "Will be archived")]),
            checkResult: new ContradictionResult(true, existingId, ContradictionResolution.KeepNew, null, "replace"));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        var indexedNote = _index.GetAll(includeArchived: true).FirstOrDefault(n => n.Id == existingId);
        Assert.NotNull(indexedNote);
        Assert.Equal(0f, indexedNote.Weight);
    }

    // AC-8: CheckContradictionAsync throws → distill continues, note written
    [Fact]
    public async Task ThrowsOnCheck_DistillContinues()
    {
        WriteConvNote("2026-05-07-ac8.md");
        WriteExistingDecision("existing-for-throw", "Existing note for throw test");

        var llm = new ThrowingCheckLlmClient(
            new DistillResult([MakeExtraction("decision", "Existing note for throw test")]));
        var op = new DistillOperator(_vault, _index, llm);

        var outcome = await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        Assert.True(outcome.Success);
        var newFiles = Directory.GetFiles(Path.Combine(_vaultPath, "decisions"), "*.md")
            .Where(f => !f.Contains("existing-for-throw")).ToList();
        Assert.NotEmpty(newFiles);
    }

    // AC-9: same-type filter — decision extraction → candidates from decisions/ only
    [Fact]
    public async Task SameTypeFilter_DecisionOnlyCheckedAgainstDecisions()
    {
        WriteConvNote("2026-05-07-ac9.md");
        WriteExistingDecision("existing-dec-ac9", "Use the factory pattern");
        WriteExistingLesson("existing-les-ac9", "Use the factory pattern");  // same title, different type

        var capturing = new CapturingCheckLlmClient(
            new DistillResult([MakeExtraction("decision", "Use the factory pattern")]));
        var op = new DistillOperator(_vault, _index, capturing);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        Assert.True(capturing.CapturedCandidates.Count > 0, "Expected CheckContradiction to be called with at least one candidate");
        Assert.All(capturing.CapturedCandidates, c => Assert.StartsWith("decisions/", c.FilePath));
        Assert.DoesNotContain(capturing.CapturedCandidates, c => c.FilePath.StartsWith("lessons/"));
    }

    // AC-10: invalid ExistingId from LLM → treated as no contradiction, note written
    [Fact]
    public async Task InvalidExistingId_TreatedAsNoContradiction()
    {
        WriteConvNote("2026-05-07-ac10.md");
        var realId = WriteExistingDecision("real-existing-ac10", "Existing real note");

        var llm = new TrackingLlmClient(
            new DistillResult([MakeExtraction("decision", "Existing real note")]),
            checkResult: new ContradictionResult(true, "nonexistent-id-000000", ContradictionResolution.KeepNew, null, "bad id"));
        var op = new DistillOperator(_vault, _index, llm);

        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false, resolveContradictions: true);

        // New note written (ExistingId not in candidates → no contradiction applied)
        var newFiles = Directory.GetFiles(Path.Combine(_vaultPath, "decisions"), "*.md")
            .Where(f => !f.Contains("real-existing-ac10")).ToList();
        Assert.NotEmpty(newFiles);

        // Original note NOT archived
        var realFile = Path.Combine(_vaultPath, "decisions", "real-existing-ac10.md");
        Assert.DoesNotContain("archived: true", File.ReadAllText(realFile));
    }

    // --- helpers ---

    private void WriteConvNote(string fileName)
    {
        var absPath = Path.Combine(_vaultPath, "chats", fileName);
        File.WriteAllText(absPath, "# Conversation\nContent worth remembering.");
        _index.Upsert(_vault.ParseNote(absPath, _vaultPath));
    }

    private string WriteExistingDecision(string fileName, string title)
    {
        var relPath = $"decisions/{fileName}.md";
        var absPath = Path.Combine(_vaultPath, relPath);
        File.WriteAllText(absPath, $"# {title}\n\n{title}");
        var note = _vault.ParseNote(absPath, _vaultPath);
        _index.Upsert(note);
        return note.Id;
    }

    private string WriteExistingLesson(string fileName, string title)
    {
        var relPath = $"lessons/{fileName}.md";
        var absPath = Path.Combine(_vaultPath, relPath);
        File.WriteAllText(absPath, $"# {title}\n\n{title}");
        var note = _vault.ParseNote(absPath, _vaultPath);
        _index.Upsert(note);
        return note.Id;
    }

    private static DistilledNote MakeExtraction(string type, string title)
        => new(type, title, $"Content for {title}.", ["test"], [], 1.0f, "test rationale");

    // --- LLM stubs ---

    private sealed class TrackingLlmClient(DistillResult distillResult, ContradictionResult? checkResult = null) : ILlmClient
    {
        public int CheckCallCount { get; private set; }

        public Task<NoteEnrichment> EnrichAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new NoteEnrichment());

        public Task<DistillResult> DistillAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(distillResult);

        public Task<ContradictionResult> CheckContradictionAsync(DistilledNote d, IReadOnlyList<Note> candidates, CancellationToken ct = default)
        {
            CheckCallCount++;
            return Task.FromResult(checkResult ?? new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, ""));
        }
    }

    private sealed class ThrowingCheckLlmClient(DistillResult distillResult) : ILlmClient
    {
        public Task<NoteEnrichment> EnrichAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new NoteEnrichment());

        public Task<DistillResult> DistillAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(distillResult);

        public Task<ContradictionResult> CheckContradictionAsync(DistilledNote d, IReadOnlyList<Note> candidates, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated check failure");
    }

    private sealed class CapturingCheckLlmClient(DistillResult distillResult) : ILlmClient
    {
        public List<Note> CapturedCandidates { get; } = [];

        public Task<NoteEnrichment> EnrichAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new NoteEnrichment());

        public Task<DistillResult> DistillAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(distillResult);

        public Task<ContradictionResult> CheckContradictionAsync(DistilledNote d, IReadOnlyList<Note> candidates, CancellationToken ct = default)
        {
            CapturedCandidates.AddRange(candidates);
            return Task.FromResult(new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, ""));
        }
    }
}
