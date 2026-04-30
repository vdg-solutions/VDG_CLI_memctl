using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Implementations.Vault;

public sealed class ObsidianVaultReader : IVaultReader
{
    private static readonly Regex WikiLinkPattern = new(@"\[\[([^\]|#]+)(?:[|#][^\]]*)?\]\]", RegexOptions.Compiled);
    private static readonly Regex TagPattern      = new(@"(?:^|\s)#([\w/-]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex FrontmatterPattern = new(@"^---\r?\n(.*?)\r?\n---\r?\n?", RegexOptions.Singleline | RegexOptions.Compiled);

    public IEnumerable<string> EnumerateMarkdownFiles(string vaultPath) =>
        Directory.EnumerateFiles(vaultPath, "*.md", SearchOption.AllDirectories)
                 .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".obsidian" + Path.DirectorySeparatorChar)
                          && !f.Contains(Path.DirectorySeparatorChar + ".memctl" + Path.DirectorySeparatorChar));

    public Note ParseNote(string absolutePath, string vaultPath)
    {
        var raw      = File.ReadAllText(absolutePath, Encoding.UTF8);
        var relative = Path.GetRelativePath(vaultPath, absolutePath).Replace('\\', '/');
        var fileInfo = new FileInfo(absolutePath);

        var (fm, body) = SplitFrontmatter(raw);

        var id       = fm.TryGetValue("id",  out var fmId)  ? fmId?.ToString() ?? FileHash(relative) : FileHash(relative);
        var title    = fm.TryGetValue("title", out var fmT) ? fmT?.ToString() ?? ExtractTitle(body)   : ExtractTitle(body);
        var tags     = ParseTags(fm, body);
        var links    = ParseLinks(fm, body);
        var created  = ParseDate(fm, "created",  fileInfo.CreationTimeUtc);
        var modified = ParseDate(fm, "modified", fileInfo.LastWriteTimeUtc);

        return new Note
        {
            Id       = id,
            FilePath = relative,
            Title    = title,
            Content  = body,
            Tags     = tags,
            Links    = links,
            Created  = created,
            Modified = modified,
        };
    }

    public void InitVaultStructure(string vaultPath)
    {
        Directory.CreateDirectory(vaultPath);
        Directory.CreateDirectory(Path.Combine(vaultPath, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(vaultPath, ".memctl"));
        Directory.CreateDirectory(Path.Combine(vaultPath, "chats"));

        WriteIfAbsent(Path.Combine(vaultPath, ".obsidian", "app.json"),        "{}");
        WriteIfAbsent(Path.Combine(vaultPath, ".obsidian", "appearance.json"), "{}");
        WriteIfAbsent(Path.Combine(vaultPath, ".obsidian", "community-plugins.json"),
            """["dataview","calendar"]""");
        WriteIfAbsent(Path.Combine(vaultPath, ".obsidian", "core-plugins.json"),
            """{"daily-notes":true,"templates":true,"backlink":true,"outline":true,"word-count":true}""");
        WriteIfAbsent(Path.Combine(vaultPath, ".obsidian", "daily-notes.json"),
            """{"folder":"chats","format":"YYYY-MM-DD"}""");

        // Placeholder so @/vault/claude-memory/MEMORY.md import doesn't fail on first session
        Directory.CreateDirectory(Path.Combine(vaultPath, "claude-memory"));
        WriteIfAbsent(Path.Combine(vaultPath, "claude-memory", "MEMORY.md"),
            "---\ntype: user\n---\n\n");
    }

    private static void WriteIfAbsent(string path, string content)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, content, Encoding.UTF8);
    }

    public void WriteNote(Note note, string vaultPath, string? fileName = null)
    {
        fileName ??= SanitizeFileName(note.Title) + ".md";
        var path = Path.Combine(vaultPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {note.Id}");
        sb.AppendLine($"created: {note.Created:O}");
        sb.AppendLine($"modified: {note.Modified:O}");

        if (note.Tags.Length > 0)
        {
            sb.AppendLine("tags:");
            foreach (var tag in note.Tags)
                sb.AppendLine($"  - {tag}");
        }

        if (note.Links.Length > 0)
        {
            sb.AppendLine("links:");
            foreach (var link in note.Links)
                sb.AppendLine($"  - \"[[{link}]]\"");
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // Title heading if not already in content
        if (!note.Content.TrimStart().StartsWith('#'))
            sb.AppendLine($"# {note.Title}");
        sb.AppendLine();
        sb.Append(note.Content.Trim());

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public void UpdateFrontmatter(string absolutePath, string[] tags, string[] links)
    {
        var raw = File.ReadAllText(absolutePath, Encoding.UTF8);
        var match = FrontmatterPattern.Match(raw);

        var fm = new StringBuilder();
        fm.AppendLine("---");

        if (match.Success)
        {
            // Preserve existing frontmatter, replace tags/links
            var existing = match.Groups[1].Value;
            var lines = existing.Split('\n');
            var skipBlock = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();
                if (trimmed == "tags:" || trimmed == "links:")
                { skipBlock = true; continue; }
                if (skipBlock && (trimmed.StartsWith("  -") || trimmed.StartsWith("- ")))
                    continue;
                skipBlock = false;
                fm.AppendLine(trimmed);
            }
        }

        if (tags.Length > 0)
        {
            fm.AppendLine("tags:");
            foreach (var tag in tags) fm.AppendLine($"  - {tag}");
        }

        if (links.Length > 0)
        {
            fm.AppendLine("links:");
            foreach (var link in links) fm.AppendLine($"  - \"[[{link}]]\"");
        }

        fm.AppendLine("---");

        var body = match.Success ? raw[match.Length..] : "\n" + raw;
        File.WriteAllText(absolutePath, fm + body, Encoding.UTF8);
    }

    // --- helpers ---

    private static (Dictionary<string, object?> fm, string body) SplitFrontmatter(string raw)
    {
        var match = FrontmatterPattern.Match(raw);
        if (!match.Success) return ([], raw);

        try
        {
            var fm = FrontmatterParser.Parse(match.Groups[1].Value);
            return (fm, raw[match.Length..].TrimStart('\r', '\n'));
        }
        catch
        {
            return ([], raw);
        }
    }

    private static string ExtractTitle(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.StartsWith("# ")) return t[2..].Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
        }
        return "Untitled";
    }

    private static string[] ParseTags(Dictionary<string, object?> fm, string body)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (fm.TryGetValue("tags", out var fmTags))
            foreach (var t in CoerceList(fmTags))
                tags.Add(t.TrimStart('#'));

        foreach (Match m in TagPattern.Matches(body))
            tags.Add(m.Groups[1].Value);

        return [.. tags];
    }

    private static string[] ParseLinks(Dictionary<string, object?> fm, string body)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (fm.TryGetValue("links", out var fmLinks))
            foreach (var s in CoerceList(fmLinks))
            {
                var inner = WikiLinkPattern.Match(s);
                links.Add(inner.Success ? inner.Groups[1].Value.Trim() : s.Trim('[', ']'));
            }

        foreach (Match m in WikiLinkPattern.Matches(body))
            links.Add(m.Groups[1].Value.Trim());

        return [.. links];
    }

    private static IEnumerable<string> CoerceList(object? raw) => raw switch
    {
        string[] arr             => arr,
        IEnumerable<object> objs => objs.Select(o => o?.ToString() ?? "").Where(s => s.Length > 0),
        string s                 => [s],
        _                        => [],
    };

    private static DateTime ParseDate(Dictionary<string, object?> fm, string key, DateTime fallback)
    {
        if (fm.TryGetValue(key, out var v) && v != null &&
            DateTime.TryParse(v.ToString(), out var dt))
            return dt.ToUniversalTime();
        return fallback;
    }

    private static string FileHash(string relativePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return Convert.ToHexStringLower(bytes)[..16];
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in title)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString().Trim('_', ' ');
    }
}
