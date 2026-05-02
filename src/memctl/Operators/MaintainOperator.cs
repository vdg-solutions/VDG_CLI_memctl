using System.Globalization;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;
using Memctl.Implementations.Embedding;

namespace Memctl.Operators;

public sealed record MaintainOptions(bool Force, bool DryRun);

public sealed class MaintainOperator(
    IVaultReader vaultReader,
    INoteIndex index,
    GemmaEmbeddingEngine? embedding)
{
    private const string LastMaintainKey   = "last_maintain_run";
    private const string LastDecayKey      = "last_decay_date";
    private const int    ThrottleSeconds   = 60;
    private const int    DecayOverdueDays  = 30;

    public MemctlOutcome Execute(string vaultPath, MaintainOptions opts)
    {
        if (!Directory.Exists(vaultPath))
            return MemctlOutcome.Fail("maintain", $"Vault not found: {vaultPath}");

        var dbPath = IngestOperator.DbPath(vaultPath);
        if (!File.Exists(dbPath))
        {
            // No index yet → ingest is the only sensible action
            if (opts.DryRun)
                return MemctlOutcome.Ok("maintain", "dry-run: index missing → ingest planned",
                    new MaintainResult(["ingest (planned)"], [], null, false, true));

            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);
            index.Initialize(dbPath);
            index.SetMetadata(LastMaintainKey, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            return MemctlOutcome.Ok("maintain", "Ingested fresh vault",
                new MaintainResult(["ingest"], [], null, false, false));
        }

        index.Initialize(dbPath);

        // Throttle check
        if (!opts.Force && IsThrottled(out var throttleReason))
        {
            return MemctlOutcome.Ok("maintain", "Throttled — skipped",
                new MaintainResult([], [], throttleReason, true, opts.DryRun ? true : null));
        }

        var actions = new List<string>();
        var skipped = new List<string>();

        // 1. Ingest — file mtimes vs db mtime
        if (IngestOperator.NeedsIngest(vaultPath))
        {
            if (opts.DryRun) actions.Add("ingest (planned)");
            else { new IngestOperator(vaultReader, index, embedding).Execute(vaultPath); actions.Add("ingest"); index.Initialize(dbPath); }
        }
        else skipped.Add("ingest (vault unchanged)");

        // 2. Decay — last_decay_date != today
        if (IsDecayOverdue())
        {
            if (opts.DryRun) actions.Add("decay (planned)");
            else
            {
                _ = new DecayOperator(vaultReader, index).Execute(vaultPath, DecayOverdueDays, 0.5f, false);
                actions.Add("decay");
            }
        }
        else skipped.Add("decay (recent)");

        // Stamp throttle (skip on dry-run)
        if (!opts.DryRun)
            index.SetMetadata(LastMaintainKey, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        var msg = actions.Count == 0
            ? "Nothing due — vault healthy"
            : $"{actions.Count} action(s) {(opts.DryRun ? "planned" : "applied")}";

        return MemctlOutcome.Ok("maintain", msg,
            new MaintainResult(actions.ToArray(), skipped.ToArray(),
                actions.Count == 0 ? "nothing_due" : null,
                false, opts.DryRun ? true : null));
    }

    private bool IsThrottled(out string? reason)
    {
        reason = null;
        var last = index.GetMetadata(LastMaintainKey);
        if (string.IsNullOrEmpty(last)) return false;
        if (!DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastDt))
            return false;
        var elapsed = (DateTime.UtcNow - lastDt).TotalSeconds;
        if (elapsed < ThrottleSeconds)
        {
            reason = $"throttled ({(int)elapsed}s ago, limit {ThrottleSeconds}s)";
            return true;
        }
        return false;
    }

    private bool IsDecayOverdue()
    {
        var last = index.GetMetadata(LastDecayKey);
        if (string.IsNullOrEmpty(last)) return true;
        var todayStr = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        return last != todayStr;
    }
}

public sealed record MaintainResult(
    string[] Actions,
    string[] Skipped,
    string?  SkippedReason,
    bool     Throttled,
    bool?    DryRun);
