using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class SearchDateOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, DateTime? from, DateTime? to, int limit)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var notes = index.SearchByDate(from, to, limit);

        return MemctlOutcome.Ok("search-date", $"{notes.Count} results",
            new SearchDateHitsResult(from?.ToString("O"), to?.ToString("O"), notes));
    }
}
