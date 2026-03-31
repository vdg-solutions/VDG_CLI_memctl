using Memctl.CoreAbstractions.Entities;

namespace Memctl.Operators;

public sealed class StatusOperator
{
    private static readonly string DefaultModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memctl", "models", "embeddinggemma-300m");

    public MemctlOutcome Execute(string vaultPath)
    {
        var modelReady  = IsModelReady(out var modelPath, out var modelMb);
        var dbPath      = IngestOperator.DbPath(vaultPath);
        var vaultExists = Directory.Exists(vaultPath);
        var indexed     = File.Exists(dbPath);
        var noteCount   = indexed ? CountNotes(dbPath) : 0;

        return MemctlOutcome.Ok("status", modelReady ? "Ready" : "Model not downloaded", new
        {
            model_ready   = modelReady,
            model_path    = modelPath,
            model_size_mb = modelMb,
            vault_exists  = vaultExists,
            vault_indexed = indexed,
            note_count    = noteCount,
            index_path    = dbPath,
        });
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
