using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Index;
using Memctl.Implementations.Vault;
using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class DeleteOperatorTests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly string _vaultPath;
    private readonly IVaultReader _vault;
    private readonly SqliteNoteIndex _index;
    private readonly IngestOperator _ingest;
    private readonly DeleteOperator _op;

    public DeleteOperatorTests()
    {
        _tmpRoot   = Path.Combine(Path.GetTempPath(), "memctl-test-delete-" + Guid.NewGuid());
        _vaultPath = Path.Combine(_tmpRoot, ".memctl");
        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_vaultPath, ".obsidian", "memctl"));

        _vault  = new ObsidianVaultReader();
        _index  = new SqliteNoteIndex();
        _ingest = new IngestOperator(_vault, _index, embedding: null);
        _op     = new DeleteOperator(_vault, _index);
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

    private Note WriteAndIngest(string fileName, string title)
    {
        var note = new Note
        {
            Id       = Guid.NewGuid().ToString("N")[..16],
            FilePath = fileName,
            Title    = title,
            Content  = $"# {title}",
            Tags     = [],
            Links    = [],
            Created  = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        _vault.WriteNote(note, _vaultPath, fileName);
        _ingest.Execute(_vaultPath);
        return note;
    }

    [Fact]
    public void Delete_RemovesFileAndIndexEntry()
    {
        var note    = WriteAndIngest("delete-me.md", "Delete Me");
        var absPath = Path.Combine(_vaultPath, note.FilePath);

        var outcome = _op.Execute(_vaultPath, note.Id);

        Assert.True(outcome.Success);
        Assert.Equal("delete", outcome.Action);
        Assert.Null(_index.GetById(note.Id));
        Assert.False(File.Exists(absPath));
    }

    [Fact]
    public void Delete_ReturnsFailWhenIdNotFound()
    {
        var outcome = _op.Execute(_vaultPath, "0000000000000000");

        Assert.False(outcome.Success);
        Assert.Equal("delete", outcome.Action);
    }

    [Fact]
    public void Delete_SucceedsWhenFileAlreadyMissing()
    {
        var note    = WriteAndIngest("gone-from-disk.md", "Gone");
        var absPath = Path.Combine(_vaultPath, note.FilePath);
        File.Delete(absPath);

        var outcome = _op.Execute(_vaultPath, note.Id);

        Assert.True(outcome.Success);
        Assert.Null(_index.GetById(note.Id));
        Assert.False(File.Exists(absPath));
    }
}
