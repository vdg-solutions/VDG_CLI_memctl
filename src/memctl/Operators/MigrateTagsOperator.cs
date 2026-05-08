using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class MigrateTagsOperator(IVaultReader vault, INoteIndex index)
{
    public MemctlOutcome Execute(
        string                                   vaultPath,
        IReadOnlyDictionary<string, string>      replaceExact,
        IReadOnlyDictionary<string, string>      replacePrefix,
        IReadOnlyList<string>                    removeExact,
        IReadOnlyList<string>                    removePrefix,
        bool                                     dryRun)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vault, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var files          = vault.EnumerateMarkdownFiles(vaultPath).ToList();
        var notesScanned   = 0;
        var notesModified  = 0;
        var tagsReplaced   = 0;
        var tagsRemoved    = 0;
        var removedSet     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replaceApplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            notesScanned++;
            Note note;
            try { note = vault.ParseNote(file, vaultPath); }
            catch { continue; }

            var newTags = new List<string>(note.Tags.Length);
            var changed = false;

            foreach (var tag in note.Tags)
            {
                if (replaceExact.TryGetValue(tag, out var replacedExact))
                {
                    newTags.Add(replacedExact);
                    replaceApplied[tag] = replacedExact;
                    tagsReplaced++;
                    changed = true;
                    continue;
                }

                var matchedPrefix = false;
                foreach (var (oldPrefix, newPrefix) in replacePrefix)
                {
                    if (tag.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var replaced = newPrefix + tag[oldPrefix.Length..];
                        newTags.Add(replaced);
                        replaceApplied[tag] = replaced;
                        tagsReplaced++;
                        matchedPrefix = true;
                        changed = true;
                        break;
                    }
                }
                if (matchedPrefix) continue;

                if (removeExact.Any(r => string.Equals(r, tag, StringComparison.OrdinalIgnoreCase))
                    || removePrefix.Any(p => tag.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    removedSet.Add(tag);
                    tagsRemoved++;
                    changed = true;
                    continue;
                }

                newTags.Add(tag);
            }

            if (!changed) continue;
            notesModified++;

            if (!dryRun)
            {
                vault.UpdateFrontmatter(file, [.. newTags], note.Links);
                var reparsed = vault.ParseNote(file, vaultPath);
                index.Upsert(reparsed);
            }
        }

        var report = new MigrateTagsReport(
            DryRun:        dryRun,
            NotesScanned:  notesScanned,
            NotesModified: notesModified,
            TagsReplaced:  tagsReplaced,
            TagsRemoved:   tagsRemoved,
            RemovedTags:   removedSet.OrderBy(s => s).ToList(),
            ReplaceMap:    replaceApplied);

        var msg = dryRun
            ? $"Dry run: would modify {notesModified}/{notesScanned} notes"
            : $"Migrated tags in {notesModified}/{notesScanned} notes";
        if (!dryRun)
            EventLog.Record(vaultPath, "operator_run", "info", "migrate-tags",
                $"Migrated {notesModified}/{notesScanned} notes, {tagsReplaced} replaced, {tagsRemoved} removed");
        return MemctlOutcome.Ok("migrate-tags", msg, report);
    }
}
