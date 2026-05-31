using System.Security.Cryptography;
using System.Text;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

// #595 — raw move primitive for the memory cascade pyramid. Lifts a note from one vault
// (source layer, e.g. project cwd or per-agent instance) into another vault (target layer,
// e.g. agent instance or machine shared) without going through the voting cycle. Used by
// the maintenance scheduler for the auto Layer 1 -> Layer 2 path (no consensus needed,
// see effective-memory-organization wiki) and as the move backend for any caller that
// wants to elevate by hand.
//
// Atomicity: target file is written first; only after the target write succeeds is the
// source note marked archived. A failed target write leaves the source untouched so the
// caller can retry. Slug collisions in the target vault get a deterministic `-elevated-<8hex>`
// suffix derived from the source vault path so repeated elevation of the same note from the
// same source converges to the same filename.
public sealed class ElevateOperator(IVaultReader vaultReader, INoteIndex sourceIndex)
{
    public MemctlOutcome Execute(string sourceVaultPath, string targetVaultPath, string noteId)
    {
        if (string.IsNullOrWhiteSpace(sourceVaultPath))
            return MemctlOutcome.Fail("elevate", "sourceVaultPath is required");
        if (string.IsNullOrWhiteSpace(targetVaultPath))
            return MemctlOutcome.Fail("elevate", "targetVaultPath is required");
        if (string.IsNullOrWhiteSpace(noteId))
            return MemctlOutcome.Fail("elevate", "noteId is required");
        if (PathsEqual(sourceVaultPath, targetVaultPath))
            return MemctlOutcome.Fail("elevate", "source and target vault are the same");

        sourceIndex.Initialize(IngestOperator.DbPath(sourceVaultPath));
        var note = sourceIndex.GetById(noteId);
        if (note is null)
            return MemctlOutcome.Fail("elevate", $"note '{noteId}' not found in source vault");
        if (note.Archived)
            return MemctlOutcome.Fail("elevate", $"note '{noteId}' is already archived (probably already elevated)");

        // Ensure target vault has the .memctl structure (idempotent).
        vaultReader.InitVaultStructure(targetVaultPath);
        var targetVaultRoot = ResolveVaultRoot(targetVaultPath);

        // File name in target vault: keep the source leaf if no collision, else append -elevated-<8hex>.
        var sourceLeaf = Path.GetFileName(note.FilePath);
        if (string.IsNullOrEmpty(sourceLeaf))
            sourceLeaf = SanitizeFileNameOrDefault(note.Title, fallback: noteId) + ".md";

        var targetFileName = sourceLeaf;
        var targetAbsPath  = Path.Combine(targetVaultRoot, targetFileName);
        if (File.Exists(targetAbsPath))
        {
            var collisionTag = ShortHash(sourceVaultPath);
            var stem         = Path.GetFileNameWithoutExtension(sourceLeaf);
            var ext          = Path.GetExtension(sourceLeaf);
            targetFileName   = $"{stem}-elevated-{collisionTag}{ext}";
            targetAbsPath    = Path.Combine(targetVaultRoot, targetFileName);
        }

        // Build the elevated note. Re-stamp Modified so the target vault treats this as fresh.
        // Source provenance recorded in note body header so the elevation is auditable from the
        // file alone (frontmatter doesn't carry free-form keys).
        var elevatedBody = AppendElevationHeader(note.Content, sourceVaultPath, note.FilePath);
        var elevated = note with
        {
            FilePath = targetFileName,
            Modified = DateTime.UtcNow,
            Content  = elevatedBody,
            Archived = false,
        };

        try
        {
            vaultReader.WriteNote(elevated, targetVaultRoot, targetFileName);
        }
        catch (Exception ex)
        {
            // Target write failed — leave source untouched, surface error to caller.
            return MemctlOutcome.Fail("elevate", $"failed to write elevated note to '{targetAbsPath}': {ex.Message}");
        }

        // Source side: mark archived. SQLite update only — physical .md file kept as audit trail
        // (the maintenance scheduler will eventually move archived files to <vault>/archive/ during
        // its decay sweep, see DecayOperator).
        sourceIndex.ApplyDecay(noteId, newWeight: note.Weight, archived: true);

        return MemctlOutcome.Ok("elevate",
            $"elevated '{noteId}' from '{sourceVaultPath}' -> '{targetAbsPath}'");
    }

    private static string AppendElevationHeader(string body, string sourceVault, string sourceRelPath)
    {
        var header = $"<!-- elevated_from: {sourceVault}/{sourceRelPath} at {DateTime.UtcNow:O} -->\n\n";
        return header + body;
    }

    private static string ResolveVaultRoot(string vaultPath)
    {
        var trimmed  = vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isDirect = Path.GetFileName(trimmed).Equals(".memctl", StringComparison.OrdinalIgnoreCase);
        return isDirect ? trimmed : Path.Combine(trimmed, ".memctl");
    }

    private static string ShortHash(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        var sb    = new StringBuilder(8);
        for (var i = 0; i < 4; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    private static string SanitizeFileNameOrDefault(string raw, string fallback)
    {
        var trimmed = raw?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed)) return fallback;
        var safe = new StringBuilder();
        foreach (var c in trimmed.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) safe.Append(c);
            else if (c is ' ' or '-' or '_') safe.Append('-');
        }
        var result = safe.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    private static bool PathsEqual(string a, string b) =>
        Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
