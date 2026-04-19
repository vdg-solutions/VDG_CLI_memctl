using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class WeightOperator(IVaultReader vaultReader, INoteIndex index)
{
    public MemctlOutcome Execute(string vaultPath, string idOrPath, string rawValue)
    {
        if (!float.TryParse(rawValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return MemctlOutcome.Fail("weight", $"Invalid weight value: '{rawValue}' — must be a number");

        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var note = index.GetById(idOrPath) ?? index.GetByFilePath(idOrPath);
        if (note is null)
            return MemctlOutcome.Fail("weight", $"Note not found: {idOrPath}");

        var clamped = Math.Clamp(parsed, 0.0f, 2.0f);
        index.SetWeight(note.Id, clamped);

        return MemctlOutcome.Ok("weight", $"Weight set to {(float)Math.Round(clamped, 2)}", new
        {
            id     = note.Id,
            file   = note.FilePath,
            weight = (float)Math.Round(clamped, 2),
        });
    }
}
