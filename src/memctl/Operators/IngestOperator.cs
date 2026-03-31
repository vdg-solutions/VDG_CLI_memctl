using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class IngestOperator(IVaultReader vault, INoteIndex index, GemmaEmbeddingEngine embedding)
{
    public MemctlOutcome Execute(string vaultPath)
    {
        if (!Directory.Exists(vaultPath))
            return MemctlOutcome.Fail("ingest", $"Vault not found: {vaultPath}");

        var dbPath = DbPath(vaultPath);
        index.Initialize(dbPath);

        var files = vault.EnumerateMarkdownFiles(vaultPath).ToList();
        var added = 0;

        foreach (var file in files)
        {
            try
            {
                var note      = vault.ParseNote(file, vaultPath);
                var emb       = embedding.Embed($"{note.Title}\n{note.Content}");
                var withEmbed = note with { Embedding = emb };
                index.Upsert(withEmbed);
                added++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  skip {file}: {ex.Message}");
            }
        }

        // store model metadata for mismatch detection
        index.SetMetadata("model_name", embedding.ModelName);
        index.SetMetadata("model_dim",  embedding.Dim.ToString());

        return MemctlOutcome.Ok("ingest", $"Indexed {added}/{files.Count} notes",
            new { indexed = added, total = files.Count, vault = vaultPath, model = embedding.ModelName });
    }

    internal static string DbPath(string vaultPath) =>
        Path.Combine(vaultPath, ".memctl", "index.db");
}
