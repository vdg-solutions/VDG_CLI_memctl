using System.Net.Http.Headers;
using System.Text;
using HtmlAgilityPack;
using Memctl.CoreAbstractions.Entities;

namespace Memctl.Operators;

public sealed class FetchOperator
{
    private const int    TimeoutSeconds = 10;
    private const int    MaxRedirects   = 5;
    private const string UserAgent      = "Mozilla/5.0 (compatible; memctl/1.0; +https://github.com/memctl)";
    private const string FetchAction    = "fetch";

    // Tags stripped wholesale before markdown conversion
    private static readonly HashSet<string> BoilerplateTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "nav", "footer", "header", "aside", "script", "style", "noscript", "template"
    };

    // Substrings checked against class/id attributes
    private static readonly string[] BoilerplateClassKeywords = ["cookie", "banner", "ad", "sidebar", "popup", "modal", "newsletter"];
    private static readonly string[] BoilerplateIdKeywords    = ["cookie", "banner", "sidebar", "nav"];

    public async Task<MemctlOutcome> ExecuteAsync(string source, bool rawHtml)
    {
        string html;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            var fetchResult = await FetchUrlAsync(uri);
            if (fetchResult.outcome is not null) return fetchResult.outcome;
            html = fetchResult.html!;
        }
        else
        {
            var fileResult = ReadFile(source);
            if (fileResult.outcome is not null) return fileResult.outcome;
            html = fileResult.content!;

            // Non-HTML local files pass through as-is
            if (!IsHtmlContent(source, html))
                return MemctlOutcome.Ok(FetchAction, "ok", html);
        }

        if (rawHtml)
            return MemctlOutcome.Ok(FetchAction, "ok", html);

        var markdown = ConvertToMarkdown(html);
        return MemctlOutcome.Ok(FetchAction, "ok", markdown);
    }

    private static async Task<(MemctlOutcome? outcome, string? html)> FetchUrlAsync(Uri uri)
    {
        var handler = new HttpClientHandler
        {
            MaxAutomaticRedirections = MaxRedirects,
            AllowAutoRedirect        = true,
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*");

        try
        {
            var response = await client.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                return (MemctlOutcome.Fail(FetchAction, $"HTTP {code}"), null);
            }

            var content = await response.Content.ReadAsStringAsync();
            return (null, content);
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout exceeded
            return (MemctlOutcome.Fail(FetchAction, "Request timed out after 10 seconds"), null);
        }
        catch (HttpRequestException ex)
        {
            // Network-level error (DNS, connection refused, etc.)
            return (MemctlOutcome.Fail(FetchAction, ex.Message), null);
        }
    }

    private static (MemctlOutcome? outcome, string? content) ReadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return (MemctlOutcome.Fail(FetchAction, $"File not found: {path}"), null);

        try
        {
            return (null, File.ReadAllText(fullPath));
        }
        catch (IOException ex)
        {
            // Permission denied or locked file
            return (MemctlOutcome.Fail(FetchAction, ex.Message), null);
        }
    }

    private static bool IsHtmlContent(string path, string content)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".md" or ".txt") return false;
        if (ext is ".html" or ".htm") return true;
        // Sniff: HTML documents start with doctype or html tag
        var trimmed = content.TrimStart();
        return trimmed.StartsWith("<!") || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    // ── HTML → Markdown ──────────────────────────────────────────────

    private static string ConvertToMarkdown(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        StripBoilerplate(doc.DocumentNode);

        var sb = new StringBuilder();
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        WalkNode(body, sb, listDepth: 0, orderedList: false, insidePre: false);

        // Collapse excessive blank lines
        var result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n");
        return result.Trim();
    }

    private static void StripBoilerplate(HtmlNode root)
    {
        // Collect nodes to remove (don't modify during enumeration)
        var toRemove = root.Descendants()
            .Where(n => ShouldStrip(n))
            .ToList();

        foreach (var node in toRemove)
            node.Remove();
    }

    private static bool ShouldStrip(HtmlNode node)
    {
        if (node.NodeType != HtmlNodeType.Element) return false;

        if (BoilerplateTags.Contains(node.Name)) return true;

        var cls = node.GetAttributeValue("class", "");
        if (BoilerplateClassKeywords.Any(k => cls.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        var id = node.GetAttributeValue("id", "");
        if (BoilerplateIdKeywords.Any(k => id.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static void WalkNode(HtmlNode node, StringBuilder sb, int listDepth, bool orderedList, bool insidePre)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                var text = HtmlEntity.DeEntitize(node.InnerText);
                if (insidePre)
                    sb.Append(text);
                else
                {
                    // Collapse whitespace for inline text
                    var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                    sb.Append(normalized);
                }
                return;

            case HtmlNodeType.Comment:
                return;
        }

        // Element nodes
        var tag = node.Name.ToLowerInvariant();

        switch (tag)
        {
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                var level = int.Parse(tag[1..]);
                sb.Append('\n');
                sb.Append(new string('#', level));
                sb.Append(' ');
                sb.Append(HtmlEntity.DeEntitize(node.InnerText).Trim());
                sb.Append("\n\n");
                return;

            case "p":
                sb.Append('\n');
                WalkChildren(node, sb, listDepth, orderedList, insidePre);
                sb.Append("\n\n");
                return;

            case "br":
                sb.Append('\n');
                return;

            case "hr":
                sb.Append("\n---\n\n");
                return;

            case "ul":
                sb.Append('\n');
                foreach (var child in node.ChildNodes.Where(c => c.Name == "li"))
                {
                    sb.Append(new string(' ', listDepth * 2));
                    sb.Append("- ");
                    WalkChildren(child, sb, listDepth + 1, orderedList: false, insidePre);
                    sb.Append('\n');
                }
                sb.Append('\n');
                return;

            case "ol":
                sb.Append('\n');
                var idx = 1;
                foreach (var child in node.ChildNodes.Where(c => c.Name == "li"))
                {
                    sb.Append(new string(' ', listDepth * 2));
                    sb.Append($"{idx++}. ");
                    WalkChildren(child, sb, listDepth + 1, orderedList: true, insidePre);
                    sb.Append('\n');
                }
                sb.Append('\n');
                return;

            case "li":
                // Handled inside ul/ol above; fall through to children if reached directly
                WalkChildren(node, sb, listDepth, orderedList, insidePre);
                return;

            case "pre":
                sb.Append("\n```\n");
                // Walk pre content preserving whitespace
                foreach (var child in node.ChildNodes)
                    WalkNode(child, sb, listDepth, orderedList, insidePre: true);
                sb.Append("\n```\n\n");
                return;

            case "code":
                if (insidePre)
                {
                    WalkChildren(node, sb, listDepth, orderedList, insidePre: true);
                }
                else
                {
                    sb.Append('`');
                    sb.Append(HtmlEntity.DeEntitize(node.InnerText));
                    sb.Append('`');
                }
                return;

            case "strong": case "b":
                sb.Append("**");
                WalkChildren(node, sb, listDepth, orderedList, insidePre);
                sb.Append("**");
                return;

            case "em": case "i":
                sb.Append('*');
                WalkChildren(node, sb, listDepth, orderedList, insidePre);
                sb.Append('*');
                return;

            case "a":
                var href = node.GetAttributeValue("href", "");
                var linkText = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (string.IsNullOrEmpty(href))
                    sb.Append(linkText);
                else
                    sb.Append($"[{linkText}]({href})");
                return;

            case "img":
                var src = node.GetAttributeValue("src", "");
                var alt = node.GetAttributeValue("alt", "");
                sb.Append($"![{alt}]({src})");
                return;

            case "table":
                ConvertTable(node, sb);
                return;

            case "blockquote":
                sb.Append('\n');
                foreach (var line in HtmlEntity.DeEntitize(node.InnerText).Trim().Split('\n'))
                    sb.Append($"> {line.Trim()}\n");
                sb.Append('\n');
                return;

            default:
                WalkChildren(node, sb, listDepth, orderedList, insidePre);
                return;
        }
    }

    private static void WalkChildren(HtmlNode node, StringBuilder sb, int listDepth, bool orderedList, bool insidePre)
    {
        foreach (var child in node.ChildNodes)
            WalkNode(child, sb, listDepth, orderedList, insidePre);
    }

    private static void ConvertTable(HtmlNode table, StringBuilder sb)
    {
        var rows = table.Descendants("tr").ToList();
        if (rows.Count == 0) return;

        sb.Append('\n');
        var headerRow = rows[0];
        var headers   = headerRow.ChildNodes
            .Where(n => n.Name is "th" or "td")
            .Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
            .ToList();

        if (headers.Count == 0) return;

        sb.AppendLine("| " + string.Join(" | ", headers) + " |");
        sb.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");

        foreach (var row in rows.Skip(1))
        {
            var cells = row.ChildNodes
                .Where(n => n.Name is "th" or "td")
                .Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim());
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        sb.Append('\n');
    }
}
