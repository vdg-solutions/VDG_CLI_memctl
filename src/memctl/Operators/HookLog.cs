using System.Globalization;

namespace Memctl.Operators;

internal static class HookLog
{
    private const int MaxLines = 1000;

    internal static string LogPath(string vaultPath) =>
        Path.Combine(vaultPath, ".memctl", "hook.log");

    internal static void Record(string vaultPath, string action, bool success, string? error)
    {
        try
        {
            var dir = Path.Combine(vaultPath, ".memctl");
            Directory.CreateDirectory(dir);
            var path = LogPath(vaultPath);
            var line = $"{DateTime.UtcNow:O}\t{action}\t{(success ? "ok" : "fail")}\t{error?.Replace('\t', ' ').Replace('\n', ' ') ?? ""}";
            File.AppendAllLines(path, [line]);
            RotateIfNeeded(path);
        }
        catch { /* hook never crash — even logging is best-effort */ }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length > MaxLines)
                File.WriteAllLines(path, lines[^MaxLines..]);
        }
        catch { /* tolerated */ }
    }
}
