using System.Collections.Generic;

namespace Memctl.CoreAbstractions.Entities;

public sealed record MigrateTagsReport(
    bool                   DryRun,
    int                    NotesScanned,
    int                    NotesModified,
    int                    TagsReplaced,
    int                    TagsRemoved,
    IReadOnlyList<string>  RemovedTags,
    IReadOnlyDictionary<string, string> ReplaceMap);
