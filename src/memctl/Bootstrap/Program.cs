using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.CommandLine.Invocation;
using Memctl.Bootstrap;
using Memctl.Boundary.Options;
using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Config;
using Memctl.Implementations.Embedding;
using Memctl.Implementations.Index;
using Memctl.Implementations.Llm;
using Memctl.Implementations.Vault;
using Memctl.Operators;

// --- shared services ---
var vaultReader = new ObsidianVaultReader();
var noteIndex   = new SqliteNoteIndex();

// --- global options ---
var vaultOpt     = new Option<string> ("--vault",      "Vault directory path");
var limitOpt     = new Option<int>    ("--limit",      () => 10, "Max results");
var llmUrlOpt    = new Option<string?>("--llm-url",    "OpenAI-compatible API base URL");
var llmModelOpt  = new Option<string?>("--llm-model",  "LLM model name");
var llmKeyOpt    = new Option<string?>("--llm-key",    "API key");
var modelDirOpt  = new Option<string?>("--model-dir",  "Override embedding model directory");

var root = new RootCommand("Obsidian-compatible personal memory vault CLI");
root.AddGlobalOption(vaultOpt);
root.AddGlobalOption(limitOpt);
root.AddGlobalOption(llmUrlOpt);
root.AddGlobalOption(llmModelOpt);
root.AddGlobalOption(llmKeyOpt);
root.AddGlobalOption(modelDirOpt);

GlobalOptions G(InvocationContext ctx) => new()
{
    Vault    = ctx.ParseResult.GetValueForOption(vaultOpt),
    Limit    = ctx.ParseResult.GetValueForOption(limitOpt),
    LlmUrl   = ctx.ParseResult.GetValueForOption(llmUrlOpt),
    LlmModel = ctx.ParseResult.GetValueForOption(llmModelOpt),
    LlmKey   = ctx.ParseResult.GetValueForOption(llmKeyOpt),
    ModelDir = ctx.ParseResult.GetValueForOption(modelDirOpt),
};

GemmaEmbeddingEngine? embeddingEngine = null;
async Task<GemmaEmbeddingEngine> GetEmbedding(GlobalOptions g)
{
    // Re-create if model dir changed between calls (e.g. different commands in same session)
    var resolvedDir = MemctlConfig.ResolveModelDir(g.ModelDir);
    if (embeddingEngine is not null && embeddingEngine.ModelName != Path.GetFileName(resolvedDir.TrimEnd(Path.DirectorySeparatorChar, '/')))
    {
        embeddingEngine.Dispose();
        embeddingEngine = null;
    }
    return embeddingEngine ??= await GemmaEmbeddingEngine.CreateAsync(resolvedDir);
}

OpenAiLlmClient? LlmClient(GlobalOptions g) =>
    g.LlmUrl is not null && g.LlmModel is not null
        ? new OpenAiLlmClient(g.LlmUrl, g.LlmModel, g.LlmKey)
        : null;

// Vault guard — auto-detects vault from cwd if --vault not provided
string? RequireVault(GlobalOptions g, InvocationContext ctx)
{
    var vault = VaultLocator.FindVault(g.Vault);
    if (vault is not null) return vault;
    ResultPrinter.Print(MemctlOutcome.Fail("error",
        "No vault found. Create one with 'memctl init --vault <path>' or run from a directory containing a vault."));
    ctx.ExitCode = 1;
    return null;
}

// MCP mode: auto-init vault at ./vault if none found — zero-config for global MCP setup
string RequireVaultOrInit(GlobalOptions g)
{
    var vault = VaultLocator.FindVault(g.Vault);
    if (vault is not null) return vault;
    var autoPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "vault"));
    vaultReader.InitVaultStructure(autoPath);
    return autoPath;
}

// init requires explicit path — can't auto-detect a vault that doesn't exist yet
string? RequireVaultExplicit(GlobalOptions g, InvocationContext ctx)
{
    if (g.Vault is not null) return g.Vault;
    ResultPrinter.Print(MemctlOutcome.Fail("error", "--vault is required for this command"));
    ctx.ExitCode = 1;
    return null;
}

