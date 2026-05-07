using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Memctl.Boundary;
using Memctl.Boundary.Llm;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Implementations.Llm;

public sealed class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string     _model;

    public OpenAiLlmClient(string baseUrl, string model, string? apiKey)
    {
        _model = model;
        _http  = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<NoteEnrichment> EnrichAsync(string content, IReadOnlyList<Note> existingNotes, CancellationToken ct = default)
    {
        var noteList = existingNotes.Take(50)
            .Select(n => $"- {n.Title} (tags: {string.Join(", ", n.Tags)})")
            .ToList();

        var existingContext = noteList.Count > 0
            ? $"\n\nExisting notes in vault:\n{string.Join('\n', noteList)}"
            : "";

        var prompt = $"""
            Analyze the following note and return a JSON object with:
            - "title": concise title (string)
            - "tags": relevant tags, lowercase, snake_case (array of strings)
            - "links": titles of existing vault notes this note relates to (array of strings, only from the existing notes list)

            Return ONLY the JSON object, no explanation.
            {existingContext}

            Note content:
            {content[..Math.Min(content.Length, 2000)]}
            """;

        var request = new OpenAiChatRequest
        {
            Model          = _model,
            Messages       = [new OpenAiChatMessage { Role = "user", Content = prompt }],
            ResponseFormat = new OpenAiResponseFormat { Type = "json_object" },
            MaxTokens      = 512,
        };

        var json = JsonSerializer.Serialize(request, OpenAiJsonContext.Default.OpenAiChatRequest);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        try
        {
            using var result = JsonDocument.Parse(text);
            var root = result.RootElement;

            return new NoteEnrichment
            {
                Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Tags  = ParseStringArray(root, "tags"),
                Links = ParseStringArray(root, "links"),
            };
        }
        catch
        {
            /* LLM returned invalid JSON — return empty enrichment */
            return new NoteEnrichment();
        }
    }

    private const int MaxDistillInputChars = 16_000;

    public async Task<DistillResult> DistillAsync(string conversationContent, IReadOnlyList<Note> existingNotes, CancellationToken ct = default)
    {
        var truncated = conversationContent.Length > MaxDistillInputChars
            ? conversationContent[..MaxDistillInputChars]
            : conversationContent;

        var noteContext = existingNotes.Take(50)
            .Select(n => $"- {n.Title} [{string.Join(", ", n.Tags)}]")
            .ToList();

        var existingSection = noteContext.Count > 0
            ? $"\n\nExisting vault notes (link targets only from this list):\n{string.Join('\n', noteContext)}"
            : "";

        var prompt = $$"""
            You are a memory consolidation system. Read the conversation below and extract high-signal items worth remembering long-term.

            For each item return a JSON extraction with:
            - "type": "decision" | "pattern" | "lesson"
            - "title": concise declarative title (string)
            - "content": full markdown content, 3rd-person declarative, no filler (string)
            - "tags": lowercase snake_case tags (array of strings)
            - "links": titles of related vault notes from the existing list only (array of strings)
            - "weight": importance 1.0-1.5 (float)
            - "rationale": one sentence why this is worth remembering (string)

            Return ONLY valid JSON with key "extractions" containing an array. Empty array if nothing is worth remembering.
            {{existingSection}}

            Conversation:
            {{truncated}}
            """;

        var request = new OpenAiChatRequest
        {
            Model          = _model,
            Messages       = [new OpenAiChatMessage { Role = "user", Content = prompt }],
            ResponseFormat = new OpenAiResponseFormat { Type = "json_object" },
            MaxTokens      = 2048,
        };

        var json = JsonSerializer.Serialize(request, OpenAiJsonContext.Default.OpenAiChatRequest);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        try
        {
            using var result = JsonDocument.Parse(text);
            if (!result.RootElement.TryGetProperty("extractions", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return new DistillResult([]);

            var notes = arr.EnumerateArray().Select(e =>
            {
                var weight = e.TryGetProperty("weight", out var w) ? (float)w.GetDouble() : 1.0f;
                return new DistilledNote(
                    Type:      e.TryGetProperty("type",      out var t) ? t.GetString() ?? "lesson" : "lesson",
                    Title:     e.TryGetProperty("title",     out var ti) ? ti.GetString() ?? "" : "",
                    Content:   e.TryGetProperty("content",   out var c) ? c.GetString() ?? "" : "",
                    Tags:      ParseStringArray(e, "tags"),
                    Links:     ParseStringArray(e, "links"),
                    Weight:    Math.Clamp(weight, 1.0f, 1.5f),
                    Rationale: e.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "");
            }).ToArray();

            return new DistillResult(notes);
        }
        catch
        {
            /* LLM returned invalid JSON — return empty result */
            return new DistillResult([]);
        }
    }

    public async Task<ContradictionResult> CheckContradictionAsync(
        DistilledNote       newNote,
        IReadOnlyList<Note> candidates,
        CancellationToken   ct = default)
    {
        var candidateList = candidates
            .Select(c => $"- id: {c.Id}\n  title: {c.Title}\n  content: {c.Content[..Math.Min(c.Content.Length, 500)]}")
            .ToList();

        var prompt = $$"""
            You are a memory quality-control system. Determine whether the new memory note contradicts any existing note.

            New note:
            title: {{newNote.Title}}
            type: {{newNote.Type}}
            content: {{newNote.Content[..Math.Min(newNote.Content.Length, 1000)]}}

            Existing notes of the same type:
            {{string.Join('\n', candidateList)}}

            Return ONLY a JSON object with:
            - "contradicts": boolean — true if any existing note directly conflicts with the new note
            - "existing_id": string | null — ID of the contradicting note (from the list above), null if no contradiction
            - "resolution": "keep_new" | "keep_existing" | "merge" — which note to keep (only relevant when contradicts=true)
            - "merged_content": string | null — merged markdown content (only when resolution="merge"), null otherwise
            - "rationale": string — one sentence explanation
            """;

        var request = new OpenAiChatRequest
        {
            Model          = _model,
            Messages       = [new OpenAiChatMessage { Role = "user", Content = prompt }],
            ResponseFormat = new OpenAiResponseFormat { Type = "json_object" },
            MaxTokens      = 1024,
        };

        var json = JsonSerializer.Serialize(request, OpenAiJsonContext.Default.OpenAiChatRequest);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(httpReq, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        try
        {
            using var result = JsonDocument.Parse(text);
            var root = result.RootElement;

            var contradicts = root.TryGetProperty("contradicts", out var cv) && cv.GetBoolean();
            var existingId  = root.TryGetProperty("existing_id", out var ei) ? ei.GetString() : null;
            var resStr      = root.TryGetProperty("resolution",  out var rv) ? rv.GetString() : null;
            var merged      = root.TryGetProperty("merged_content", out var mc) ? mc.GetString() : null;
            var rationale   = root.TryGetProperty("rationale",   out var rt) ? rt.GetString() ?? "" : "";

            var resolution = resStr switch
            {
                "keep_existing" => ContradictionResolution.KeepExisting,
                "merge"         => ContradictionResolution.Merge,
                _               => ContradictionResolution.KeepNew,
            };

            return new ContradictionResult(contradicts, existingId, resolution, merged, rationale);
        }
        catch
        {
            /* LLM returned invalid JSON — no contradiction */
            return new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, "");
        }
    }

    private static string[] ParseStringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];
        return [.. el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)];
    }
}
