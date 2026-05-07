using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class DeleteOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string noteId)
    {
        index.Initialize(IngestOperator.DbPath(vaultPath));
        // Snapshot before ingest so pruned-on-ingest notes are still findable
        var noteSnapshot = index.GetById(noteId);

        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        var note = index.GetById(noteId) ?? noteSnapshot;
        if (note is null)
            return MemctlOutcome.Fail("delete", $"Note not found: {noteId}");

        var absPath = Path.Combine(vaultPath, note.FilePath);
        if (File.Exists(absPath))
            File.Delete(absPath);

        index.Delete(noteId);
        return MemctlOutcome.Ok("delete", $"Deleted: {note.FilePath}", note);
    }
}