// --- init ---
var initCmd = new Command("init", "Create a new Obsidian-compatible vault");
initCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVaultExplicit(g, ctx) is not { } vault) return;
    vaultReader.InitVaultStructure(vault);
    ResultPrinter.Print(MemctlOutcome.Ok("init", $"Vault initialized at {vault}", new { vault }));
});
root.AddCommand(initCmd);

// --- ingest ---
var ingestCmd = new Command("ingest", "Index all notes in vault");
ingestCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb = await GetEmbedding(g);
    var op  = new IngestOperator(vaultReader, noteIndex, emb);
    ResultPrinter.Print(op.Execute(vault));
});
root.AddCommand(ingestCmd);

// --- add ---
var addTextArg   = new Argument<string>("text", "Note content");
var addTitleOpt  = new Option<string?>("--title", "Note title (auto-extracted if omitted)");
var addTagsOpt   = new Option<string?>("--tags",  "Comma-separated tags");
var addFileOpt   = new Option<string?>("--file",  "Output filename (e.g. notes/crypto.md)");
var addCmd = new Command("add", "Add a new note to vault");
addCmd.AddArgument(addTextArg);
addCmd.AddOption(addTitleOpt);
addCmd.AddOption(addTagsOpt);
addCmd.AddOption(addFileOpt);
addCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var pr   = ctx.ParseResult;
    var emb  = await GetEmbedding(g);
    var op   = new AddOperator(vaultReader, noteIndex, emb);
    var tags = pr.GetValueForOption(addTagsOpt)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var outcome = await op.ExecuteAsync(
        vault,
        pr.GetValueForArgument(addTextArg),
        pr.GetValueForOption(addTitleOpt),
        tags,
        pr.GetValueForOption(addFileOpt),
        LlmClient(g));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(addCmd);

// --- add-turn ---
var atChatOpt      = new Option<long>  ("--chat-id",   "Telegram chat ID")   { IsRequired = true };
var atUserOpt      = new Option<long>  ("--user-id",   "Telegram user ID");
var atFromOpt      = new Option<string>("--from",      "Display name")        { IsRequired = true };
var atRoleOpt      = new Option<string>("--role",      "user | assistant")    { IsRequired = true };
var atTextOpt      = new Option<string>("--text",      "Turn content")        { IsRequired = true };
var atTsOpt        = new Option<string?>("--timestamp","ISO 8601 timestamp (default: now)");
var atWriteOnlyOpt = new Option<bool>  ("--write-only","Write file only, skip indexing");
var atCmd = new Command("add-turn", "Append a conversation turn and update vault index");
atCmd.AddOption(atChatOpt); atCmd.AddOption(atUserOpt); atCmd.AddOption(atFromOpt);
atCmd.AddOption(atRoleOpt); atCmd.AddOption(atTextOpt); atCmd.AddOption(atTsOpt);
atCmd.AddOption(atWriteOnlyOpt);
atCmd.SetHandler(async ctx =>
{
    var g         = G(ctx);
    var pr        = ctx.ParseResult;
    if (RequireVault(g, ctx) is not { } vault) return;
    var writeOnly = pr.GetValueForOption(atWriteOnlyOpt);
    var emb       = writeOnly ? null : await GetEmbedding(g);
    var op        = new AddTurnOperator(vaultReader, noteIndex, emb);
    DateTime? ts  = null;
    var tsRaw     = pr.GetValueForOption(atTsOpt);
    if (tsRaw != null) DateTime.TryParse(tsRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed);
    var outcome   = op.Execute(
        vault,
        pr.GetValueForOption(atChatOpt),
        pr.GetValueForOption(atUserOpt),
        pr.GetValueForOption(atFromOpt)!,
        pr.GetValueForOption(atRoleOpt)!,
        pr.GetValueForOption(atTextOpt)!,
        ts,
        writeOnly);
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(atCmd);

// --- get ---
var getIdArg = new Argument<string>("id", "Note ID or file path");
var getCmd   = new Command("get", "Get full note content by ID or path");
getCmd.AddArgument(getIdArg);
getCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new GetOperator(vaultReader, noteIndex);
    var outcome = op.Execute(vault, ctx.ParseResult.GetValueForArgument(getIdArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 2;
});
root.AddCommand(getCmd);

// --- list ---
var listTagOpt            = new Option<string?>("--tag",              "Filter by tag");
var listIncludeArchiveOpt = new Option<bool>   ("--include-archived", "Include archived notes");
var listCmd               = new Command("list", "List notes");
listCmd.AddOption(listTagOpt);
listCmd.AddOption(listIncludeArchiveOpt);
listCmd.SetHandler(ctx =>
{
    var g               = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var includeArchived = ctx.ParseResult.GetValueForOption(listIncludeArchiveOpt);
    var op              = new ListOperator(vaultReader, noteIndex);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForOption(listTagOpt), g.Limit, includeArchived));
});
root.AddCommand(listCmd);

