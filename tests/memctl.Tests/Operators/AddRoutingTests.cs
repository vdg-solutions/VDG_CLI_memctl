using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class AddRoutingTests : IDisposable
{
    private readonly string       _tmpRoot;
    private readonly string       _vaultPath;
    private readonly IVaultReader  _vault;
    private readonly SqliteNoteIndex _index;
    private readonly AddOperator   _op;

    public AddRoutingTests()
    {
        _tmpRoot   = Path.Combine(Path.GetTempPath(), "memctl-test-add-routing-" + Guid.NewGuid());
        _vaultPath = Path.Combine(_tmpRoot, ".memctl");
        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian", "memctl"));

        _vault = new ObsidianVaultReader();
        _index = new SqliteNoteIndex();
        _index.Initialize(Path.Combine(_vaultPath, ".obsidian", "memctl", "index.db"));
        _op    = new AddOperator(_vault, _index, embedding: null);
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

    private async Task<(MemctlOutcome outcome, Note note)> Add(string content, string[]? tags = null, string? fileName = null)
    {
        var outcome = await _op.ExecuteAsync(_vaultPath, content, title: "Test Note", tags: tags, fileName: fileName, llm: null);
        var note    = (Note)outcome.Data!;
        return (outcome, note);
    }

    // AC-1
    [Fact]
    public async Task GoldenRule_RoutesToLessons()
    {
        var (_, note) = await Add("content", ["golden-rule"]);
        Assert.StartsWith("lessons/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-2
    [Fact]
    public async Task AntiPattern_RoutesToLessons()
    {
        var (_, note) = await Add("content", ["anti-pattern"]);
        Assert.StartsWith("lessons/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-3
    [Fact]
    public async Task Insight_RoutesToLessons()
    {
        var (_, note) = await Add("content", ["insight"]);
        Assert.StartsWith("lessons/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-4
    [Fact]
    public async Task DreamLog_RoutesToLessons()
    {
        var (_, note) = await Add("content", ["dream-log"]);
        Assert.StartsWith("lessons/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-5
    [Fact]
    public async Task QcRule_RoutesToPatterns()
    {
        var (_, note) = await Add("content", ["qc-rule"]);
        Assert.StartsWith("patterns/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-9: session tag → chats/
    [Fact]
    public async Task Session_RoutesToChats()
    {
        var (_, note) = await Add("content", ["session", "task-42"]);
        Assert.StartsWith("chats/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-10: first match wins — golden-rule before session → lessons
    [Fact]
    public async Task FirstMatchWins_GoldenRuleBeforeSession()
    {
        var (_, note) = await Add("content", ["golden-rule", "session"]);
        Assert.StartsWith("lessons/", note.FilePath);
        Assert.False(note.FilePath.StartsWith("chats/"));
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-11: case-insensitive
    [Fact]
    public async Task GoldenRule_CaseInsensitive()
    {
        var (_, note) = await Add("content", ["Golden-Rule"]);
        Assert.StartsWith("lessons/", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-12: unmapped tag → vault root (no slash)
    [Fact]
    public async Task UnmappedTag_RoutesToVaultRoot()
    {
        var (_, note) = await Add("content", ["user-preference"]);
        Assert.False(note.FilePath.Contains('/'));
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-13: no tags → vault root
    [Fact]
    public async Task NoTags_RoutesToVaultRoot()
    {
        var (_, note) = await Add("content", tags: null);
        Assert.False(note.FilePath.Contains('/'));
        Assert.True(File.Exists(Path.Combine(_vaultPath, note.FilePath)));
    }

    // AC-14: explicit --file overrides routing
    [Fact]
    public async Task ExplicitFile_SkipsRouting()
    {
        var (_, note) = await Add("content", ["golden-rule"], fileName: "custom/note.md");
        Assert.Equal("custom/note.md", note.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, "custom/note.md")));
    }

    // AC-15: disk path matches index path (catches WriteNote/index mismatch)
    [Fact]
    public async Task DiskPath_MatchesIndexPath()
    {
        var (_, note) = await Add("content", ["golden-rule"]);
        var indexed   = _index.GetById(note.Id);
        Assert.NotNull(indexed);
        Assert.Equal(note.FilePath, indexed.FilePath);
        Assert.True(File.Exists(Path.Combine(_vaultPath, indexed.FilePath)));
    }

    // AC-16: subdir auto-created if absent
    [Fact]
    public async Task Subdir_AutoCreated()
    {
        Assert.False(Directory.Exists(Path.Combine(_vaultPath, "lessons")));
        await Add("content", ["golden-rule"]);
        Assert.True(Directory.Exists(Path.Combine(_vaultPath, "lessons")));
    }
}
