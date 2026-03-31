using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

internal static class ModelGuard
{
    /// <summary>Returns a Fail outcome if index was built with a different model, null if OK.</summary>
    public static MemctlOutcome? Check(INoteIndex index, GemmaEmbeddingEngine embedding, string action)
    {
        var storedModel = index.GetMetadata("model_name");
        if (storedModel is null) return null;  // not yet indexed — let operator handle it

        if (storedModel != embedding.ModelName)
            return MemctlOutcome.Fail(action,
                $"Index built with '{storedModel}', current model is '{embedding.ModelName}'. Re-run `memctl ingest` to rebuild.");

        return null;
    }
}
