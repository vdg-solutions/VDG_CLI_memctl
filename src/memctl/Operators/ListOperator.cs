using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class ListOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string? tag, int limit, bool includeArchived = false)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var notes = tag is not null
            ? index.SearchByTags([tag], matchAll: false, limit)
            : index.GetAll(includeArchived).Take(limit).ToList();

        return MemctlOutcome.Ok("list", $"{notes.Count} notes",
            new { count = notes.Count, notes = notes.Select(n => GetOperator.NoteToData(n)) });
    }
}
