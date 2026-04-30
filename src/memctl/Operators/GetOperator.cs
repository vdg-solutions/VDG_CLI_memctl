using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class GetOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string idOrPath)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var note = index.GetById(idOrPath)
                ?? index.GetByFilePath(idOrPath);

        if (note is null)
            return MemctlOutcome.Fail("get", $"Note not found: {idOrPath}");

        index.IncrementAccess(note.Id);
        return MemctlOutcome.Ok("get", "Note found", note);
    }
}
