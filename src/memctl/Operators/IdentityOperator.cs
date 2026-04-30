using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class IdentityOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome ExecuteSet(string vaultPath, string idOrPath)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var note = index.GetById(idOrPath) ?? index.GetByFilePath(idOrPath);
        if (note is null)
            return MemctlOutcome.Fail("identity", $"Note not found: {idOrPath}");

        index.SetMetadata("identity_note_id", note.Id);
        index.SetWeight(note.Id, 1.0f);

        return MemctlOutcome.Ok("identity", $"Identity note set: {note.Title}", note);
    }

    public MemctlOutcome ExecuteGet(string vaultPath)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var noteId = index.GetMetadata("identity_note_id");
        if (noteId is null)
            return MemctlOutcome.Ok("identity", "No identity note set", null);

        var note = index.GetById(noteId);
        if (note is null)
            return MemctlOutcome.Ok("identity", "Identity note not found (may have been deleted)", null);

        return MemctlOutcome.Ok("identity", "Identity note", note);
    }
}
