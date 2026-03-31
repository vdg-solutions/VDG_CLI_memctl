using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memctl.Implementations.Config;

public sealed class MemctlConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memctl", "config.json");

    private static readonly string ModelsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memctl", "models");

    [JsonPropertyName("default_model")]
    public string DefaultModel { get; init; } = "embeddinggemma-300m";

    public static MemctlConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new MemctlConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<MemctlConfig>(json) ?? new MemctlConfig();
        }
        catch
        {
            /* corrupted config — fall back to defaults */
            return new MemctlConfig();
        }
    }

    public static void Save(MemctlConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOpts));
    }

    public static string ResolveModelDir(string? overrideDir)
    {
        if (overrideDir is not null) return overrideDir;
        return Path.Combine(ModelsRoot, Load().DefaultModel);
    }

    public static IEnumerable<(string Name, bool Ready, int SizeMb, bool IsDefault)> ListModels()
    {
        var defaultModel = Load().DefaultModel;
        if (!Directory.Exists(ModelsRoot)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(ModelsRoot))
        {
            var name  = Path.GetFileName(dir);
            var ready = File.Exists(Path.Combine(dir, "model_quantized.onnx"))
                     && File.Exists(Path.Combine(dir, "model_quantized.onnx_data"));
            var sizeMb = ready
                ? (int)(Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length) / 1024 / 1024)
                : 0;
            yield return (name, ready, sizeMb, name == defaultModel);
        }
    }
}
