using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memctl.Operators;

internal static class DistillStateStore
{
    private const int DefaultThreshold = 5;

    private static string StateFilePath(string vaultPath) =>
        Path.Combine(vaultPath, ".obsidian", "memctl", "distill-state.json");

    internal static void Increment(string vaultPath)
    {
        try
        {
            var (count, threshold, lastAt) = ReadState(vaultPath);
            WriteState(vaultPath, count + 1, threshold, lastAt);
        }
        catch { /* best-effort; never crash callers */ }
    }

    internal static void Reset(string vaultPath)
    {
        try
        {
            var (_, threshold, _) = ReadState(vaultPath);
            WriteState(vaultPath, 0, threshold, DateTime.UtcNow);
        }
        catch { /* best-effort; never crash callers */ }
    }

    internal static bool ShouldRecommend(string vaultPath)
    {
        try
        {
            var (count, threshold, _) = ReadState(vaultPath);
            return count >= threshold;
        }
        catch { /* best-effort; never crash callers */ }
        return false;
    }

    internal static (int count, int threshold, DateTime? lastAt) GetState(string vaultPath)
    {
        try { return ReadState(vaultPath); }
        catch { /* best-effort; never crash callers */ }
        return (0, DefaultThreshold, null);
    }

    private static (int count, int threshold, DateTime? lastAt) ReadState(string vaultPath)
    {
        var path = StateFilePath(vaultPath);
        if (!File.Exists(path))
            return (0, DefaultThreshold, null);

        var json  = File.ReadAllText(path, Encoding.UTF8);
        var state = JsonSerializer.Deserialize(json, DistillStateJsonContext.Default.DistillState);
        if (state is null)
            return (0, DefaultThreshold, null);

        var threshold = state.Threshold > 0 ? state.Threshold : DefaultThreshold;
        return (state.ConversationsSinceDistill, threshold, state.LastDistillAt);
    }

    private static void WriteState(string vaultPath, int count, int threshold, DateTime? lastAt)
    {
        var dest  = StateFilePath(vaultPath);
        var temp  = dest + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var state = new DistillState
        {
            ConversationsSinceDistill = count,
            Threshold                 = threshold,
            LastDistillAt             = lastAt,
        };
        var json = JsonSerializer.Serialize(state, DistillStateJsonContext.Default.DistillState);
        File.WriteAllText(temp, json, Encoding.UTF8);
        File.Move(temp, dest, overwrite: true);
    }

    internal static void SetThreshold(string vaultPath, int threshold)
    {
        try
        {
            var (count, _, lastAt) = ReadState(vaultPath);
            WriteState(vaultPath, count, threshold, lastAt);
        }
        catch { /* best-effort; never crash callers */ }
    }
}

internal sealed class DistillState
{
    [JsonPropertyName("conversations_since_distill")]
    public int ConversationsSinceDistill { get; set; }

    [JsonPropertyName("threshold")]
    public int Threshold { get; set; } = 5;

    [JsonPropertyName("last_distill_at")]
    public DateTime? LastDistillAt { get; set; }
}

[JsonSerializable(typeof(DistillState))]
internal sealed partial class DistillStateJsonContext : JsonSerializerContext { }
