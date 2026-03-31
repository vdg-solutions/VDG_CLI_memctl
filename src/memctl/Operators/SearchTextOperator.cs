using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class SearchTextOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string query, int limit)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var hits = index.SearchBm25(query, limit);

        return MemctlOutcome.Ok("search-text", $"{hits.Count} results", new
        {
            query,
            count   = hits.Count,
            results = hits.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }
}
