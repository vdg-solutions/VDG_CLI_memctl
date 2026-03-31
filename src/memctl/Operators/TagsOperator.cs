using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class TagsOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var tags = index.GetTagStats();
        return MemctlOutcome.Ok("tags", $"{tags.Count} tags",
            new { count = tags.Count, tags = tags.Select(t => new { tag = t.Tag, count = t.Count }) });
    }
}
