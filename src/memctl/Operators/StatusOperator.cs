using Memctl.CoreAbstractions.Entities;
using Memctl.Implementations.Config;

namespace Memctl.Operators;

public sealed class StatusOperator
{
    private static readonly string DefaultModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memctl", "models", "embeddinggemma-300m");

    public MemctlOutcome Execute(string vaultPath)
        => Execute(vaultPath, explicitVault: vaultPath, searchPath: null);

    public MemctlOutcome Execute(string vaultPath, string? explicitVault, string? searchPath)
    {
        var modelReady = IsModelReady(out var modelPath, out var modelMb);
        var discovery  = VaultLocator.Discover(explicitVault, searchPath ?? Directory.GetCurrentDirectory());
        var resolved   = discovery.Vault ?? vaultPath;
        var dbPath     = IngestOperator.DbPath(resolved);
        var exists     = Directory.Exists(resolved);
        var indexed    = File.Exists(dbPath);
        var noteCount  = indexed ? CountNotes(dbPath) : 0;

        var hint = discovery.Vault is null
            ? "No vault found. Run 'memctl init --vault <path>' to create one, or cd to a folder containing .obsidian/."
            : !exists
                ? $"Vault path '{resolved}' does not exist on disk."
                : !indexed
                    ? "Vault exists but is not indexed. Run 'memctl ingest'."
                    : null;

        var msg = !modelReady
            ? "Model not downloaded"
            : discovery.Vault is null
                ? "No vault found"
                : "Ready";

        return MemctlOutcome.Ok("status", msg,
            new VaultStatus(
                ModelReady:     modelReady,
                ModelPath:      modelPath,
                ModelSizeMb:    modelMb,
                VaultExists:    exists,
                VaultIndexed:   indexed,
                NoteCount:      noteCount,
                IndexPath:      dbPath,
                VaultFound:     discovery.Vault is not null,
                SearchPath:     discovery.SearchPath,
                SearchStrategy: discovery.Strategy,
                CheckedPaths:   discovery.CheckedPaths,
                Hint:           hint));
    }

    internal static bool IsModelReady(out string modelPath, out int modelMb)
    {
        modelPath = DefaultModelDir;
        var onnx  = Path.Combine(DefaultModelDir, "model_quantized.onnx");
        var data  = Path.Combine(DefaultModelDir, "model_quantized.onnx_data");

        if (!File.Exists(onnx) || !File.Exists(data))
        {
            modelMb = 0;
            return false;
        }

        modelMb = (int)((new FileInfo(onnx).Length + new FileInfo(data).Length) / 1024 / 1024);
        return true;
    }

    private static int CountNotes(string dbPath)
    {
        try
        {
            using var db  = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM notes";
            return (int)(long)cmd.ExecuteScalar()!;
        }
        catch
        {
            return 0;
        }
    }
}
