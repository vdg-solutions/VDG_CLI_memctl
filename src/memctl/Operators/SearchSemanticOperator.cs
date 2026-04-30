using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class SearchSemanticOperator(IVaultReader vaultReader, INoteIndex index, GemmaEmbeddingEngine embedding)
{
    public MemctlOutcome Execute(string vaultPath, string query, int limit, string[]? scopeIds, string? folderPrefix = null)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var mismatch = ModelGuard.Check(index, embedding, "search-semantic");
        if (mismatch is not null) return mismatch;

        var qEmb = embedding.Embed(query);
        var hits = index.SearchSemantic(qEmb, limit, scopeIds, folderPrefix);

        return MemctlOutcome.Ok("search-semantic", $"{hits.Count} results",
            new SearchHitsResult(query, hits));
    }
}
