using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class StatsOperator(INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath)
    {
        index.Initialize(IngestOperator.DbPath(vaultPath));
        var (noteCount, tagCount, linkCount, indexBytes) = index.GetStats();
        return MemctlOutcome.Ok("stats", "Vault statistics", new
        {
            note_count  = noteCount,
            tag_count   = tagCount,
            link_count  = linkCount,
            index_bytes = indexBytes,
            vault_path  = Path.GetFullPath(vaultPath),
        });
    }
}
