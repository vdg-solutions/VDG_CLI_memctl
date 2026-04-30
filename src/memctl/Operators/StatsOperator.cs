using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class StatsOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var (noteCount, tagCount, linkCount, indexBytes) = index.GetStats();
        return MemctlOutcome.Ok("stats", "Vault statistics",
            new VaultStats(noteCount, tagCount, linkCount, indexBytes, Path.GetFullPath(vaultPath)));
    }
}
