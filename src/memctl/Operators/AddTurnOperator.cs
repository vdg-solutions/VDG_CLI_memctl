using System.Text;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed class AddTurnOperator(IVaultReader vault, INoteIndex index, GemmaEmbeddingEngine? embedding)
{
    public MemctlOutcome Execute(
        string   vaultPath,
        long     chatId,
        long     userId,
        string   from,
        string   role,
        string   text,
        DateTime? timestamp = null,
        bool     writeOnly  = false)
    {
        // Auto-init vault on first use (idempotent — WriteIfAbsent guards all files)
        vault.InitVaultStructure(vaultPath);

        var ts       = (timestamp ?? DateTime.UtcNow).ToLocalTime();
        var date     = DateOnly.FromDateTime(ts);
        var time     = ts.ToString("HH:mm");
        var chatDir  = Path.Combine(vaultPath, "chats", chatId.ToString());
        var filePath = Path.Combine(chatDir, $"{date:yyyy-MM-dd}.md");

        Directory.CreateDirectory(chatDir);

        // Create file with frontmatter on first write of the day
        if (!File.Exists(filePath))
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"id: {chatId}-{date:yyyy-MM-dd}");
            sb.AppendLine($"title: Chat {chatId} — {date:yyyy-MM-dd}");
            sb.AppendLine($"date: {date:yyyy-MM-dd}");
            sb.AppendLine($"created: {date:yyyy-MM-dd}");
            sb.AppendLine("tags:");
            sb.AppendLine("  - telegram");
            if (userId != 0) sb.AppendLine($"  - user-{userId}");
            sb.AppendLine($"  - chat-{chatId}");
            sb.AppendLine("---");
            sb.AppendLine();
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
        }

        // Append turn
        var line = role == "assistant"
            ? $"**{time} Assistant:** {text}{Environment.NewLine}{Environment.NewLine}"
            : $"**{time} {from}:** {text}{Environment.NewLine}{Environment.NewLine}";

        File.AppendAllText(filePath, line, new UTF8Encoding(false));

        if (writeOnly)
            return MemctlOutcome.Ok("add-turn", $"Logged {role} turn", new { chatId, date = date.ToString("yyyy-MM-dd"), role });

        // Re-parse and re-index only this file
        if (embedding == null)
            return MemctlOutcome.Fail("add-turn", "Embedding engine required for indexing");

        var dbPath = IngestOperator.DbPath(vaultPath);
        index.Initialize(dbPath);

        var note      = vault.ParseNote(filePath, vaultPath);
        var emb       = embedding.Embed($"{note.Title}\n{note.Content}");
        var withEmbed = note with { Embedding = emb };
        index.Upsert(withEmbed);

        return MemctlOutcome.Ok("add-turn", $"Logged and indexed {role} turn", new { chatId, date = date.ToString("yyyy-MM-dd"), role });
    }
}
