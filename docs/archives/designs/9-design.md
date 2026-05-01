# Task #9 — memctl fetch: Design

## Layer placement

`FetchOperator` sits in `Operators/` namespace. No vault access, no index ports. Standalone — constructs its own `HttpClient`.

## New NuGet package

`HtmlAgilityPack` — HTML parsing + DOM traversal. Sufficient for both stripping boilerplate and markdown conversion via recursive tree walk.

## FetchOperator

```csharp
public sealed class FetchOperator
{
    private const int    TimeoutSeconds = 10;
    private const int    MaxRedirects   = 5;
    private const string UserAgent      = "Mozilla/5.0 (compatible; memctl/1.0; +https://github.com/memctl)";

    public async Task<MemctlOutcome> ExecuteAsync(string source, bool rawHtml);
}
```

### Source detection
- `Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"` → HTTP fetch
- Otherwise → local file path

### HTTP fetch
- `HttpClientHandler { MaxAutomaticRedirections = MaxRedirects, AllowAutoRedirect = true }`
- `HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) }`
- `request.Headers.UserAgent.ParseAdd(UserAgent)`
- `OperationCanceledException` → timeout error
- Non-success status → HTTP error with status code

### HTML → Markdown conversion
Recursive tree walk on `HtmlNode`:
- Boilerplate strip pass first (mutates document)
- Strip tags: `nav, footer, header, aside, script, style, noscript, template`
- Strip by class keywords: `cookie, banner, ad, sidebar, popup, modal, newsletter`
- Strip by id keywords: `cookie, banner, sidebar, nav`
- Conversion walk:
  - `h1`–`h6` → `# ` through `###### `
  - `p` → text + `\n\n`
  - `ul` → children as `- item\n`
  - `ol` → children as `1. item\n`
  - `li` → content (handled by parent)
  - `pre` → ` ```\n...\n``` `
  - `code` (inside pre already handled) → `` `content` ``
  - `strong`, `b` → `**content**`
  - `em`, `i` → `*content*`
  - `a` → `[text](href)`
  - `img` → `![alt](src)`
  - `br` → `\n`
  - `table` → markdown table (thead/tbody/tr/th/td)
  - `#text` → decoded text
  - Everything else → recurse into children

## Program.cs integration

```csharp
// --- fetch ---
var fetchSourceArg = new Argument<string>("source", "URL (http/https) or local file path");
var fetchRawOpt    = new Option<bool>("--raw", "Output raw HTML without markdown conversion");
var fetchCmd       = new Command("fetch", "Fetch a URL or file and output as markdown");
fetchCmd.AddArgument(fetchSourceArg);
fetchCmd.AddOption(fetchRawOpt);
fetchCmd.SetHandler(async ctx =>
{
    var source  = ctx.ParseResult.GetValueForArgument(fetchSourceArg);
    var raw     = ctx.ParseResult.GetValueForOption(fetchRawOpt);
    var outcome = await new FetchOperator().ExecuteAsync(source, raw);
    if (outcome.IsSuccess)
        Console.Write(outcome.Data?.ToString() ?? string.Empty);
    else
    {
        ResultPrinter.Print(outcome);
        ctx.ExitCode = 1;
    }
});
root.AddCommand(fetchCmd);
```

Inserted before `// --- capture ---`.

## Error JSON shape

Uses `ResultPrinter.Print(outcome)` → consistent with all other commands:
```json
{"success":false,"action":"fetch","message":"..."}
```
