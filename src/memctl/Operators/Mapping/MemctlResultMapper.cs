using System.Collections.Generic;
using System.Linq;
using Memctl.Boundary;
using Memctl.CoreAbstractions.Entities;

namespace Memctl.Operators.Mapping;

public static class MemctlResultMapper
{
    public static MemctlResult ToResult(MemctlOutcome outcome)
    {
        var data = outcome.Data switch
        {
            null                              => null,
            Note note                         => (object)MapNote(note),
            VaultStats stats                  => MapStats(stats),
            VaultStatus status                => MapStatus(status),
            VaultRef vref                     => MapVaultRef(vref),
            HookStatus hs                     => MapHookStatus(hs),
            WeightChange wc                   => MapWeight(wc),
            DecayReport dr                    => MapDecay(dr),
            CaptureReport cr                  => MapCapture(cr),
            IngestReport ir                   => MapIngest(ir),
            OrganizeReport or                 => MapOrganize(or),
            ModelInfo mi                      => MapModelInfo(mi),
            ModelList ml                      => MapModelList(ml),
            ModelSelection ms                 => MapModelSelection(ms),
            LintReport lr                     => MapLint(lr),
            SearchHitsResult shr              => MapSearchHits(shr),
            SearchTagsHitsResult sthr         => MapSearchTagsHits(sthr),
            SearchLinksHitsResult slhr        => MapSearchLinksHits(slhr),
            SearchDateHitsResult sdhr         => MapSearchDateHits(sdhr),
            GrepResult gres                   => MapGrepResult(gres),
            IReadOnlyList<Note> notes         => MapNoteList(notes),
            IReadOnlyList<TagCount> tags      => MapTagList(tags),
            IReadOnlyList<GrepHit> grep       => MapGrepList(grep, null),
            string raw                        => raw,
            _                                 => throw new System.InvalidOperationException(
                $"MemctlResultMapper: unsupported Data type '{outcome.Data.GetType().FullName}' " +
                $"for action '{outcome.Action}'. Add a switch branch or refactor the Operator to use a known Entity."),
        };

        return new MemctlResult
        {
            Success = outcome.Success,
            Action  = outcome.Action,
            Message = outcome.Message,
            Data    = data,
        };
    }

    public static NoteDto MapNote(Note n, float? score = null) => new()
    {
        Id          = n.Id,
        File        = n.FilePath,
        Title       = n.Title,
        Content     = n.Content,
        Tags        = n.Tags,
        Links       = n.Links,
        Created     = n.Created  == default ? null : n.Created.ToString("O"),
        Modified    = n.Modified == default ? null : n.Modified.ToString("O"),
        Weight      = (float)System.Math.Round(n.Weight, 2),
        AccessCount = n.AccessCount,
        Score       = score,
    };

    public static StatsDto MapStats(VaultStats s) => new()
    {
        NoteCount  = s.NoteCount,
        TagCount   = s.TagCount,
        LinkCount  = s.LinkCount,
        IndexBytes = s.IndexBytes,
        VaultPath  = s.VaultPath,
    };

    public static VaultStatusDto MapStatus(VaultStatus s) => new()
    {
        ModelReady   = s.ModelReady,
        ModelPath    = s.ModelPath,
        ModelSizeMb  = s.ModelSizeMb,
        VaultExists  = s.VaultExists,
        VaultIndexed = s.VaultIndexed,
        NoteCount    = s.NoteCount,
        IndexPath    = s.IndexPath,
    };

    public static VaultRefDto MapVaultRef(VaultRef v) => new() { Vault = v.Vault };

    public static HookStatusDto MapHookStatus(HookStatus s) => new()
    {
        LogPath       = s.LogPath,
        LogExists     = s.LogExists,
        RecentSuccess = s.RecentSuccess,
        RecentFail    = s.RecentFail,
        LastError     = s.LastError,
        LastErrorAt   = s.LastErrorAt,
        LastEntries   = s.LastEntries.Select(e => new HookLogEntryDto
        {
            Timestamp = e.Timestamp,
            Action    = e.Action,
            Success   = e.Success,
            Error     = e.Error,
        }).ToArray(),
    };

    public static WeightChangeDto MapWeight(WeightChange w) => new()
    {
        Id     = w.Id,
        File   = w.FilePath,
        Weight = w.Weight,
    };

    public static DecayReportDto MapDecay(DecayReport d) => new()
    {
        Decayed         = d.Decayed,
        Archived        = d.Archived,
        Unchanged       = d.Unchanged,
        AlreadyArchived = d.AlreadyArchived,
        AlreadyRunToday = d.AlreadyRunToday,
        DryRun          = d.DryRun,
    };

    public static CaptureReportDto MapCapture(CaptureReport c) => new()
    {
        DryRun = c.DryRun,
        File   = c.FilePath,
        Turns  = c.Turns,
        Weight = c.Weight,
    };

    public static IngestReportDto MapIngest(IngestReport i) => new()
    {
        Indexed          = i.Indexed,
        Total            = i.Total,
        Vault            = i.Vault,
        Model            = i.Model,
        SemanticLintHint = i.SemanticLintHint,
    };

