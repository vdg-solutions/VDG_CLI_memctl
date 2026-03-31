using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class SearchTagsOperator(INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string[] tags, bool matchAll, int limit)
    {
        index.Initialize(IngestOperator.DbPath(vaultPath));
        var notes = index.SearchByTags(tags, matchAll, limit);

        return MemctlOutcome.Ok("search-tags", $"{notes.Count} results", new
        {
            tags,
            match_all = matchAll,
            count     = notes.Count,
            results   = notes.Select(n => GetOperator.NoteToData(n)),
        });
    }
}
