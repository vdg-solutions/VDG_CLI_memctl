using System.Text.Json;
using System.Text.RegularExpressions;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class CaptureOperator(IVaultReader vaultReader, INoteIndex index, GemmaEmbeddingEngine? embedding)
{
    private const float ConversationNoteWeight = 0.5f;
    private const int   MinTurnLength     = 50;

    private static readonly Regex CodeBlockPattern =
        new(@"```[\s\S]*?```", RegexOptions.Compiled);

    public MemctlOutcome Execute(
        string  vaultPath,
        string? conversationIdRaw,
        IReadOnlyList<(string Role, string Content)> turns,
        bool    dryRun)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var filteredTurns = turns.Where(t => !IsNoise(t.Content)).ToList();
        if (filteredTurns.Count == 0)
            return MemctlOutcome.Ok("capture", "no meaningful turns");

        var safeId  = SanitizeConversationId(conversationIdRaw ?? GenerateConversationId());
        var date    = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var relPath = $"chats/{date}-{safeId}.md";
        var absPath = Path.Combine(vaultPath, relPath);

        if (dryRun)
        {
            var preview = $"# Conversation {date} — {safeId}\n\n{FormatTurns(filteredTurns)}";
            return MemctlOutcome.Ok("capture", preview,
                new CaptureReport(true, relPath, filteredTurns.Count, null));
        }

        if (!File.Exists(absPath))
            return CreateNote(vaultPath, relPath, date, safeId, filteredTurns);

        return AppendNote(vaultPath, relPath, absPath, filteredTurns);
    }

    private MemctlOutcome CreateNote(
        string vaultPath, string relPath,
        string date, string safeId,
        IReadOnlyList<(string Role, string Content)> turns)
    {
        var content = $"# Conversation {date} — {safeId}\n\n{FormatTurns(turns)}";
        var note = new Note
        {
            Id       = Guid.NewGuid().ToString("N")[..16],
            FilePath = relPath,
            Title    = $"Conversation {date} — {safeId}",
            Content  = content,
            Tags     = ["conversation"],
            Links    = [],
            Created  = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            Weight   = ConversationNoteWeight,
        };
        var emb    = embedding?.Embed($"{note.Title}\n{note.Content}");
        var stored = emb is not null ? note with { Embedding = emb } : note;

        vaultReader.WriteNote(stored, vaultPath, relPath);
        index.Upsert(stored);
        EventLog.Record(vaultPath, "operator_run", "info", "capture", $"{turns.Count} turns → {relPath}");
        DistillStateStore.Increment(vaultPath);

        return MemctlOutcome.Ok("capture", $"Created conversation note: {relPath}",
            new CaptureReport(false, relPath, turns.Count, ConversationNoteWeight));
    }

    private MemctlOutcome AppendNote(
        string vaultPath, string relPath, string absPath,
        IReadOnlyList<(string Role, string Content)> turns)
    {
        var existing = index.GetByFilePath(relPath)
                    ?? vaultReader.ParseNote(absPath, vaultPath) with { Weight = ConversationNoteWeight };

        var newText   = FormatTurns(turns);
        var separator = existing.Content.EndsWith('\n') ? "" : "\n";
        var combined  = existing.Content + separator + newText;
        var appended  = existing with { Content = combined, Modified = DateTime.UtcNow };
        var emb       = embedding?.Embed($"{appended.Title}\n{appended.Content}");
        var stored    = emb is not null ? appended with { Embedding = emb } : appended;

        vaultReader.WriteNote(stored, vaultPath, relPath);
        index.Upsert(stored);
        EventLog.Record(vaultPath, "operator_run", "info", "capture", $"appended {turns.Count} turns → {relPath}");

        return MemctlOutcome.Ok("capture", $"Appended to conversation note: {relPath}",
            new CaptureReport(false, relPath, turns.Count, null));
    }

    private static bool IsNoise(string content)
    {
        if (content.Trim().Length < MinTurnLength) return true;

        // Tool-call-only: pure JSON object or array
        var trimmed = content.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { JsonDocument.Parse(trimmed); return true; }
            catch { /* not pure JSON */ }
        }

        // Tool-call-only: code blocks are all that remains
        var withoutCode = CodeBlockPattern.Replace(content, "");
        return withoutCode.Trim().Length < MinTurnLength;
    }

    private static string FormatTurns(IReadOnlyList<(string Role, string Content)> turns)
    {
        var sb = new System.Text.StringBuilder();
        var ts = DateTime.UtcNow;
        foreach (var (role, content) in turns)
        {
            sb.AppendLine($"## Turn {ts:O}");
            sb.AppendLine($"**{role}:** {content}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string SanitizeConversationId(string id)
        => Regex.Replace(id, @"[^\w\-]", "_").Trim('_');

    private static string GenerateConversationId()
        => Guid.NewGuid().ToString("N")[..8];
}