// --- search (hybrid RRF) ---
var searchQueryArg  = new Argument<string>("query");
var searchFolderOpt = new Option<string?>("--folder", "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)");
var searchCmd       = new Command("search", "Hybrid search (BM25 + semantic, RRF fusion)");
searchCmd.AddArgument(searchQueryArg);
searchCmd.AddOption(searchFolderOpt);
searchCmd.SetHandler(async ctx =>
{
    var g      = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb    = await GetEmbedding(g);
    var op     = new SearchOperator(vaultReader, noteIndex, emb);
    var folder = ctx.ParseResult.GetValueForOption(searchFolderOpt);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(searchQueryArg), g.Limit, folder));
});
root.AddCommand(searchCmd);

// --- search-semantic ---
var semQueryArg  = new Argument<string>("query");
var semScopeOpt  = new Option<string?>("--scope",  "Comma-separated note IDs to restrict search");
var semFolderOpt = new Option<string?>("--folder", "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)");
var semCmd       = new Command("search-semantic", "Semantic vector search");
semCmd.AddArgument(semQueryArg);
semCmd.AddOption(semScopeOpt);
semCmd.AddOption(semFolderOpt);
semCmd.SetHandler(async ctx =>
{
    var g      = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb    = await GetEmbedding(g);
    var op     = new SearchSemanticOperator(vaultReader, noteIndex, emb);
    var scope  = ctx.ParseResult.GetValueForOption(semScopeOpt)
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var folder = ctx.ParseResult.GetValueForOption(semFolderOpt);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(semQueryArg), g.Limit, scope, folder));
});
root.AddCommand(semCmd);

// --- search-text ---
var stQueryArg  = new Argument<string>("query");
var stFolderOpt = new Option<string?>("--folder", "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)");
var stCmd       = new Command("search-text", "Full-text BM25 search");
stCmd.AddArgument(stQueryArg);
stCmd.AddOption(stFolderOpt);
stCmd.SetHandler(ctx =>
{
    var g      = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op     = new SearchTextOperator(vaultReader, noteIndex);
    var folder = ctx.ParseResult.GetValueForOption(stFolderOpt);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(stQueryArg), g.Limit, folder));
});
root.AddCommand(stCmd);

// --- search-tags ---
var sTagsArg    = new Argument<string>("tags", "Comma-separated tag list");
var sTagsMatch  = new Option<string>("--match", () => "any", "Match mode: any|all");
var sTagsCmd    = new Command("search-tags", "Search by tags");
sTagsCmd.AddArgument(sTagsArg);
sTagsCmd.AddOption(sTagsMatch);
sTagsCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op   = new SearchTagsOperator(vaultReader, noteIndex);
    var tags = ctx.ParseResult.GetValueForArgument(sTagsArg)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var matchAll = ctx.ParseResult.GetValueForOption(sTagsMatch) == "all";
    ResultPrinter.Print(op.Execute(vault, tags, matchAll, g.Limit));
});
root.AddCommand(sTagsCmd);

