using System.Text;

namespace Memctl.Operators;

internal static class EventLog
{
    internal static void Record(
        string  vaultPath,
        string  type,
        string  severity,
        string  source,
        string  payload,
        string? conversationId = null)
    {
        try
        {
            var ts      = DateTime.UtcNow;
            var id      = Guid.NewGuid().ToString("N")[..16];
            var relPath = $"events/{ts:yyyy-MM-dd}-{source}-{id[..6]}.md";
            var absPath = Path.Combine(vaultPath, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"id: {id}");
            sb.AppendLine($"type: {type}");
            sb.AppendLine($"severity: {severity}");
            sb.AppendLine($"source: {source}");
            sb.AppendLine($"payload: \"{payload.Replace("\"", "'")}\"");
            sb.AppendLine($"timestamp: {ts:O}");
            if (conversationId is not null)
                sb.AppendLine($"conversation_id: {conversationId}");
            sb.AppendLine("tags:");
            sb.AppendLine("  - event");
            sb.AppendLine($"  - {severity}");
            sb.AppendLine("archived: true");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append($"{source} {severity} — {payload}");

            File.WriteAllText(absPath, sb.ToString(), Encoding.UTF8);
            // Intentionally NOT calling index.Upsert — events are disk-only until next ingest.
            // HookLog.Record still called for backward compat (hook-status reads it).
        }
        catch { /* best-effort; never crash callers */ }
    }
}
