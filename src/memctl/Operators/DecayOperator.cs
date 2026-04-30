using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class DecayOperator(IVaultReader vaultReader, INoteIndex index)
{
    private const float  ArchiveThreshold  = 0.05f;
    private const float  ProtectedDecayExp = 1.0f / 3.0f;
    private const string LastDecayDateKey  = "last_decay_date";

    public MemctlOutcome Execute(string vaultPath, int days, float decayFactor, bool dryRun)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        // idempotency: skip if already ran today (non-dry-run only)
        var todayStr = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        if (!dryRun && index.GetMetadata(LastDecayDateKey) == todayStr)
        {
            return MemctlOutcome.Ok("decay", "Already ran today — skipped",
                new DecayReport(0, 0, 0, 0, true, null));
        }

        var now   = DateTime.UtcNow;
        var notes = index.GetAll(includeArchived: true);

        var updates         = new List<(string NoteId, float NewWeight, bool Archived)>();
        int decayed         = 0;
        int newlyArchived   = 0;
        int unchanged       = 0;
        int alreadyArchived = 0;

        foreach (var note in notes)
        {
            // already archived before this run
            if (note.Archived) { alreadyArchived++; continue; }

            // recency guards — skip if modified or weight-set within --days
            var daysSinceModified = (now - note.Modified).TotalDays;
            if (daysSinceModified <= days) { unchanged++; continue; }

            if (note.LastWeightSet.HasValue)
            {
                var daysSinceWeightSet = (now - note.LastWeightSet.Value).TotalDays;
                if (daysSinceWeightSet <= days) { unchanged++; continue; }
            }

            // floor guard — already at zero
            if (note.Weight <= 0.0f) { unchanged++; continue; }

            // compute new weight
            float newWeight;
            if (note.Weight > 1.0f)
                newWeight = note.Weight * MathF.Pow(decayFactor, ProtectedDecayExp);
            else
                newWeight = note.Weight * decayFactor;

            var willArchive = newWeight < ArchiveThreshold;
            if (willArchive) newWeight = 0.0f;

            updates.Add((note.Id, newWeight, willArchive));
            decayed++;
            if (willArchive) newlyArchived++;
        }

        if (!dryRun)
        {
            if (updates.Count > 0)
                index.ApplyDecayBatch(updates);
            index.SetMetadata(LastDecayDateKey, todayStr);
        }

        return MemctlOutcome.Ok("decay", $"Decayed {decayed} notes, archived {newlyArchived}",
            new DecayReport(decayed, newlyArchived, unchanged, alreadyArchived, null, dryRun));
    }
}
