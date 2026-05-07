using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Vault;

namespace Memctl.Operators;

public sealed class DistillOperator(IVaultReader vaultReader, INoteIndex index, ILlmClient llmClient)
{
    private static readonly string[] FolderByType = ["decisions", "patterns", "lessons"];

    public async Task<MemctlOutcome> ExecuteAsync(
        string    vaultPath,
        string?   conversationId,
        DateTime? since,
        bool      dryRun,
        bool      resolveContradictions = false,
        CancellationToken ct = default)
    {
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var candidates = GetCandidates(vaultPath, conversationId);
        if (since.HasValue)
            candidates = candidates.Where(n => ParseDateFromPath(n.FilePath) >= since.Value).ToList();

        // Filter out already-distilled notes (check file frontmatter for idempotency across re-ingest)
        var pending = candidates.Where(n => !IsDistilled(vaultPath, n)).ToList();

        if (pending.Count == 0)
            return MemctlOutcome.Ok("distill", "no conversations to distill");

        // Top-50 non-chat notes as context for LLM link validation
        var contextNotes = index.GetAll()
            .Where(n => !n.FilePath.StartsWith("chats/"))
            .Take(50)
            .ToList();

        var totalExtracted = 0;

        foreach (var conv in pending)
        {
            var result = await llmClient.DistillAsync(conv.Content, contextNotes, ct);

            if (dryRun)
            {
                foreach (var ex in result.Extractions)
                    Console.WriteLine($"[dry-run] {ex.Type}/{ex.Title} (weight {ex.Weight:F1}) — {ex.Rationale}");
                continue;
            }

            var writtenPaths = new List<string>();
            foreach (var exRaw in result.Extractions)
            {
                var ex = await ApplyContradictionCheckAsync(vaultPath, exRaw, resolveContradictions, ct);
                if (ex is null) continue; // KeepExisting

                var folder      = MapFolder(ex.Type);
                var safeTitle   = SanitizeFileName(ex.Title);
                var relPath     = $"{folder}/{safeTitle}.md";
                var absPath     = Path.Combine(vaultPath, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);

                // Validate links against index — drop hallucinated targets
                var validLinks = ex.Links
                    .Where(l => index.SearchBm25(l, 1).Count > 0)
                    .ToArray();

                var note = new Note
                {
                    Id       = Guid.NewGuid().ToString("N")[..16],
                    FilePath = relPath,
                    Title    = ex.Title,
                    Content  = ex.Content,
                    Tags     = ex.Tags,
                    Links    = validLinks,
                    Created  = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                    Weight   = Math.Clamp(ex.Weight, 1.0f, 1.5f),
                };
                vaultReader.WriteNote(note, vaultPath, relPath);
                index.Upsert(note);
                writtenPaths.Add(relPath);
                totalExtracted++;
            }

            if (writtenPaths.Count > 0 || result.Extractions.Length == 0)
            {
                var convAbsPath = Path.Combine(vaultPath, conv.FilePath);
                vaultReader.MarkAsDistilled(convAbsPath, DateTime.UtcNow, [.. writtenPaths]);
                index.Upsert(conv with { Modified = DateTime.UtcNow });
            }
        }

        if (!dryRun && pending.Count > 0)
            DistillStateStore.Reset(vaultPath);

        var msg = dryRun
            ? $"dry-run: {pending.Count} conversations, {totalExtracted} extractions"
            : $"{pending.Count} conversations distilled, {totalExtracted} notes extracted";
        return MemctlOutcome.Ok("distill", msg);
    }

    private List<Note> GetCandidates(string vaultPath, string? conversationId)
    {
        if (conversationId is not null)
        {
            var note = index.GetById(conversationId)
                    ?? index.SearchBm25(conversationId, 1, folderPrefix: "chats/").FirstOrDefault()?.Note;
            return note is not null ? [note] : [];
        }
        return [.. index.GetAll().Where(n => n.FilePath.StartsWith("chats/"))];
    }

    private bool IsDistilled(string vaultPath, Note note)
    {
        var absPath = Path.Combine(vaultPath, note.FilePath);
        if (!File.Exists(absPath)) return false;
        var raw   = File.ReadAllText(absPath);
        var idx   = raw.IndexOf("distilled: true", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 && idx < 500; // must be in frontmatter (first 500 chars)
    }

    private static DateTime ParseDateFromPath(string filePath)
    {
        // chats/2026-05-07-{id}.md → extract first 10 chars of filename
        var name = Path.GetFileName(filePath);
        if (name.Length >= 10 && DateTime.TryParse(name[..10], out var d))
            return d;
        return DateTime.MinValue;
    }

    private static string MapFolder(string type) => type.ToLowerInvariant() switch
    {
        "decision" => "decisions",
        "pattern"  => "patterns",
        _          => "lessons",
    };

    private static string SanitizeFileName(string title)
        => string.Concat(title.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-'))
                 .Trim('-')
                 .ToLowerInvariant();

    // Returns null to signal "skip this note" (KeepExisting), or the (possibly mutated) note to write.
    private async Task<DistilledNote?> ApplyContradictionCheckAsync(
        string vaultPath, DistilledNote ex, bool resolveContradictions, CancellationToken ct)
    {
        if (!resolveContradictions) return ex;

        var folderPrefix = MapFolder(ex.Type) + "/";
        var candidates   = index.SearchBm25(ex.Title, 5, folderPrefix: folderPrefix)
                               .Select(h => h.Note)
                               .ToList();
        if (candidates.Count == 0) return ex;

        ContradictionResult cr;
        try
        {
            cr = await llmClient.CheckContradictionAsync(ex, candidates, ct);
        }
        catch (Exception e)
        {
            EventLog.Record(vaultPath, "operator_run", "error", "distill", $"CheckContradiction failed: {e.Message}");
            return ex;
        }

        if (!cr.Contradicts) return ex;

        var existingNote = cr.ExistingId is not null
            ? candidates.FirstOrDefault(c => c.Id == cr.ExistingId)
            : null;

        if (existingNote is null) return ex; // invalid ExistingId — treat as no contradiction

        Console.Error.WriteLine($"[distill] contradiction resolved: {cr.Resolution} — {cr.Rationale}");

        if (cr.Resolution == ContradictionResolution.KeepExisting)
            return null;

        ArchiveNote(vaultPath, existingNote);

        if (cr.Resolution == ContradictionResolution.Merge && cr.MergedContent is not null)
            return ex with { Content = cr.MergedContent };

        return ex;
    }

    private void ArchiveNote(string vaultPath, Note note)
    {
        var absPath = Path.Combine(vaultPath, note.FilePath);
        if (!File.Exists(absPath)) return;
        var archived = note with
        {
            Archived = true,
            Weight   = 0f,
            Tags     = [.. note.Tags.Append("superseded").Distinct()],
            Modified = DateTime.UtcNow,
        };
        vaultReader.WriteNote(archived, vaultPath, note.FilePath);
        // ApplyDecay updates weight + archived atomically — Upsert preserves these fields on conflict
        index.ApplyDecay(note.Id, 0f, archived: true);
    }
}
