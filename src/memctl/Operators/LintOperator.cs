using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

internal sealed record LintOptions(
    bool    Semantic,
    bool    Self,
    bool    DryRun,
    bool    Save,
    bool    UpdateTimestampOnly,
    string  Format,
    string? LlmUrl,
    string? LlmModel,
    string? LlmKey);

public sealed class LintOperator(IVaultReader vaultReader, INoteIndex index)
{
    private const double DuplicateSimThreshold  = 0.92;
    private const double DecayRiskWeightLow     = 0.05;
    private const double DecayRiskWeightHigh    = 0.30;
    private const int    DecayRiskDaysThreshold = 60;
    private const int    DecayRiskMinInbound    = 2;
    private const int    SemanticBatchSize      = 50;
    private const int    LlmTimeoutSeconds      = 30;
    private const string LastSemanticLintKey    = "last_semantic_lint";
    private const int    SemanticOverdueDays    = 14;

    internal async Task<(MemctlOutcome outcome, int exitCode)> ExecuteAsync(string vaultPath, LintOptions opts)
    {
        index.Initialize(IngestOperator.DbPath(vaultPath));

        // --update-timestamp only
        if (opts.UpdateTimestampOnly)
        {
            if (!opts.DryRun)
                index.SetMetadata(LastSemanticLintKey, DateTime.UtcNow.ToString("O"));
            return (MemctlOutcome.Ok("lint", "Semantic lint timestamp updated"), 0);
        }

        // validate semantic flag combos
        if (opts.Self && opts.LlmUrl is not null)
            return (MemctlOutcome.Fail("lint", "--self and --llm-url are mutually exclusive"), 1);

        if (opts.Semantic && !opts.Self && (opts.LlmUrl is null || opts.LlmModel is null))
            return (MemctlOutcome.Fail("lint", "Semantic lint requires --llm-url and --llm-model, or --self"), 1);

        var notes = index.GetAll(includeArchived: false);

        if (notes.Count == 0)
        {
            var emptyData = new { structural = EmptyStructural(), semantic = (object?)null };
            return (MemctlOutcome.Ok("lint", "Lint complete: 0 notes, 0 issues", emptyData), 0);
        }

        var structural = RunStructural(notes);

        // --save
        if (opts.Save && !opts.DryRun)
            SaveReport(structural, vaultPath);

        // --format md
        if (opts.Format == "md")
        {
            Console.WriteLine(FormatMarkdown(structural, null));
            return (MemctlOutcome.Ok("lint", BuildMessage(notes.Count, structural)), 0);
        }

        // semantic tier
        object? semanticResult = null;
        int exitCode = 0;

        if (opts.Semantic)
        {
            if (opts.Self)
            {
                Console.WriteLine(BuildSelfPrompt(notes));
                // structural JSON still returned via outcome
            }
            else
            {
                var (sem, timedOut) = await RunSemanticAsync(notes, opts);
                semanticResult = sem;
                if (timedOut) exitCode = 1;

                if (!timedOut && !opts.DryRun)
                    index.SetMetadata(LastSemanticLintKey, DateTime.UtcNow.ToString("O"));
            }
        }

        var data = new { structural, semantic = semanticResult };
        return (MemctlOutcome.Ok("lint", BuildMessage(notes.Count, structural), data), exitCode);
    }

    // --- structural ---

    private static object RunStructural(IReadOnlyList<Note> notes)
    {
        var titleToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in notes)
            titleToId.TryAdd(n.Title, n.Id);

        // inbound link counts
        var inboundCounts = new Dictionary<string, int>();
        foreach (var note in notes)
            foreach (var link in note.Links)
                if (titleToId.TryGetValue(link, out var targetId))
                {
                    inboundCounts.TryGetValue(targetId, out var c);
                    inboundCounts[targetId] = c + 1;
                }

        var orphans      = FindOrphans(notes, inboundCounts);
        var brokenLinks  = FindBrokenLinks(notes, titleToId);
        var duplicates   = FindDuplicates(notes);
        var decayRisk    = FindDecayRisk(notes, inboundCounts);

