using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Config;

namespace Memctl.Operators;

public sealed class ModelUseOperator
{
    public MemctlOutcome Execute(string modelName)
    {
        var models = MemctlConfig.ListModels().ToList();
        var found  = models.FirstOrDefault(m => m.Name == modelName);

        if (found == default)
            return MemctlOutcome.Fail("model-use",
                $"Model '{modelName}' not found. Run `memctl model download` first.");

        if (!found.Ready)
            return MemctlOutcome.Fail("model-use",
                $"Model '{modelName}' is not fully downloaded. Run `memctl model download` first.");

        MemctlConfig.Save(new MemctlConfig { DefaultModel = modelName });

        return MemctlOutcome.Ok("model-use", $"Default model → {modelName}",
            new { model = modelName });
    }
}
