using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Config;

namespace Memctl.Operators;

public sealed class ModelListOperator
{
    public MemctlOutcome Execute()
    {
        var models = MemctlConfig.ListModels()
            .Select(m => new
            {
                name       = m.Name,
                ready      = m.Ready,
                size_mb    = m.SizeMb,
                is_default = m.IsDefault,
            })
            .ToList();

        return MemctlOutcome.Ok("model-list", $"{models.Count} model(s)",
            new { default_model = MemctlConfig.Load().DefaultModel, models });
    }
}
