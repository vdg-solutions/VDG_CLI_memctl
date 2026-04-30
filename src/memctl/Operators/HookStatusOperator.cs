using Memctl.CoreAbstractions.Entities;

namespace Memctl.Operators;

public sealed class HookStatusOperator
{
    private const int RecentWindow = 100;

    public MemctlOutcome Execute(string vaultPath)
    {
        var path = HookLog.LogPath(vaultPath);
        if (!File.Exists(path))
        {
            return MemctlOutcome.Ok("hook-status", "No hook activity recorded yet",
                new HookStatus(path, false, 0, 0, null, null, []));
        }

        var lines  = File.ReadAllLines(path);
        var window = lines.Length > RecentWindow ? lines[^RecentWindow..] : lines;
        var parsed = window.Select(Parse).Where(e => e is not null).Cast<HookLogEntry>().ToList();

        var success = parsed.Count(e => e.Success);
        var failed  = parsed.Count(e => !e.Success);

        var lastFail = parsed.LastOrDefault(e => !e.Success);
        var msg = failed == 0
            ? $"Healthy — {success} successful invocations in last {parsed.Count}"
            : $"{failed} failures in last {parsed.Count} (last: {lastFail?.Error})";

        return MemctlOutcome.Ok("hook-status", msg,
            new HookStatus(
                LogPath:       path,
                LogExists:     true,
                RecentSuccess: success,
                RecentFail:    failed,
                LastError:     lastFail?.Error,
                LastErrorAt:   lastFail?.Timestamp,
                LastEntries:   parsed.TakeLast(10).ToList()));
    }

    private static HookLogEntry? Parse(string line)
    {
        var parts = line.Split('\t', 4);
        if (parts.Length < 3) return null;
        return new HookLogEntry(
            Timestamp: parts[0],
            Action:    parts[1],
            Success:   parts[2] == "ok",
            Error:     parts.Length >= 4 && parts[3].Length > 0 ? parts[3] : null);
    }
}