    public static OrganizeReportDto MapOrganize(OrganizeReport o) => new()
    {
        Updated = o.Updated,
        Errors  = o.Errors,
        Vault   = o.Vault,
    };

    public static ModelInfoDto MapModelInfo(ModelInfo m) => new()
    {
        ModelPath   = m.ModelPath,
        ModelSizeMb = m.ModelSizeMb,
    };

    public static ModelListDto MapModelList(ModelList m) => new()
    {
        DefaultModel = m.DefaultModel,
        Models       = m.Models.Select(e => new ModelEntryDto
        {
            Name      = e.Name,
            Ready     = e.Ready,
            SizeMb    = e.SizeMb,
            IsDefault = e.IsDefault,
        }).ToArray(),
    };

    public static ModelSelectionDto MapModelSelection(ModelSelection m) => new() { Model = m.Model };

    public static LintReportDto MapLint(LintReport l) => new()
    {
        Structural = MapLintStructural(l.Structural),
        Semantic   = l.Semantic,
    };

    public static LintStructuralDto MapLintStructural(LintStructural s) => new()
    {
        Orphans     = s.Orphans.Select(o => new LintOrphanDto
        {
            Id       = o.Id,
            Title    = o.Title,
            FilePath = o.FilePath,
        }).ToArray(),
        BrokenLinks = s.BrokenLinks.Select(b => new LintBrokenLinkDto
        {
            NoteId     = b.NoteId,
            NoteTitle  = b.NoteTitle,
            BrokenLink = b.BrokenLink,
        }).ToArray(),
        Duplicates  = s.Duplicates.Select(d => new LintDuplicateDto
        {
            NoteAId    = d.NoteAId,
            NoteATitle = d.NoteATitle,
            NoteBId    = d.NoteBId,
            NoteBTitle = d.NoteBTitle,
            Similarity = d.Similarity,
        }).ToArray(),
        DecayRisk   = s.DecayRisk.Select(r => new LintDecayRiskDto
        {
            Id                = r.Id,
            Title             = r.Title,
            Weight            = r.Weight,
            DaysSinceModified = r.DaysSinceModified,
            InboundLinkCount  = r.InboundLinkCount,
        }).ToArray(),
    };

    public static SearchResultDto MapSearchHits(SearchHitsResult shr) => new()
    {
        Query   = shr.Query,
        Count   = shr.Hits.Count,
        Results = shr.Hits.Select(h => MapNoteWithSnippet(h.Note, h.Score, h.Snippet)).ToArray(),
    };

    private static NoteDto MapNoteWithSnippet(Note n, float? score, string? snippet) => new()
    {
        Id          = n.Id,
        File        = n.FilePath,
        Title       = n.Title,
        Content     = n.Content,
        Tags        = n.Tags,
        Links       = n.Links,
        Created     = n.Created  == default ? null : n.Created.ToString("O"),
        Modified    = n.Modified == default ? null : n.Modified.ToString("O"),
        Weight      = (float)System.Math.Round(n.Weight, 2),
        AccessCount = n.AccessCount,
        Score       = score,
        Snippet     = snippet,
    };

    public static SearchTagsResultDto MapSearchTagsHits(SearchTagsHitsResult sthr) => new()
    {
        Tags     = sthr.Tags,
        MatchAll = sthr.MatchAll,
        Count    = sthr.Notes.Count,
        Results  = sthr.Notes.Select(n => MapNote(n)).ToArray(),
    };

    public static SearchLinksResultDto MapSearchLinksHits(SearchLinksHitsResult slhr) => new()
    {
        SourceId = slhr.SourceId,
        Depth    = slhr.Depth,
        Count    = slhr.Notes.Count,
        Results  = slhr.Notes.Select(n => MapNote(n)).ToArray(),
    };

    public static SearchDateResultDto MapSearchDateHits(SearchDateHitsResult sdhr) => new()
    {
        From    = sdhr.From,
        To      = sdhr.To,
        Count   = sdhr.Notes.Count,
        Results = sdhr.Notes.Select(n => MapNote(n)).ToArray(),
    };

    public static NoteListResultDto MapNoteList(IReadOnlyList<Note> notes) => new()
    {
        Count = notes.Count,
        Notes = notes.Select(n => MapNote(n)).ToArray(),
    };

    public static TagsListResultDto MapTagList(IReadOnlyList<TagCount> tags) => new()
    {
        Count = tags.Count,
        Tags  = tags.Select(t => new TagDto { Tag = t.Tag, Count = t.Count }).ToArray(),
    };

    public static GrepListResultDto MapGrepList(IReadOnlyList<GrepHit> hits, string? pattern) => new()
    {
        Pattern = pattern,
        Count   = hits.Count,
        Hits    = hits.Select(h => new GrepHitDto
        {
            File    = h.FilePath,
            Line    = h.LineNumber,
            Content = h.Content,
        }).ToArray(),
    };

    public static GrepListResultDto MapGrepResult(GrepResult g) => MapGrepList(g.Hits, g.Pattern);
}
