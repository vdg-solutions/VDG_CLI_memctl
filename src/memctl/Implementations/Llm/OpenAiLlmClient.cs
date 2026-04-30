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

    private static string[] ParseStringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];
        return [.. el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)];
    }
}
