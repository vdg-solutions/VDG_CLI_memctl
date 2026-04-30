using System.Text.Json;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Config;
using Memctl.Implementations.Embedding;
using Memctl.Operators.Mapping;

namespace Memctl.Operators;

public sealed class McpServerOperator(
    IVaultReader vaultReader,
    INoteIndex   index,
    string       vaultPath,
    string?      modelDir)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private GemmaEmbeddingEngine? _embedding;

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // initialize index once at startup
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        while (!ct.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement msg;
            try { msg = JsonSerializer.Deserialize<JsonElement>(line); }
            catch { continue; }

            var response = await RouteAsync(msg, ct).ConfigureAwait(false);
            if (response is null) continue;

            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOpts)).ConfigureAwait(false);
            await Console.Out.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    // --- routing ---

    private async Task<object?> RouteAsync(JsonElement msg, CancellationToken ct)
    {
        // notifications have no id — no response
        if (!msg.TryGetProperty("id", out var idEl)) return null;

        var id     = ParseId(idEl);
        var method = msg.TryGetProperty("method", out var m) ? m.GetString() : null;
        var prms   = msg.TryGetProperty("params",  out var p) ? p : default;

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolsCallAsync(id, prms, ct),
            "ping"       => new { jsonrpc = "2.0", id, result = new { } },
            _            => RpcError(id, -32601, "Method not found"),
        };
    }

    // --- MCP protocol ---

    private object HandleInitialize(object id) => new
    {
        jsonrpc = "2.0",
        id,
        result  = new
        {
            protocolVersion = "2024-11-05",
            capabilities    = new { tools = new { } },
            serverInfo      = new { name = "memctl", version = "1.0.0", instructions = GetIdentityContent() },
        },
    };

    private static object HandleToolsList(object id) => new
    {
        jsonrpc = "2.0",
        id,
        result = new
        {
            tools = new object[]
            {
                MakeTool("search",
                    "Hybrid semantic+BM25 search (RRF fusion). Use when query is general or you don't know exact terms.",
                    req: [("query",  "string",  "Search query text")],
                    opt: [("limit",  "integer", "Max results (default 10)"),
                          ("folder", "string",  "Filter to folder prefix (e.g. crypto)")],
                    dataDtoName: "SearchResultDto"),

                MakeTool("get",
                    "Retrieve a single note by ID or file path; increments access_count. Use when you have a specific identifier.",
                    req: [("id", "string", "Note ID or relative file path")],
                    opt: [],
                    dataDtoName: "NoteDto"),

                MakeTool("list",
                    "List notes sorted by importance (weight DESC, access_count DESC). Use at session start to load top memories.",
                    req: [],
                    opt: [("limit", "integer", "Max results (default 10)"),
                          ("tag",   "string",  "Filter by single tag")],
                    dataDtoName: "NoteListResultDto"),

                MakeTool("search_semantic",
                    "Pure vector similarity over embedded notes. Use when query is conceptual and exact words may not appear in notes.",
                    req: [("query",  "string",  "Search query text")],
                    opt: [("limit",  "integer", "Max results (default 10)"),
                          ("folder", "string",  "Filter to folder prefix"),
                          ("scope",  "string",  "Comma-separated note IDs to restrict to")],
                    dataDtoName: "SearchResultDto"),

                MakeTool("search_tags",
                    "Filter notes by tag membership. Use when user mentions a tag explicitly (e.g. 'notes tagged crypto').",
                    req: [("tags", "string", "Comma-separated tag list")],
                    opt: [("match", "string",  "any or all (default: any)"),
                          ("limit", "integer", "Max results (default 10)")],
                    dataDtoName: "SearchTagsResultDto"),

                MakeTool("search_date",
                    "Filter notes by creation date range. Use when user asks 'what did I work on last week'.",
                    req: [],
                    opt: [("from",  "string",  "ISO 8601 start date (inclusive)"),
                          ("to",    "string",  "ISO 8601 end date (inclusive)"),
                          ("limit", "integer", "Max results (default 10)")],
                    dataDtoName: "SearchDateResultDto"),

                MakeTool("search_links",
                    "Traverse wikilinks graph from a source note. Use to find notes linked to or from a specific note.",
                    req: [("id", "string", "Note ID or file path")],
                    opt: [("depth", "integer", "Link traversal depth (default 1)")],
                    dataDtoName: "SearchLinksResultDto"),

                MakeTool("get_identity",
                    "Retrieve the vault identity note (Layer 0 context). Use at the start of every session to load project context.",
                    req: [],
                    opt: [],
                    dataDtoName: "NoteDto"),

                MakeTool("create",
                    "Create a new note and index it immediately. Use to persist a decision, finding, or insight.",
                    req: [("content", "string", "Note body text")],
                    opt: [("title",    "string",  "Note title (extracted from content if omitted)"),
                          ("folder",   "string",  "Subfolder path relative to vault root (e.g. notes)"),
                          ("filename", "string",  "Filename without extension (default: sanitized title)")],
                    dataDtoName: "NoteDto"),

                MakeTool("update",
                    "Replace the entire content of an existing note. Use when rewriting a note.",
                    req: [("id",      "string", "Note ID or relative file path"),
                          ("content", "string", "New note body text")],
                    opt: [],
                    dataDtoName: "NoteDto"),

                MakeTool("append",
                    "Append text to an existing note without overwriting. Use when adding incremental updates.",
                    req: [("id",      "string", "Note ID or relative file path"),
                          ("content", "string", "Text to append")],
                    opt: [],
                    dataDtoName: "NoteDto"),

                MakeTool("set_weight",
                    "Set note importance weight (0.0-1.0); affects list order. Use to mark notes that matter for next session.",
                    req: [("id",     "string", "Note ID or relative file path"),
                          ("weight", "number", "Importance weight 0.0-1.0")],
                    opt: [],
                    dataDtoName: "WeightChangeDto"),

                MakeTool("delete",
                    "Permanently delete a note from vault and index. Use to remove obsolete notes.",
                    req: [("id", "string", "Note ID or relative file path")],
                    opt: [],
                    dataDtoName: "NoteDto"),
            },
        },
    };

    private async Task<object> HandleToolsCallAsync(object id, JsonElement prms, CancellationToken ct)
    {
        if (!prms.TryGetProperty("name", out var nameProp))
            return RpcError(id, -32602, "Missing 'name'");

        var name = nameProp.GetString() ?? "";
        var args = prms.TryGetProperty("arguments", out var a) ? a : default;

        try
        {
            MemctlOutcome? outcome = name switch
            {
                "search"          => await CallSearchAsync(args, ct),
                "get"             => CallGet(args),
                "list"            => CallList(args),
                "search_semantic" => await CallSearchSemanticAsync(args, ct),
                "search_tags"     => CallSearchTags(args),
                "search_date"     => CallSearchDate(args),
                "search_links"    => CallSearchLinks(args),
                "get_identity"    => CallGetIdentity(),
                "create"          => await CallCreateAsync(args, ct),
                "update"          => await CallUpdateAsync(args, ct),
                "append"          => await CallAppendAsync(args, ct),
                "set_weight"      => CallSetWeight(args),
                "delete"          => CallDelete(args),
                _                 => null,
            };

            if (outcome is null)
                return RpcError(id, -32601, $"Unknown tool: {name}");

            return ToolResult(id, outcome);
        }
        catch (Exception ex)
        {
            return ToolResultError(id, ex.Message);
        }
    }

    // --- tool implementations ---

    private async Task<MemctlOutcome> CallSearchAsync(JsonElement args, CancellationToken _)
    {
        var query  = Str(args, "query") ?? throw new InvalidOperationException("'query' is required");
        var limit  = Int(args, "limit",  10);
        var folder = Str(args, "folder");
        var emb    = await GetEmbeddingAsync().ConfigureAwait(false);
        return new SearchOperator(vaultReader, index, emb).Execute(vaultPath, query, limit, folder);
    }

    private MemctlOutcome CallGet(JsonElement args)
    {
        var id = Str(args, "id") ?? throw new InvalidOperationException("'id' is required");
        return new GetOperator(vaultReader, index).Execute(vaultPath, id);
    }

    private MemctlOutcome CallList(JsonElement args)
    {
        var limit = Int(args, "limit", 10);
        var tag   = Str(args, "tag");
        return new ListOperator(vaultReader, index).Execute(vaultPath, tag, limit);
    }

    private async Task<MemctlOutcome> CallSearchSemanticAsync(JsonElement args, CancellationToken _)
    {
        var query  = Str(args, "query") ?? throw new InvalidOperationException("'query' is required");
        var limit  = Int(args, "limit",  10);
        var folder = Str(args, "folder");
        var scope  = Str(args, "scope")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var emb    = await GetEmbeddingAsync().ConfigureAwait(false);
        return new SearchSemanticOperator(vaultReader, index, emb).Execute(vaultPath, query, limit, scope, folder);
    }

    private MemctlOutcome CallSearchTags(JsonElement args)
    {
        var tagsRaw  = Str(args, "tags") ?? throw new InvalidOperationException("'tags' is required");
        var tags     = tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matchAll = (Str(args, "match") ?? "any") == "all";
        var limit    = Int(args, "limit", 10);
        return new SearchTagsOperator(vaultReader, index).Execute(vaultPath, tags, matchAll, limit);
    }

    private MemctlOutcome CallSearchDate(JsonElement args)
    {
        var limit   = Int(args, "limit", 10);
        DateTime? from = Str(args, "from") is { } f ? DateTime.Parse(f).ToUniversalTime() : null;
        DateTime? to   = Str(args, "to")   is { } t ? DateTime.Parse(t).ToUniversalTime() : null;
        return new SearchDateOperator(vaultReader, index).Execute(vaultPath, from, to, limit);
    }

    private MemctlOutcome CallSearchLinks(JsonElement args)
    {
        var id    = Str(args, "id") ?? throw new InvalidOperationException("'id' is required");
        var depth = Int(args, "depth", 1);
        return new SearchLinksOperator(vaultReader, index).Execute(vaultPath, id, depth);
    }

    private MemctlOutcome CallGetIdentity()
    {
        var noteId = index.GetMetadata("identity_note_id");
        if (noteId is null)
            return MemctlOutcome.Ok("get_identity", "No identity note set", null);
        var note = index.GetById(noteId);
        if (note is null)
            return MemctlOutcome.Ok("get_identity", "Identity note not found (may have been deleted)", null);
        index.IncrementAccess(note.Id);
        return MemctlOutcome.Ok("get_identity", "Identity note", note);
    }

    private const string SessionProtocol = """

---
## memctl Session Protocol
First time in a project (no vault yet — `list` returns empty or errors):
1. Use CLI: `memctl init --vault ./vault` to create vault structure
2. Use CLI: `memctl ingest` to index existing markdown files
3. Then proceed with normal session protocol below

At the start of every session:
1. Call `list` (limit 10) — load top notes by importance weight
2. Call `search` with current task keywords — load relevant context

Before ending session:
- `create` or `append` to persist decisions, findings, or insights
- `set_weight 1.0` on notes that will matter next session
""";

    private string? GetIdentityContent()
    {
        var noteId = index.GetMetadata("identity_note_id");
        if (noteId is null) return SessionProtocol.TrimStart();
        var note = index.GetById(noteId);
        return note is null ? SessionProtocol.TrimStart() : note.Content + SessionProtocol;
    }

    // --- embedding lazy init ---

    private async Task<GemmaEmbeddingEngine> GetEmbeddingAsync()
    {
        if (_embedding is not null) return _embedding;
        var dir = MemctlConfig.ResolveModelDir(modelDir);
        if (!GemmaEmbeddingEngine.IsReady(dir))
            throw new InvalidOperationException(
                "Embedding model not found. Run 'memctl model download' first, then restart the MCP server.");
        _embedding = await GemmaEmbeddingEngine.CreateAsync(dir).ConfigureAwait(false);
        return _embedding;
    }

    // --- JSON-RPC / MCP response helpers ---

    private static object RpcError(object id, int code, string message) => new
    {
        jsonrpc = "2.0",
        id,
        error   = new { code, message },
    };

    private static object ToolResult(object id, MemctlOutcome outcome)
    {
        var text = JsonSerializer.Serialize(MemctlResultMapper.ToResult(outcome));
        return new
        {
            jsonrpc = "2.0",
            id,
            result  = new
            {
                content = new[] { new { type = "text", text } },
                isError = !outcome.Success,
            },
        };
    }

    private static object ToolResultError(object id, string message) => new
    {
        jsonrpc = "2.0",
        id,
        result  = new
        {
            content = new[] { new { type = "text", text = message } },
            isError = true,
        },
    };

    // --- tool schema builder ---

    private static object MakeTool(
        string name,
        string description,
        (string name, string type, string desc)[] req,
        (string name, string type, string desc)[] opt,
        string? dataDtoName = null)
    {
        var props = new Dictionary<string, object>();
        foreach (var (n, t, d) in req) props[n] = new { type = t, description = d };
        foreach (var (n, t, d) in opt) props[n] = new { type = t, description = d };
        return new
        {
            name,
            description,
            inputSchema = new
            {
                type       = "object",
                properties = props,
                required   = req.Select(r => r.name).ToArray(),
            },
            outputSchema = BuildOutputSchema(dataDtoName),
        };
    }

    private static object BuildOutputSchema(string? dataDtoName) => new
    {
        type       = "object",
        properties = new Dictionary<string, object>
        {
            ["success"] = new { type = "boolean", description = "True if the operation succeeded" },
            ["action"]  = new { type = "string",  description = "Echoed action name matching the tool" },
            ["message"] = new { type = "string",  description = "Human-readable status message" },
            ["data"]    = dataDtoName is null
                ? (object)new { type = new[] { "object", "null" }, description = "No payload" }
                : new
                  {
                      type        = new[] { "object", "null" },
                      description = $"Boundary DTO: {dataDtoName} (see Boundary/MemctlResult.cs for full schema). " +
                                    "Always typed; envelope keys (success/action/message/data) are stable.",
                  },
        },
        required = new[] { "success", "action", "message" },
    };

    // --- parameter extraction helpers ---

    private static object ParseId(JsonElement idEl) =>
        idEl.ValueKind switch
        {
            JsonValueKind.Number => (object)idEl.GetInt64(),
            _                    => idEl.GetString()!,
        };

    private static string? Str(JsonElement args, string key) =>
        args.ValueKind != JsonValueKind.Undefined
        && args.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int Int(JsonElement args, string key, int defaultVal) =>
        args.ValueKind != JsonValueKind.Undefined
        && args.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : defaultVal;

    private static float? FloatArg(JsonElement args, string key) =>
        args.ValueKind != JsonValueKind.Undefined
        && args.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetSingle() : null;

    // --- write tool implementations ---

    private async Task<MemctlOutcome> CallCreateAsync(JsonElement args, CancellationToken _)
    {
        var content  = Str(args, "content")  ?? throw new InvalidOperationException("'content' is required");
        var title    = Str(args, "title");
        var folder   = Str(args, "folder");
        var filename = Str(args, "filename");
        var emb      = await GetEmbeddingAsync().ConfigureAwait(false);
        return new VaultWriteOperator(vaultReader, index, emb).ExecuteCreate(vaultPath, content, title, folder, filename);
    }

    private async Task<MemctlOutcome> CallUpdateAsync(JsonElement args, CancellationToken _)
    {
        var id      = Str(args, "id")      ?? throw new InvalidOperationException("'id' is required");
        var content = Str(args, "content") ?? throw new InvalidOperationException("'content' is required");
        var emb     = await GetEmbeddingAsync().ConfigureAwait(false);
        return new VaultWriteOperator(vaultReader, index, emb).ExecuteUpdate(vaultPath, id, content);
    }

    private async Task<MemctlOutcome> CallAppendAsync(JsonElement args, CancellationToken _)
    {
        var id      = Str(args, "id")      ?? throw new InvalidOperationException("'id' is required");
        var content = Str(args, "content") ?? throw new InvalidOperationException("'content' is required");
        var emb     = await GetEmbeddingAsync().ConfigureAwait(false);
        return new VaultWriteOperator(vaultReader, index, emb).ExecuteAppend(vaultPath, id, content);
    }

    private MemctlOutcome CallSetWeight(JsonElement args)
    {
        var id     = Str(args,      "id")     ?? throw new InvalidOperationException("'id' is required");
        var weight = FloatArg(args, "weight") ?? throw new InvalidOperationException("'weight' is required");
        var note   = index.GetById(id) ?? index.GetByFilePath(id);
        if (note is null) return MemctlOutcome.Fail("set_weight", $"Note not found: {id}");
        var clamped = Math.Clamp(weight, 0f, 1f);
        index.SetWeight(note.Id, clamped);
        var rounded = (float)Math.Round(clamped, 2);
        return MemctlOutcome.Ok("set_weight", $"Weight set to {rounded}",
            new WeightChange(note.Id, note.FilePath, rounded));
    }

    private MemctlOutcome CallDelete(JsonElement args)
    {
        var id = Str(args, "id") ?? throw new InvalidOperationException("'id' is required");
        return new VaultWriteOperator(vaultReader, index, null).ExecuteDelete(vaultPath, id);
    }
}
