using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class IngestOperatorPruneTests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly string _vaultPath;
    private readonly IVaultReader _vault;
    private readonly SqliteNoteIndex _index;
    private readonly IngestOperator _op;

    public IngestOperatorPruneTests()
    {
        _tmpRoot   = Path.Combine(Path.GetTempPath(), "memctl-test-prune-" + Guid.NewGuid());
        _vaultPath = Path.Combine(_tmpRoot, ".memctl");
        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian", "memctl"));

        _vault = new ObsidianVaultReader();
        _index = new SqliteNoteIndex();
        _op    = new IngestOperator(_vault, _index, embedding: null);
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

    private void WriteNote(string fileName, string title, string content)
    {
        var note = new Note
        {
            Id       = Guid.NewGuid().ToString("N")[..16],
            FilePath = fileName,
            Title    = title,
            Content  = content,
            Tags     = [],
            Links    = [],
            Created  = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        _vault.WriteNote(note, _vaultPath, fileName);
    }

    [Fact]
    public void Prune_RemovesDeletedFileEntry()
    {
        WriteNote("keep.md", "Keep", "this stays");
        WriteNote("delete.md", "Delete", "this goes");
        _op.Execute(_vaultPath);

        Assert.Equal(2, _index.GetAll().Count);

        File.Delete(Path.Combine(_vaultPath, "delete.md"));
        _op.Execute(_vaultPath);

        var notes = _index.GetAll();
        Assert.Single(notes);
        Assert.Equal("keep.md", notes[0].FilePath);
    }

    [Fact]
    public void Prune_KeepsExistingFiles()
    {
        WriteNote("a.md", "A", "first");
        WriteNote("b.md", "B", "second");
        _op.Execute(_vaultPath);

        File.Delete(Path.Combine(_vaultPath, "b.md"));
        _op.Execute(_vaultPath);

        var notes = _index.GetAll();
        Assert.Single(notes);
        Assert.Equal("a.md", notes[0].FilePath);
    }

    [Fact]
    public void Prune_NoOpWhenNoDeletions()
    {
        WriteNote("note.md", "Note", "content");
        _op.Execute(_vaultPath);

        var result1 = _op.Execute(_vaultPath);
        Assert.True(result1.Success);

        var report = Assert.IsType<IngestReport>(result1.Data);
        Assert.Equal(0, report.Pruned);
        Assert.Single(_index.GetAll());
    }

    [Fact]
    public void Prune_EmptyVaultFirstIngest_ReturnsPrunedZero()
    {
        var result = _op.Execute(_vaultPath);
        var report = Assert.IsType<IngestReport>(result.Data);
        Assert.Equal(0, report.Pruned);
        Assert.Equal(0, report.Indexed);
    }
}
