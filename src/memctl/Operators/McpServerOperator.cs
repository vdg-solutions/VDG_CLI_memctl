using System.Text.Json;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Config;
using Memctl.Implementations.Embedding;

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
                    "Hybrid semantic+BM25 search over vault notes, sorted by relevance",
                    req: [("query",  "string",  "Search query text")],
                    opt: [("limit",  "integer", "Max results (default 10)"),
                          ("folder", "string",  "Filter to folder prefix (e.g. crypto)")]),

                MakeTool("get",
                    "Retrieve a note by ID or file path; increments access_count",
                    req: [("id", "string", "Note ID or relative file path")],
                    opt: []),

                MakeTool("list",
                    "List notes sorted by importance (weight DESC, access_count DESC)",
                    req: [],
                    opt: [("limit", "integer", "Max results (default 10)"),
                          ("tag",   "string",  "Filter by single tag")]),

                MakeTool("search_semantic",
                    "Pure vector similarity search over embedded notes",
                    req: [("query",  "string",  "Search query text")],
                    opt: [("limit",  "integer", "Max results (default 10)"),
                          ("folder", "string",  "Filter to folder prefix"),
                          ("scope",  "string",  "Comma-separated note IDs to restrict to")]),

                MakeTool("search_tags",
                    "Find notes that have specific tag(s)",
                    req: [("tags", "string", "Comma-separated tag list")],
                    opt: [("match", "string",  "any or all (default: any)"),
                          ("limit", "integer", "Max results (default 10)")]),

                MakeTool("search_date",
                    "Find notes by creation date range",
                    req: [],
                    opt: [("from",  "string",  "ISO 8601 start date (inclusive)"),
                          ("to",    "string",  "ISO 8601 end date (inclusive)"),
                          ("limit", "integer", "Max results (default 10)")]),

                MakeTool("search_links",
                    "Find notes linked to or from a given note (wikilinks graph)",
                    req: [("id", "string", "Note ID or file path")],
                    opt: [("depth", "integer", "Link traversal depth (default 1)")]),

                MakeTool("get_identity",
                    "Retrieve the vault identity note — load this first in every session for context",
                    req: [],
                    opt: []),

                MakeTool("create",
                    "Create a new note in the vault and index it immediately",
                    req: [("content", "string", "Note body text")],
                    opt: [("title",    "string",  "Note title (extracted from content if omitted)"),
                          ("folder",   "string",  "Subfolder path relative to vault root (e.g. notes)"),
                          ("filename", "string",  "Filename without extension (default: sanitized title)")]),

                MakeTool("update",
                    "Replace the content of an existing note by ID or file path",
                    req: [("id",      "string", "Note ID or relative file path"),
                          ("content", "string", "New note body text")],
                    opt: []),

                MakeTool("append",
                    "Append text to an existing note without overwriting existing content",
                    req: [("id",      "string", "Note ID or relative file path"),
                          ("content", "string", "Text to append")],
                    opt: []),

                MakeTool("set_weight",
                    "Set note importance weight (0.0-1.0); affects list order",
                    req: [("id",     "string", "Note ID or relative file path"),
                          ("weight", "number", "Importance weight 0.0-1.0")],
                    opt: []),

                MakeTool("delete",
                    "Permanently delete a note from vault and index by ID or file path",
                    req: [("id", "string", "Note ID or relative file path")],
                    opt: []),
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
        return MemctlOutcome.Ok("get_identity", "Identity note", GetOperator.NoteToData(note));
    }

    private string? GetIdentityContent()
    {
        var noteId = index.GetMetadata("identity_note_id");
        if (noteId is null) return null;
        var note = index.GetById(noteId);
        return note?.Content;
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
        var text = JsonSerializer.Serialize(new
        {
            success = outcome.Success,
            action  = outcome.Action,
            message = outcome.Message,
            data    = outcome.Data,
        });
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
        (string name, string type, string desc)[] opt)
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
        };
    }

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
        return MemctlOutcome.Ok("set_weight", $"Weight set to {(float)Math.Round(clamped, 2)}",
            new { id = note.Id, file = note.FilePath, weight = (float)Math.Round(clamped, 2) });
    }

    private MemctlOutcome CallDelete(JsonElement args)
    {
        var id = Str(args, "id") ?? throw new InvalidOperationException("'id' is required");
        return new VaultWriteOperator(vaultReader, index, null).ExecuteDelete(vaultPath, id);
    }
}
