using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class IngestOperator(IVaultReader vault, INoteIndex index, GemmaEmbeddingEngine? embedding)
{
    private const int SemanticOverdueDays = 14;

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
                var note = vault.ParseNote(file, vaultPath);
                if (embedding != null)
                {
                    var emb = embedding.Embed($"{note.Title}\n{note.Content}");
                    note = note with { Embedding = emb };
                }
                index.Upsert(note);
                added++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  skip {file}: {ex.Message}");
            }
        }

        if (embedding != null)
        {
            // store model metadata for mismatch detection
            index.SetMetadata("model_name", embedding.ModelName);
            index.SetMetadata("model_dim",  embedding.Dim.ToString());
        }

        var model = embedding?.ModelName ?? "none";

        // semantic lint hint
        string? semanticHint = null;
        var lastLint = index.GetMetadata("last_semantic_lint");
        if (lastLint is null)
        {
            semanticHint = "Semantic lint: never run. Run: memctl lint --semantic";
        }
        else if (DateTime.TryParse(lastLint, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastLintDate))
        {
            var daysSince = (DateTime.UtcNow - lastLintDate).TotalDays;
            if (daysSince > SemanticOverdueDays)
                semanticHint = $"Semantic lint not run in {(int)daysSince} days. Run: memctl lint --semantic";
        }
        else
        {
            semanticHint = "Semantic lint: never run. Run: memctl lint --semantic";
        }

        object resultData = semanticHint is not null
            ? new { indexed = added, total = files.Count, vault = vaultPath, model, semantic_lint_hint = semanticHint }
            : new { indexed = added, total = files.Count, vault = vaultPath, model };
        return MemctlOutcome.Ok("ingest", $"Indexed {added}/{files.Count} notes", resultData);
    }

    public static bool NeedsIngest(string vaultPath)
    {
        var dbPath = DbPath(vaultPath);
        if (!File.Exists(dbPath)) return true;
        var dbMtime = File.GetLastWriteTimeUtc(dbPath);
        return Directory.EnumerateFiles(vaultPath, "*.md", SearchOption.AllDirectories)
            .Any(f => File.GetLastWriteTimeUtc(f) > dbMtime);
    }

    internal static string DbPath(string vaultPath) =>
        Path.Combine(vaultPath, ".memctl", "index.db");
}
