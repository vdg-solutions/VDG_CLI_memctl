using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class DistillStateStoreTests : IDisposable
{
    private readonly string _vaultPath;
    private readonly ObsidianVaultReader _vault;
    private readonly SqliteNoteIndex _index;

    public DistillStateStoreTests()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), "memctl-test-dss-" + Guid.NewGuid());
        _vault     = new ObsidianVaultReader();
        _index     = new SqliteNoteIndex();

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

    private string StateFilePath => Path.Combine(_vaultPath, ".obsidian", "memctl", "distill-state.json");

    // AC-1: distill-state.json created on first capture
    [Fact]
    public void Increment_CreatesFileOnFirstCall()
    {
        Assert.False(File.Exists(StateFilePath));

        DistillStateStore.Increment(_vaultPath);

        Assert.True(File.Exists(StateFilePath));
    }

    // AC-2: counter increments in CreateNote only, not AppendNote
    [Fact]
    public void Capture_CreateNote_IncrementsCounter_AppendNote_DoesNot()
    {
        InitIndex();
        var op = new CaptureOperator(_vault, _index, null);

        // First call: file doesn't exist → CreateNote
        op.Execute(_vaultPath, "conv-abc", [("user", new string('x', 100))], dryRun: false);
        var (count1, _, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(1, count1);

        // Second call: same conversation ID → AppendNote (file already exists at chats/{date}-conv-abc.md)
        op.Execute(_vaultPath, "conv-abc", [("assistant", new string('y', 100))], dryRun: false);
        var (count2, _, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(1, count2); // must not increment
    }

    // AC-3: context-inject appends recommendation when counter >= threshold
    [Fact]
    public void ContextInject_AppendsRecommendation_WhenThresholdMet()
    {
        // Set threshold=2, then increment twice
        DistillStateStore.SetThreshold(_vaultPath, 2);
        DistillStateStore.Increment(_vaultPath);
        DistillStateStore.Increment(_vaultPath);

        InitIndex();
        var op     = new ContextInjectOperator(_vault, _index);
        var result = op.Execute(_vaultPath, "some query text");

        Assert.NotNull(result);
        Assert.Contains("Distill Recommendation", result);
        Assert.Contains("2/2 conversations", result);
    }

    // AC-4: context-inject does NOT append below threshold
    [Fact]
    public void ContextInject_NoRecommendation_BelowThreshold()
    {
        DistillStateStore.SetThreshold(_vaultPath, 5);
        DistillStateStore.Increment(_vaultPath); // count = 1, threshold = 5

        InitIndex();
        var op     = new ContextInjectOperator(_vault, _index);
        var result = op.Execute(_vaultPath, "query");

        if (result is not null)
            Assert.DoesNotContain("Distill Recommendation", result);
    }

    // AC-5: context-inject returns recommendation even with empty vault
    [Fact]
    public void ContextInject_EmptyVault_ReturnsRecommendation_WhenThresholdMet()
    {
        DistillStateStore.SetThreshold(_vaultPath, 1);
        DistillStateStore.Increment(_vaultPath);

        InitIndex();
        var op     = new ContextInjectOperator(_vault, _index);
        var result = op.Execute(_vaultPath, "query with no vault notes");

        Assert.NotNull(result);
        Assert.Contains("Distill Recommendation", result);
    }

    // AC-6: distill resets counter on success
    [Fact]
    public async Task Distill_ResetsCounter_AfterSuccess()
    {
        DistillStateStore.Increment(_vaultPath);
        DistillStateStore.Increment(_vaultPath);
        var convPath = Path.Combine(_vaultPath, "chats", "2026-05-07-reset.md");
        File.WriteAllText(convPath, "# Conversation\nSome content worth distilling.");
        InitIndex();
        var note = _vault.ParseNote(convPath, _vaultPath);
        _index.Upsert(note);

        var llm = new FixedDistillClient(new DistillResult([]));
        var op  = new DistillOperator(_vault, _index, llm);
        await op.ExecuteAsync(_vaultPath, null, null, dryRun: false);

        var (count, _, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(0, count);
    }

    // AC-7: distill --dry-run does NOT reset counter
    [Fact]
    public async Task Distill_DryRun_DoesNotResetCounter()
    {
        DistillStateStore.Increment(_vaultPath);
        DistillStateStore.Increment(_vaultPath);
        var convPath = Path.Combine(_vaultPath, "chats", "2026-05-07-dry.md");
        File.WriteAllText(convPath, "# Conversation\nContent.");
        InitIndex();
        _index.Upsert(_vault.ParseNote(convPath, _vaultPath));

        var llm = new FixedDistillClient(new DistillResult([]));
        var op  = new DistillOperator(_vault, _index, llm);
        await op.ExecuteAsync(_vaultPath, null, null, dryRun: true);

        var (count, _, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(2, count); // unchanged
    }

    // AC-8: config set distill-threshold changes threshold
    [Fact]
    public void SetThreshold_ChangesThreshold()
    {
        DistillStateStore.Increment(_vaultPath); // count = 1, threshold = 5 (default)
        var (_, thresholdBefore, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(5, thresholdBefore);
        Assert.False(DistillStateStore.ShouldRecommend(_vaultPath));

        DistillStateStore.SetThreshold(_vaultPath, 1);

        var (_, thresholdAfter, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(1, thresholdAfter);
        Assert.True(DistillStateStore.ShouldRecommend(_vaultPath));
    }

    // AC-9: DistillStateStore errors do not crash CaptureOperator
    [Fact]
    public void Increment_IoError_DoesNotCrashCaptureOperator()
    {
        // Make state file path unwritable: create distill-state.json as a directory
        var stateDir = Path.Combine(_vaultPath, ".obsidian", "memctl", "distill-state.json");
        Directory.CreateDirectory(stateDir); // now .json is a directory → write fails

        InitIndex();
        var op = new CaptureOperator(_vault, _index, null);

        // CaptureOperator must not throw even when DistillStateStore.Increment fails
        var ex = Record.Exception(() =>
            op.Execute(_vaultPath, "conv-io", [("user", new string('z', 100))], dryRun: false));

        Assert.Null(ex);
        // Note must have been written
        var files = Directory.GetFiles(Path.Combine(_vaultPath, "chats"), "*.md");
        Assert.NotEmpty(files);
    }

    // AC-10: corrupt JSON falls back to defaults, no crash
    [Fact]
    public void CorruptJson_FallsBackToDefaults()
    {
        File.WriteAllText(StateFilePath, "{ this is not valid json !!!");

        var ex = Record.Exception(() => DistillStateStore.ShouldRecommend(_vaultPath));
        Assert.Null(ex);

        var (count, threshold, _) = DistillStateStore.GetState(_vaultPath);
        Assert.Equal(0, count);
        Assert.Equal(5, threshold);
        Assert.False(DistillStateStore.ShouldRecommend(_vaultPath));
    }

    private void InitIndex()
    {
        _index.Initialize(Path.Combine(_vaultPath, ".obsidian", "memctl", "index.db"));
    }

    private sealed class FixedDistillClient(DistillResult result) : ILlmClient
    {
        public Task<NoteEnrichment> EnrichAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(new NoteEnrichment());

        public Task<DistillResult> DistillAsync(string c, IReadOnlyList<Note> n, CancellationToken ct = default)
            => Task.FromResult(result);
    }
}