// --- search-links ---
var slIdArg    = new Argument<string>("id", "Source note ID or file path");
var slDepthOpt = new Option<int>("--depth", () => 1, "Traversal depth");
var slCmd      = new Command("search-links", "Traverse wikilink graph from a note");
slCmd.AddArgument(slIdArg);
slCmd.AddOption(slDepthOpt);
slCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new SearchLinksOperator(vaultReader, noteIndex);
    ResultPrinter.Print(op.Execute(vault,
        ctx.ParseResult.GetValueForArgument(slIdArg),
        ctx.ParseResult.GetValueForOption(slDepthOpt)));
});
root.AddCommand(slCmd);

// --- search-date ---
var sdFromOpt = new Option<string?>("--from", "Start date (ISO 8601)");
var sdToOpt   = new Option<string?>("--to",   "End date (ISO 8601)");
var sdCmd     = new Command("search-date", "Search notes by creation date range");
sdCmd.AddOption(sdFromOpt);
sdCmd.AddOption(sdToOpt);
sdCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new SearchDateOperator(vaultReader, noteIndex);
    var pr = ctx.ParseResult;
    DateTime? from = pr.GetValueForOption(sdFromOpt) is { } fs ? DateTime.Parse(fs).ToUniversalTime() : null;
    DateTime? to   = pr.GetValueForOption(sdToOpt)   is { } ts ? DateTime.Parse(ts).ToUniversalTime() : null;
    ResultPrinter.Print(op.Execute(vault, from, to, g.Limit));
});
root.AddCommand(sdCmd);

// --- grep ---
var grepPatternArg = new Argument<string>("pattern");
var grepRegexOpt   = new Option<bool>("--regex", "Use regex pattern");
var grepCmd        = new Command("grep", "Search raw markdown files by pattern");
grepCmd.AddArgument(grepPatternArg);
grepCmd.AddOption(grepRegexOpt);
grepCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new GrepOperator();
    ResultPrinter.Print(op.Execute(vault,
        ctx.ParseResult.GetValueForArgument(grepPatternArg),
        ctx.ParseResult.GetValueForOption(grepRegexOpt),
        g.Limit));
});
root.AddCommand(grepCmd);

// --- tags ---
var tagsCmd = new Command("tags", "List all tags with note counts");
tagsCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    ResultPrinter.Print(new TagsOperator(vaultReader, noteIndex).Execute(vault));
});
root.AddCommand(tagsCmd);

// --- stats ---
var statsCmd = new Command("stats", "Vault statistics");
statsCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    ResultPrinter.Print(new StatsOperator(vaultReader, noteIndex).Execute(vault));
});
root.AddCommand(statsCmd);

// --- organize ---
var orgSinceOpt = new Option<string?>("--since", "Only organize notes modified after this date");
var orgCmd      = new Command("organize", "Auto-tag and auto-link notes via LLM");
orgCmd.AddOption(orgSinceOpt);
orgCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    if (g.LlmUrl is null || g.LlmModel is null)
    {
        ResultPrinter.Print(MemctlOutcome.Fail("organize", "--llm-url and --llm-model are required"));
        ctx.ExitCode = 1;
        return;
    }
    var llm = LlmClient(g)!;
    var op  = new OrganizeOperator(vaultReader, noteIndex, llm);
    DateTime? since = ctx.ParseResult.GetValueForOption(orgSinceOpt) is { } s
        ? DateTime.Parse(s).ToUniversalTime() : null;
    var outcome = await op.ExecuteAsync(vault, since);
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(orgCmd);

// --- status ---
var statusCmd = new Command("status", "Check model and vault readiness");
statusCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    ResultPrinter.Print(new StatusOperator().Execute(g.Vault ?? ""));
});
root.AddCommand(statusCmd);

