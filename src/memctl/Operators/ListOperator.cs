using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class ListOperator(INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string? tag, int limit)
    {
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var notes = tag is not null
            ? index.SearchByTags([tag], matchAll: false, limit)
            : index.GetAll().Take(limit).ToList();

        return MemctlOutcome.Ok("list", $"{notes.Count} notes",
            new { count = notes.Count, notes = notes.Select(n => GetOperator.NoteToData(n)) });
    }
}
