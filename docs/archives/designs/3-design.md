# Technical Design: Add MCP Server Mode

**Spec:** docs/specs/3-spec.md
**Task:** 3
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

`memctl mcp` starts a long-running stdio JSON-RPC 2.0 server (no SDK — raw implementation). It maps 7 MCP tools to existing Operators with no changes to Operator logic. A guard is added to `SqliteNoteIndex.Initialize` to prevent connection leaks in the long-running process.

Control flow:
```
stdin → McpServerOperator.RunAsync → parse JSON-RPC → route to tool handler → Operator.Execute → serialize → stdout
```

**No new NuGet packages.** Raw JSON-RPC over stdio, System.Text.Json for parsing/serialization.

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/McpServerOperator.cs` | All MCP protocol + 7 tool handlers | `McpServerOperator` |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs` | Guard in Initialize: skip if already open | Prevent connection leak in long-running server |
| `src/memctl/Bootstrap/Program.cs` | Wire `mcp` command | FR-001 |

### Integration Code Blocks

```
// INTEGRATION: SqliteNoteIndex.cs → Initialize() — add already-open guard
// old_string:
    public void Initialize(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        ApplySchema();
    }

// new_string:
    public void Initialize(string dbPath)
    {
        if (_db is not null) return;  // already initialized (e.g. MCP long-running server)
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        ApplySchema();
    }
```

```
// INTEGRATION: Program.cs — add mcp command before `return await root.InvokeAsync(args);`
// old_string:
// --- weight ---

// new_string:
// --- mcp ---
var mcpCmd = new Command("mcp", "Start a stdio MCP server exposing the vault as an AI memory layer");
mcpCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new McpServerOperator(vaultReader, noteIndex, vault, g.ModelDir);
    await op.RunAsync(ctx.GetCancellationToken());
});
root.AddCommand(mcpCmd);

// --- weight ---
```

---

## 3. New File: McpServerOperator.cs

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
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
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private GemmaEmbeddingEngine? _embedding;

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Initialize index once at startup
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
        // Notifications have no id — no response
        if (!msg.TryGetProperty("id", out var idEl)) return null;
        var id     = ParseId(idEl);
        var method = msg.TryGetProperty("method", out var m) ? m.GetString() : null;
        var prms   = msg.TryGetProperty("params",  out var p) ? p : default;

        return method switch
        {
            "initialize"       => HandleInitialize(id),
            "tools/list"       => HandleToolsList(id),
            "tools/call"       => await HandleToolsCallAsync(id, prms, ct),
            "ping"             => new { jsonrpc = "2.0", id, result = new { } },
            _                  => RpcError(id, -32601, "Method not found"),
        };
    }

    // --- MCP protocol handlers ---

    private static object HandleInitialize(object id) => new
    {
        jsonrpc = "2.0",
        id,
        result  = new
        {
            protocolVersion = "2024-11-05",
            capabilities    = new { tools = new { } },
            serverInfo      = new { name = "memctl", version = "1.0.0" },
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
                    req: [("query", "string", "Search query text")],
                    opt: [("limit", "integer", "Max results (default 10)"),
                          ("folder", "string", "Filter to folder prefix (e.g. crypto)")]),

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
                    req: [("query", "string", "Search query text")],
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
        var limit  = Int(args, "limit", 10);
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

    // --- embedding lazy init ---

    private async Task<GemmaEmbeddingEngine> GetEmbeddingAsync()
    {
        if (_embedding is not null) return _embedding;
        _embedding = await GemmaEmbeddingEngine.CreateAsync(MemctlConfig.ResolveModelDir(modelDir)).ConfigureAwait(false);
        return _embedding;
    }

    // --- JSON-RPC / MCP helpers ---

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
}
```

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5 — send JSON-RPC via echo pipe)

| Scenario | Command | Expected | FR |
|----------|---------|----------|-----|
| mcp --help | `memctl mcp --help` | exit 0, shows usage | FR-001 |
| initialize handshake | `echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}' \| memctl mcp --vault .` | stdout contains `serverInfo` and `memctl` | FR-007 |
| tools/list | multi-message pipe | stdout contains 7 tool names | FR-008 |

Note: Full MCP session testing via process pipe.

---

## 12. Implementation Order

1. `SqliteNoteIndex.cs` — add Initialize guard
2. `McpServerOperator.cs` — new file (full implementation above)
3. `Program.cs` — wire mcp command

---

## 14. Traceability Matrix

| Requirement | Design Section | Files |
|-------------|---------------|-------|
| FR-001/002/003 | Program.cs mcp command | Bootstrap/Program.cs |
| FR-005/007/008/009/010 | McpServerOperator protocol | Operators/McpServerOperator.cs |
| FR-011/012 | RunAsync startup init | McpServerOperator.cs |
| FR-013 | GetEmbeddingAsync lazy | McpServerOperator.cs |
| FR-014–017 | CallSearchAsync | McpServerOperator.cs |
| FR-018–021 | CallGet | McpServerOperator.cs |
| FR-022–026 | CallList | McpServerOperator.cs |
| FR-027–029 | CallSearchSemanticAsync | McpServerOperator.cs |
| FR-030–032 | CallSearchTags | McpServerOperator.cs |
| FR-033–036 | CallSearchDate | McpServerOperator.cs |
| FR-037–039 | CallSearchLinks | McpServerOperator.cs |
| NFR-007 | stdout only for JSON-RPC | McpServerOperator.cs (Console.In/Out) |
| SQLite guard | SqliteNoteIndex.Initialize | Implementations/Index/ |
