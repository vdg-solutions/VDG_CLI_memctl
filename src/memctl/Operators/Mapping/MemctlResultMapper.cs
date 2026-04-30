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
            SearchHitsResult shr              => MapSearchHits(shr),
            SearchTagsHitsResult sthr         => MapSearchTagsHits(sthr),
            SearchLinksHitsResult slhr        => MapSearchLinksHits(slhr),
            IReadOnlyList<Note> notes         => MapNoteList(notes),
            IReadOnlyList<TagCount> tags      => MapTagList(tags),
            IReadOnlyList<GrepHit> grep       => MapGrepList(grep),
            _                                 => outcome.Data,
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
        Id       = n.Id,
        File     = n.FilePath,
        Title    = n.Title,
        Content  = n.Content,
        Tags     = n.Tags,
        Links    = n.Links,
        Created  = n.Created  == default ? null : n.Created.ToString("O"),
        Modified = n.Modified == default ? null : n.Modified.ToString("O"),
        Score    = score,
    };

    public static StatsDto MapStats(VaultStats s) => new()
    {
        NoteCount  = s.NoteCount,
        TagCount   = s.TagCount,
        LinkCount  = s.LinkCount,
        IndexBytes = s.IndexBytes,
        VaultPath  = s.VaultPath,
    };

    public static SearchResultDto MapSearchHits(SearchHitsResult shr) => new()
    {
        Query   = shr.Query,
        Count   = shr.Hits.Count,
        Results = shr.Hits.Select(h => MapNoteWithSnippet(h.Note, h.Score, h.Snippet)).ToArray(),
    };

    private static NoteDto MapNoteWithSnippet(Note n, float? score, string? snippet) => new()
    {
        Id       = n.Id,
        File     = n.FilePath,
        Title    = n.Title,
        Content  = n.Content,
        Tags     = n.Tags,
        Links    = n.Links,
        Created  = n.Created  == default ? null : n.Created.ToString("O"),
        Modified = n.Modified == default ? null : n.Modified.ToString("O"),
        Score    = score,
        Snippet  = snippet,
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

    public static GrepListResultDto MapGrepList(IReadOnlyList<GrepHit> hits) => new()
    {
        Count = hits.Count,
        Hits  = hits.Select(h => new GrepHitDto
        {
            File    = h.FilePath,
            Line    = h.LineNumber,
            Content = h.Content,
        }).ToArray(),
    };
}
