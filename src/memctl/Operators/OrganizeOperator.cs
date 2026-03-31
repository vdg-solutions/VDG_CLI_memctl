using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class OrganizeOperator(IVaultReader vault, INoteIndex index, ILlmClient llm)
{
    public async Task<MemctlOutcome> ExecuteAsync(string vaultPath, DateTime? since, CancellationToken ct = default)
    {
        if (!Directory.Exists(vaultPath))
            return MemctlOutcome.Fail("organize", $"Vault not found: {vaultPath}");

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var notes = since.HasValue
            ? index.GetAll().Where(n => n.Modified >= since.Value).ToList()
            : index.GetAll();

        var allNotes   = index.GetAll();
        var updated    = 0;
        var errors     = 0;

        foreach (var note in notes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var enrichment = await llm.EnrichAsync(note.Content, allNotes, ct);
                if (enrichment.Tags.Length == 0 && enrichment.Links.Length == 0) continue;

                var absolutePath = Path.Combine(vaultPath, note.FilePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(absolutePath)) continue;

                vault.UpdateFrontmatter(absolutePath, enrichment.Tags, enrichment.Links);

                var updated_note = note with
                {
                    Tags     = enrichment.Tags,
                    Links    = enrichment.Links,
                    Modified = DateTime.UtcNow,
                };
                index.Upsert(updated_note);
                updated++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  skip {note.FilePath}: {ex.Message}");
                errors++;
            }
        }

        return MemctlOutcome.Ok("organize", $"Organized {updated} notes",
            new { updated, errors, vault = vaultPath });
    }
}