// --- model ---
var modelCmd = new Command("model", "Manage embedding model");
var modelDownloadCmd = new Command("download", "Download EmbeddingGemma ONNX model (~310 MB, one-time)");
modelDownloadCmd.SetHandler(async ctx =>
{
    var outcome = await new ModelDownloadOperator().ExecuteAsync();
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 9;
});
modelCmd.AddCommand(modelDownloadCmd);

var modelListCmd = new Command("list", "List downloaded embedding models");
modelListCmd.SetHandler(_ => ResultPrinter.Print(new ModelListOperator().Execute()));
modelCmd.AddCommand(modelListCmd);

var modelUseNameArg = new Argument<string>("name", "Model directory name (e.g. embeddinggemma-300m)");
var modelUseCmd     = new Command("use", "Set default embedding model");
modelUseCmd.AddArgument(modelUseNameArg);
modelUseCmd.SetHandler(ctx =>
{
    var outcome = new ModelUseOperator().Execute(ctx.ParseResult.GetValueForArgument(modelUseNameArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
modelCmd.AddCommand(modelUseCmd);

root.AddCommand(modelCmd);

// --- mcp ---
var mcpCmd = new Command("mcp", "Start a stdio MCP server exposing the vault as an AI memory layer");
mcpCmd.SetHandler(async ctx =>
{
    var g     = G(ctx);
    var vault = RequireVaultOrInit(g);
    var op    = new McpServerOperator(vaultReader, noteIndex, vault, g.ModelDir);
    await op.RunAsync(ctx.GetCancellationToken());
});
root.AddCommand(mcpCmd);

// --- weight ---
var weightIdArg  = new Argument<string>("id", "Note ID or file path");
var weightValArg = new Argument<string>("value", "Weight value (0.0–2.0)");
var weightCmd    = new Command("weight", "Set importance weight for a note (0.0–2.0)");
weightCmd.AddArgument(weightIdArg);
weightCmd.AddArgument(weightValArg);
weightCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var pr      = ctx.ParseResult;
    var outcome = new WeightOperator(vaultReader, noteIndex).Execute(
        vault,
        pr.GetValueForArgument(weightIdArg),
        pr.GetValueForArgument(weightValArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(weightCmd);

// --- decay ---
var decayDaysOpt   = new Option<int>   ("--days",         "Age threshold in days") { IsRequired = true };
var decayFactorOpt = new Option<double>("--decay-factor", () => 0.9, "Multiplicative decay factor (default 0.9)");
var decayDryRunOpt = new Option<bool>  ("--dry-run",      "Simulate — no weight changes");
var decayCmd       = new Command("decay", "Apply temporal decay to stale vault notes");
decayCmd.AddOption(decayDaysOpt);
decayCmd.AddOption(decayFactorOpt);
decayCmd.AddOption(decayDryRunOpt);
decayCmd.SetHandler(ctx =>
{
    var g      = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var pr     = ctx.ParseResult;
    var days   = pr.GetValueForOption(decayDaysOpt);
    var factor = (float)pr.GetValueForOption(decayFactorOpt);
    var dryRun = pr.GetValueForOption(decayDryRunOpt);
    var outcome = new DecayOperator(vaultReader, noteIndex).Execute(vault, days, factor, dryRun);
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
root.AddCommand(decayCmd);

// --- identity ---
var identityCmd = new Command("identity", "Manage vault identity note (Layer 0 context)");

var idSetIdArg = new Argument<string>("id", "Note ID or file path");
var idSetCmd   = new Command("set", "Designate a note as the vault identity note");
idSetCmd.AddArgument(idSetIdArg);
idSetCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var outcome = new IdentityOperator(vaultReader, noteIndex).ExecuteSet(
        vault,
        ctx.ParseResult.GetValueForArgument(idSetIdArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
identityCmd.AddCommand(idSetCmd);

var idGetCmd = new Command("get", "Retrieve current identity note");
idGetCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    ResultPrinter.Print(new IdentityOperator(vaultReader, noteIndex).ExecuteGet(vault));
});
identityCmd.AddCommand(idGetCmd);

root.AddCommand(identityCmd);

// --- context-inject ---
var ciDryRunOpt      = new Option<bool>("--dry-run", "Same as live run (read-only command)");
var contextInjectCmd = new Command("context-inject", "Inject relevant vault context for UserPromptSubmit hook");
contextInjectCmd.AddOption(ciDryRunOpt);
contextInjectCmd.SetHandler(async ctx =>
{
    try
    {
        var g = G(ctx);

        if (!Console.IsInputRedirected) { ctx.ExitCode = 0; return; }

        string stdinText;
        try   { stdinText = await Console.In.ReadToEndAsync(); }
        catch { ctx.ExitCode = 0; return; }

        if (string.IsNullOrWhiteSpace(stdinText)) { ctx.ExitCode = 0; return; }

        stdinText = ExtractPromptText(stdinText);
        if (string.IsNullOrWhiteSpace(stdinText)) { ctx.ExitCode = 0; return; }

        var vaultPath = VaultLocator.FindVault(g.Vault);
        if (vaultPath is null) { ctx.ExitCode = 0; return; }

        var context = new ContextInjectOperator(vaultReader, noteIndex)
            .Execute(vaultPath, stdinText);

        if (context is not null)
            Console.Write(context);
    }
    catch { /* NFR-002: never crash hook */ }
    ctx.ExitCode = 0;
});
root.AddCommand(contextInjectCmd);

// --- lint ---
var lintSemanticOpt         = new Option<bool>   ("--semantic",         "Enable Tier 2 semantic lint via LLM");
var lintSelfOpt             = new Option<bool>   ("--self",             "Print self-analysis prompt to stdout (no LLM call)");
var lintFormatOpt           = new Option<string> ("--format",           () => "json", "Output format: json | md");
var lintSaveOpt             = new Option<bool>   ("--save",             "Persist structural report as vault note");
var lintDryRunOpt           = new Option<bool>   ("--dry-run",          "Simulate — no writes");
var lintUpdateTsOpt         = new Option<bool>   ("--update-timestamp", "Record semantic lint timestamp only; skip lint run");
var lintCmd = new Command("lint", "Two-tier vault health check (structural + optional semantic)");
lintCmd.AddOption(lintSemanticOpt);
lintCmd.AddOption(lintSelfOpt);
lintCmd.AddOption(lintFormatOpt);
lintCmd.AddOption(lintSaveOpt);
lintCmd.AddOption(lintDryRunOpt);
lintCmd.AddOption(lintUpdateTsOpt);
lintCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var pr   = ctx.ParseResult;
    var opts = new LintOptions(
        Semantic:            pr.GetValueForOption(lintSemanticOpt),
        Self:                pr.GetValueForOption(lintSelfOpt),
        DryRun:              pr.GetValueForOption(lintDryRunOpt),
        Save:                pr.GetValueForOption(lintSaveOpt),
        UpdateTimestampOnly: pr.GetValueForOption(lintUpdateTsOpt),
        Format:              pr.GetValueForOption(lintFormatOpt) ?? "json",
        LlmUrl:              g.LlmUrl,
        LlmModel:            g.LlmModel,
        LlmKey:              g.LlmKey);
    var op = new LintOperator(vaultReader, noteIndex);
    var (outcome, exitCode) = await op.ExecuteAsync(vault, opts);
    if (opts.Format != "md" || !outcome.Success)
        ResultPrinter.Print(outcome);
    ctx.ExitCode = exitCode;
});
root.AddCommand(lintCmd);

// --- capture ---
var capRoleOpt    = new Option<string?>("--role",       "Turn role (user | assistant) — direct mode");
var capTextOpt    = new Option<string?>("--text",       "Turn content — direct mode");
var capSessionOpt = new Option<string?>("--session-id", "Session ID override");
var capDryRunOpt  = new Option<bool>   ("--dry-run",    "Print what would be saved; no disk write");
var captureCmd     = new Command("capture", "Auto-capture conversation turns (Hook Protocol v1)");
var captureJsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
captureCmd.AddOption(capRoleOpt);
captureCmd.AddOption(capTextOpt);
captureCmd.AddOption(capSessionOpt);
captureCmd.AddOption(capDryRunOpt);
captureCmd.SetHandler(async ctx =>
{
    try
    {
        var pr     = ctx.ParseResult;
        var role   = pr.GetValueForOption(capRoleOpt);
        var text   = pr.GetValueForOption(capTextOpt);
        var sesId  = pr.GetValueForOption(capSessionOpt);
        var dryRun = pr.GetValueForOption(capDryRunOpt);
        var g      = G(ctx);

        // Direct mode: --role / --text
        if (role is not null || text is not null)
        {
            if (role is null || text is null)
            {
                Console.WriteLine("""{"success":false,"action":"capture","message":"--role and --text are both required for direct mode"}""");
                ctx.ExitCode = 1;
                return;
            }
            var vault = VaultLocator.FindVault(g.Vault);
            if (vault is null) { ctx.ExitCode = 0; return; }
            GemmaEmbeddingEngine? emb = null;
            try   { emb = await GetEmbedding(g); }
            catch { /* embedding optional */ }
            var turns  = new (string Role, string Content)[] { (role, text) };
            var op     = new CaptureOperator(vaultReader, noteIndex, emb);
            var result = op.Execute(vault, sesId, turns, dryRun);
            if (dryRun) ResultPrinter.Print(result);
            ctx.ExitCode = 0;
            return;
        }

        // Hook mode: read stdin
        if (!Console.IsInputRedirected) { ctx.ExitCode = 0; return; }

        string stdinText;
        try   { stdinText = await Console.In.ReadToEndAsync(); }
        catch { ctx.ExitCode = 0; return; }

        CapturePayload? payload;
        try   { payload = JsonSerializer.Deserialize<CapturePayload>(stdinText, captureJsonOpts); }
        catch { ctx.ExitCode = 0; return; }

        if (payload is null) { ctx.ExitCode = 0; return; }
        if (payload.Transcript is null or { Length: 0 }) { ctx.ExitCode = 0; return; }

        var cwd       = payload.Cwd ?? Directory.GetCurrentDirectory();
        var vaultPath = VaultLocator.FindVaultFrom(cwd);
        if (vaultPath is null) { ctx.ExitCode = 0; return; }

        GemmaEmbeddingEngine? emb2 = null;
        try   { emb2 = await GetEmbedding(g); }
        catch { /* embedding optional */ }

        var turns2 = payload.Transcript.Select(t => (t.Role, t.Content)).ToArray();
        var op2    = new CaptureOperator(vaultReader, noteIndex, emb2);
        var res    = op2.Execute(vaultPath, payload.SessionId ?? sesId, turns2, dryRun);
        if (dryRun) ResultPrinter.Print(res);
    }
    catch { /* NFR-002: never crash hook */ }
    ctx.ExitCode = 0;
});
root.AddCommand(captureCmd);

return await root.InvokeAsync(args);

// Extract plain-text prompt from stdin — plain text or first string field from JSON
static string ExtractPromptText(string raw)
{
    var trimmed = raw.TrimStart();
    if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[')) return raw;
    try
    {
        using var doc = JsonDocument.Parse(trimmed);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString() ?? "";
            return ""; // JSON with no string fields
        }
    }
    catch { /* not valid JSON — use raw */ }
    return raw;
}

// Hook Protocol v1 payload DTOs
internal sealed record CapturePayload(
    [property: JsonPropertyName("session_id")] string?           SessionId,
    [property: JsonPropertyName("cwd")]        string?           Cwd,
    [property: JsonPropertyName("transcript")] TranscriptTurn[]? Transcript);

internal sealed record TranscriptTurn(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);


