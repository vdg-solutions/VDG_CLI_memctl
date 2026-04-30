using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Config;

namespace Memctl.Operators;

public sealed class ModelListOperator
{
    public MemctlOutcome Execute()
    {
        var entries = MemctlConfig.ListModels()
            .Select(m => new ModelEntry(m.Name, m.Ready, m.SizeMb, m.IsDefault))
            .ToList();

        return MemctlOutcome.Ok("model-list", $"{entries.Count} model(s)",
            new ModelList(MemctlConfig.Load().DefaultModel, entries));
    }
}
