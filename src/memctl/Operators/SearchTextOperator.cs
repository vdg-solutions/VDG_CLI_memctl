using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class SearchTextOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string query, int limit, string? folderPrefix = null)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var hits = index.SearchBm25(query, limit, folderPrefix);

        return MemctlOutcome.Ok("search-text", $"{hits.Count} results",
            new SearchHitsResult(query, hits));
    }
}
