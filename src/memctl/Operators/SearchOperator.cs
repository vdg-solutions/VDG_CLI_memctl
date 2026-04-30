using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

/// <summary>Hybrid search: RRF fusion of BM25 + semantic.</summary>
public sealed class SearchOperator(IVaultReader vaultReader, INoteIndex index, GemmaEmbeddingEngine embedding)
{
    private const int RrfK = 60;  // RRF constant

    public MemctlOutcome Execute(string vaultPath, string query, int limit, string? folderPrefix = null)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var mismatch = ModelGuard.Check(index, embedding, "search");
        if (mismatch is not null) return mismatch;

        var bm25Hits     = index.SearchBm25(query, limit * 2, folderPrefix);
        var qEmb         = embedding.Embed(query);
        var semanticHits = index.SearchSemantic(qEmb, limit * 2, folderPrefix: folderPrefix);

        var scores = new Dictionary<string, float>();

        AddRrfScores(scores, bm25Hits,     weight: 1.0f);
        AddRrfScores(scores, semanticHits, weight: 1.0f);

        var allNotes = bm25Hits.Concat(semanticHits)
            .GroupBy(h => h.Note.Id)
            .ToDictionary(g => g.Key, g => g.First().Note);

        var results = scores
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new SearchHit
            {
                Note    = allNotes[kv.Key],
                Score   = kv.Value,
                Snippet = bm25Hits.FirstOrDefault(h => h.Note.Id == kv.Key)?.Snippet,
            })
            .ToList();

        return MemctlOutcome.Ok("search", $"{results.Count} results",
            new SearchHitsResult(query, results));
    }

    private static void AddRrfScores(Dictionary<string, float> scores, IReadOnlyList<SearchHit> hits, float weight)
    {
        for (var i = 0; i < hits.Count; i++)
        {
            var id = hits[i].Note.Id;
            var rrf = weight / (RrfK + i + 1);
            scores[id] = scores.GetValueOrDefault(id) + rrf;
        }
    }
}