        return new { orphans, broken_links = brokenLinks, duplicates, decay_risk = decayRisk };
    }

    private static object EmptyStructural() =>
        new { orphans = Array.Empty<object>(), broken_links = Array.Empty<object>(), duplicates = Array.Empty<object>(), decay_risk = Array.Empty<object>() };

    private static List<object> FindOrphans(IReadOnlyList<Note> notes, Dictionary<string, int> inboundCounts) =>
        notes
            .Where(n => !inboundCounts.ContainsKey(n.Id))
            .Select(n => (object)new { id = n.Id, title = n.Title, file_path = n.FilePath })
            .ToList();

    private static List<object> FindBrokenLinks(IReadOnlyList<Note> notes, Dictionary<string, string> titleToId)
    {
        var broken = new List<object>();
        foreach (var note in notes)
            foreach (var link in note.Links)
                if (!titleToId.ContainsKey(link))
                    broken.Add(new { note_id = note.Id, note_title = note.Title, broken_link = link });
        return broken;
    }

    private static List<object> FindDuplicates(IReadOnlyList<Note> notes)
    {
        var embedded = notes.Where(n => n.Embedding is not null).ToList();
        var dupes = new List<object>();

        for (var i = 0; i < embedded.Count; i++)
        for (var j = i + 1; j < embedded.Count; j++)
        {
            var sim = CosineSim(embedded[i].Embedding!, embedded[j].Embedding!);
            if (sim > DuplicateSimThreshold)
            {
                // deterministic ordering
                var (a, b) = string.Compare(embedded[i].Id, embedded[j].Id, StringComparison.Ordinal) < 0
                    ? (embedded[i], embedded[j])
                    : (embedded[j], embedded[i]);

                dupes.Add(new
                {
                    note_a_id    = a.Id,
                    note_a_title = a.Title,
                    note_b_id    = b.Id,
                    note_b_title = b.Title,
                    similarity   = Math.Round(sim, 4),
                });
            }
        }
        return dupes;
    }

    private static List<object> FindDecayRisk(IReadOnlyList<Note> notes, Dictionary<string, int> inboundCounts)
    {
        var now  = DateTime.UtcNow;
        var risk = new List<object>();

        foreach (var note in notes)
        {
            var inbound = inboundCounts.GetValueOrDefault(note.Id);
            if (inbound < DecayRiskMinInbound) continue;
            if (note.Weight < DecayRiskWeightLow || note.Weight > DecayRiskWeightHigh) continue;

            var daysSince = (now - note.Modified).TotalDays;
            if (daysSince <= DecayRiskDaysThreshold) continue;

            risk.Add(new
            {
                id                  = note.Id,
                title               = note.Title,
                weight              = note.Weight,
                days_since_modified = (int)daysSince,
                inbound_link_count  = inbound,
            });
        }
        return risk;
    }

    private static float CosineSim(float[] a, float[] b)
    {
        // L2-normalized vectors — dot product = cosine similarity
        var dot = 0f;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++) dot += a[i] * b[i];
        return dot;
    }

    // --- semantic ---

    private static async Task<(object? result, bool timedOut)> RunSemanticAsync(IReadOnlyList<Note> notes, LintOptions opts)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(opts.LlmUrl!.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromSeconds(LlmTimeoutSeconds),
        };
        if (!string.IsNullOrEmpty(opts.LlmKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.LlmKey);

        var allContradictions = new List<object>();
        var allStaleClaims    = new List<object>();
        var allMissingLinks   = new List<object>();
        var allSummaryGaps    = new List<object>();

        var batches = notes.Chunk(SemanticBatchSize);

        foreach (var batch in batches)
        {
            var notesText = string.Join("\n\n---\n\n", batch.Select(n =>
                $"### {n.Title}\n{n.Content[..Math.Min(n.Content.Length, 500)]}"));

            var prompt = $$"""
                Analyze the following vault notes and return a JSON object with four arrays:
                - "contradictions": conflicting statements (each: { "notes": ["title1","title2"], "description": "..." })
                - "stale_claims": outdated information (each: { "note": "title", "claim": "..." })
                - "missing_links": related notes that should cross-reference (each: { "notes": ["title1","title2"], "reason": "..." })
                - "summary_gaps": topics needing a dedicated note (each: { "topic": "..." })

                Return ONLY valid JSON.

                {{notesText}}
                """;

            var request = new
            {
                model          = opts.LlmModel,
                messages       = new[] { new { role = "user", content = prompt } },
                response_format = new { type = "json_object" },
                max_tokens     = 1024,
            };

            var jsonBody = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            };

            HttpResponseMessage resp;
            try   { resp = await http.SendAsync(req); }
            catch (TaskCanceledException) { return (null, true); }  // timeout

            if (!resp.IsSuccessStatusCode)
                continue;  // non-fatal HTTP error — skip batch

            var body = await resp.Content.ReadAsStringAsync();

            try
            {
                using var doc  = JsonDocument.Parse(body);
                var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
                using var res  = JsonDocument.Parse(text);
                var root = res.RootElement;

                allContradictions.AddRange(ParseObjectArray(root, "contradictions"));
                allStaleClaims   .AddRange(ParseObjectArray(root, "stale_claims"));
                allMissingLinks  .AddRange(ParseObjectArray(root, "missing_links"));
                allSummaryGaps   .AddRange(ParseObjectArray(root, "summary_gaps"));
            }
            catch { /* malformed LLM JSON — skip batch */ }
        }

        return (new
        {
            contradictions = allContradictions,
            stale_claims   = allStaleClaims,
            missing_links  = allMissingLinks,
            summary_gaps   = allSummaryGaps,
        }, false);
    }

    private static IEnumerable<object> ParseObjectArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in el.EnumerateArray())
            yield return item.Clone();
    }

    // --- self prompt ---

    private static string BuildSelfPrompt(IReadOnlyList<Note> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Vault Self-Analysis Request");
        sb.AppendLine();
        sb.AppendLine("Please analyze the following vault notes for:");
        sb.AppendLine("1. Contradictions between notes");
        sb.AppendLine("2. Stale claims that may be outdated");
        sb.AppendLine("3. Missing cross-references between related notes");
        sb.AppendLine("4. Concepts mentioned in multiple notes that deserve a dedicated page");
        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();

        foreach (var note in notes)
        {
            sb.AppendLine($"### {note.Title}");
            sb.AppendLine(note.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.AppendLine("## Instructions");
        sb.AppendLine("Return a structured report with four sections:");
        sb.AppendLine("- **Contradictions**: conflicting statements across notes");
        sb.AppendLine("- **Stale Claims**: information that may be outdated");
        sb.AppendLine("- **Missing Links**: related notes that should cross-reference each other");
        sb.AppendLine("- **Summary Gaps**: topics needing a dedicated note");
        sb.AppendLine();
        sb.AppendLine("When done, run: memctl lint --update-timestamp");

        return sb.ToString();
    }

    // --- save ---

    private void SaveReport(object structural, string vaultPath)
    {
        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var content = FormatMarkdown(structural, null);
        var note = new Note
        {
            Id       = Guid.NewGuid().ToString("N"),
            FilePath = $"lint/{dateStr}-structural.md",
            Title    = $"Lint Report {dateStr}",
            Content  = content,
            Tags     = ["lint", "health-check"],
            Links    = [],
            Created  = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        vaultReader.WriteNote(note, vaultPath, note.FilePath);
        index.Upsert(note);
    }

    // --- formatting ---

    private static string FormatMarkdown(object structural, object? semantic)
    {
        // serialize structural to JsonElement for iteration
        var json = JsonSerializer.Serialize(structural);
        using var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var orphans     = CountArray(root, "orphans");
        var broken      = CountArray(root, "broken_links");
        var dupes       = CountArray(root, "duplicates");
        var decay       = CountArray(root, "decay_risk");
        var total       = orphans + broken + dupes + decay;

        var sb = new StringBuilder();
        sb.AppendLine($"# Vault Lint Report — {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine(total == 0
            ? "**0 issues found**"
            : $"**{total} issues found** (orphans: {orphans}, broken: {broken}, dupes: {dupes}, decay: {decay})");
        sb.AppendLine();

        sb.AppendLine($"## Orphan Notes ({orphans})");
        foreach (var item in root.GetProperty("orphans").EnumerateArray())
            sb.AppendLine($"- [{item.GetProperty("title").GetString()}]({item.GetProperty("file_path").GetString()})");
        sb.AppendLine();

        sb.AppendLine($"## Broken Links ({broken})");
        foreach (var item in root.GetProperty("broken_links").EnumerateArray())
            sb.AppendLine($"- **{item.GetProperty("note_title").GetString()}**: `[[{item.GetProperty("broken_link").GetString()}]]`");
        sb.AppendLine();

        sb.AppendLine($"## Duplicate Candidates ({dupes})");
        foreach (var item in root.GetProperty("duplicates").EnumerateArray())
            sb.AppendLine($"- [{item.GetProperty("note_a_title").GetString()}] ↔ [{item.GetProperty("note_b_title").GetString()}] (similarity: {item.GetProperty("similarity").GetDouble():F4})");
        sb.AppendLine();

        sb.AppendLine($"## Decay Risk ({decay})");
        foreach (var item in root.GetProperty("decay_risk").EnumerateArray())
            sb.AppendLine($"- [{item.GetProperty("title").GetString()}]({item.GetProperty("id").GetString()}) — weight: {item.GetProperty("weight").GetDouble():F2}, days inactive: {item.GetProperty("days_since_modified").GetInt32()}, inbound links: {item.GetProperty("inbound_link_count").GetInt32()}");

        if (semantic is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## Semantic Analysis");
            sb.AppendLine("*(see data.semantic in JSON output for details)*");
        }

        return sb.ToString();
    }

    private static int CountArray(JsonElement root, string key) =>
        root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.GetArrayLength()
            : 0;

    private static string BuildMessage(int noteCount, object structural)
    {
        var json = JsonSerializer.Serialize(structural);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var total = CountArray(root, "orphans") + CountArray(root, "broken_links")
                  + CountArray(root, "duplicates") + CountArray(root, "decay_risk");
        return $"Lint complete: {noteCount} notes, {total} issues";
    }
}
