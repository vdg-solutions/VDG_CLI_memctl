using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class SearchLinksOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string noteId, int depth)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var source = index.GetById(noteId) ?? index.GetByFilePath(noteId);
        if (source is null)
            return MemctlOutcome.Fail("search-links", $"Note not found: {noteId}");

        var linked = index.GetLinked(source.Id, depth);

        return MemctlOutcome.Ok("search-links", $"{linked.Count} linked notes", new
        {
            source_id = source.Id,
            depth,
            count   = linked.Count,
            results = linked.Select(n => GetOperator.NoteToData(n)),
        });
    }
}
