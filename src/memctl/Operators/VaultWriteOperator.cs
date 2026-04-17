using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class VaultWriteOperator(IVaultReader vaultReader, INoteIndex index, GemmaEmbeddingEngine embedding)
{
    public MemctlOutcome ExecuteCreate(string vaultPath, string content, string? title, string? folder, string? filename)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var resolvedTitle = title ?? ExtractTitle(content);
        var relativeFile  = BuildRelativePath(folder, filename, resolvedTitle);
        var absolutePath  = Path.GetFullPath(Path.Combine(vaultPath, relativeFile));

        if (!IsPathSafe(vaultPath, absolutePath))
            return MemctlOutcome.Fail("create", "Path traversal detected — folder must be inside vault root");

        var note = new Note
        {
            Id       = Guid.NewGuid().ToString("N")[..16],
            FilePath = relativeFile,
            Title    = resolvedTitle,
            Content  = content,
            Tags     = [],
            Links    = [],
            Created  = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        var emb    = embedding.Embed($"{note.Title}\n{note.Content}");
        var stored = note with { Embedding = emb };

        vaultReader.WriteNote(stored, vaultPath, relativeFile);
        index.Upsert(stored);

        return MemctlOutcome.Ok("create", $"Created note: {note.Title}",
            new { id = note.Id, file = relativeFile, title = note.Title });
    }

    public MemctlOutcome ExecuteUpdate(string vaultPath, string id, string content)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var existing = index.GetById(id) ?? index.GetByFilePath(id);
        if (existing is null)
            return MemctlOutcome.Fail("update", $"Note not found: {id}");

        var updated = existing with { Content = content, Modified = DateTime.UtcNow };
        var emb     = embedding.Embed($"{updated.Title}\n{updated.Content}");
        var stored  = updated with { Embedding = emb };

        vaultReader.WriteNote(stored, vaultPath, existing.FilePath);
        index.Upsert(stored);

        return MemctlOutcome.Ok("update", $"Updated note: {updated.Title}",
            new { id = updated.Id, file = updated.FilePath, title = updated.Title });
    }

    public MemctlOutcome ExecuteAppend(string vaultPath, string id, string content)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var existing = index.GetById(id) ?? index.GetByFilePath(id);
        if (existing is null)
            return MemctlOutcome.Fail("append", $"Note not found: {id}");

        var separator = existing.Content.EndsWith('\n') ? "" : "\n";
        var combined  = existing.Content + separator + content;
        var appended  = existing with { Content = combined, Modified = DateTime.UtcNow };
        var emb       = embedding.Embed($"{appended.Title}\n{appended.Content}");
        var stored    = appended with { Embedding = emb };

        vaultReader.WriteNote(stored, vaultPath, existing.FilePath);
        index.Upsert(stored);

        return MemctlOutcome.Ok("append", $"Appended to: {appended.Title}",
            new { id = appended.Id, file = appended.FilePath, title = appended.Title });
    }

    private static bool IsPathSafe(string vaultPath, string absoluteResolved)
        => absoluteResolved.StartsWith(
            Path.GetFullPath(vaultPath) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

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

    private static string BuildRelativePath(string? folder, string? filename, string title)
    {
        var name = (filename is { Length: > 0 } fn ? fn : SanitizeFileName(title)) + ".md";
        return folder is { Length: > 0 } f ? $"{f.Trim('/')}/{name}" : name;
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(title.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim('_', ' ');
    }
}
