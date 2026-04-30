using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class ModelDownloadOperator
{
    public async Task<MemctlOutcome> ExecuteAsync()
    {
        if (StatusOperator.IsModelReady(out var modelPath, out var modelMb))
            return MemctlOutcome.Ok("model-download", "Model already present",
                new ModelInfo(modelPath, modelMb));

        try
        {
            // CreateAsync triggers download if files are missing
            using var engine = await GemmaEmbeddingEngine.CreateAsync();
            StatusOperator.IsModelReady(out modelPath, out modelMb);

            return MemctlOutcome.Ok("model-download", "Model downloaded",
                new ModelInfo(modelPath, modelMb));
        }
        catch (Exception ex)
        {
            return MemctlOutcome.Fail("model-download", $"Download failed: {ex.Message}");
        }
    }
}
