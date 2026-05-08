using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class AddOperator(IVaultReader vault, INoteIndex index, GemmaEmbeddingEngine? embedding)
{
    private static readonly (string[] Tags, string Subdir)[] TagSubdirMap =
    [
        (["golden-rule", "anti-pattern", "insight", "dream-log"], "lessons"),
        (["qc-rule", "qc-error", "qc-feedback"],                  "patterns"),
        (["decisions", "adr"],                                     "decisions"),
        (["session"],                                              "chats"),
    ];

    public async Task<MemctlOutcome> ExecuteAsync(
        string vaultPath,
        string content,
        string? title,
        string[]? tags,
        string? fileName,
        ILlmClient? llm)
    {
        if (!Directory.Exists(vaultPath))
            vault.InitVaultStructure(vaultPath);

        var dbPath = IngestOperator.DbPath(vaultPath);
        index.Initialize(dbPath);

        var enriched   = new NoteEnrichment();
        var existingNotes = index.GetAll();

        if (llm is not null)
        {
            try { enriched = await llm.EnrichAsync(content, existingNotes); }
            catch (Exception ex) { Console.Error.WriteLine($"LLM enrichment failed: {ex.Message}"); }
        }

        var resolvedTitle = title ?? (enriched.Title.Length > 0 ? enriched.Title : ExtractTitle(content));
        var resolvedTags  = tags ?? enriched.Tags;
        var resolvedLinks = enriched.Links;

        var subdir   = fileName is null ? ResolveSubdir(resolvedTags) : null;
        var filePath = fileName
            ?? (subdir is not null
                ? subdir + "/" + SanitizeFileName(resolvedTitle) + ".md"
                : SanitizeFileName(resolvedTitle) + ".md");

        var note = new Note
        {
            Id       = Guid.NewGuid().ToString("N")[..16],
            FilePath = filePath,
            Title    = resolvedTitle,
            Content  = content,
            Tags     = resolvedTags,
            Links    = resolvedLinks,
            Created  = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };

        float[]? emb = embedding?.Embed($"{note.Title}\n{note.Content}");
        var withEmbed = note with { Embedding = emb };

        vault.WriteNote(withEmbed, vaultPath, filePath);
        index.Upsert(withEmbed);

        return MemctlOutcome.Ok("add", $"Added note: {note.Title}", withEmbed);
    }

    private static string? ResolveSubdir(string[]? tags)
    {
        if (tags is null or { Length: 0 }) return null;
        foreach (var (routeTags, subdir) in TagSubdirMap)
            if (routeTags.Any(rt => tags.Any(t => string.Equals(t, rt, StringComparison.OrdinalIgnoreCase))))
                return subdir;
        return null;
    }

    private static string ExtractTitle(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.StartsWith("# ")) return t[2..].Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t.Trim()[..Math.Min(t.Trim().Length, 60)];
        }
        return $"Note {DateTime.UtcNow:yyyy-MM-dd}";
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(title.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return clean.Trim('_', ' ');
    }
}
