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
        return MemctlOutcome.Ok("get", "Note found", NoteToData(note));
    }

    internal static object NoteToData(Note n, float? score = null) => new
    {
        id           = n.Id,
        file         = n.FilePath,
        title        = n.Title,
        content      = n.Content,
        tags         = n.Tags,
        links        = n.Links,
        created      = n.Created.ToString("O"),
        modified     = n.Modified.ToString("O"),
        weight       = (float)Math.Round(n.Weight, 2),
        access_count = n.AccessCount,
        score,
    };
}
