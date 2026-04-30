using System.Collections.Generic;
using Memctl.Boundary;
using Memctl.CoreAbstractions.Entities;
using Memctl.Operators.Mapping;
using Xunit;

namespace Memctl.Tests.Mapping;

public class MemctlResultMapperTests
{
    [Fact]
    public void Null_Data_Maps_To_Null()
    {
        var outcome = MemctlOutcome.Ok("test", "msg");
        var result  = MemctlResultMapper.ToResult(outcome);

        Assert.True(result.Success);
        Assert.Equal("test", result.Action);
        Assert.Equal("msg",  result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Fail_Outcome_Preserves_Success_False_With_Null_Data()
    {
        var outcome = MemctlOutcome.Fail("test", "boom");
        var result  = MemctlResultMapper.ToResult(outcome);

        Assert.False(result.Success);
        Assert.Equal("boom", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Note_Maps_To_NoteDto()
    {
        var note = new Note
        {
            Id          = "abc123",
            FilePath    = "notes/x.md",
            Title       = "X",
            Content     = "body",
            Tags        = ["t1"],
            Links       = ["L"],
            Created     = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
            Modified    = new System.DateTime(2026, 1, 2, 0, 0, 0, System.DateTimeKind.Utc),
            Weight      = 1.25f,
            AccessCount = 7,
        };
        var outcome = MemctlOutcome.Ok("get", "ok", note);
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<NoteDto>(result.Data);
        Assert.Equal("abc123",                                                    dto.Id);
        Assert.Equal("notes/x.md",                                                dto.File);
        Assert.Equal("X",                                                         dto.Title);
        Assert.Equal("body",                                                      dto.Content);
        Assert.Equal(["t1"],                                                      dto.Tags);
        Assert.Equal(["L"],                                                       dto.Links);
        Assert.Equal("2026-01-01T00:00:00.0000000Z",                              dto.Created);
        Assert.Equal("2026-01-02T00:00:00.0000000Z",                              dto.Modified);
        Assert.Equal(1.25f,                                                       dto.Weight);
        Assert.Equal(7,                                                           dto.AccessCount);
        Assert.Null(dto.Score);
        Assert.Null(dto.Snippet);
    }

    [Fact]
    public void NoteList_Maps_To_NoteListResultDto()
    {
        var notes = new List<Note>
        {
            new() { Id = "a", FilePath = "a.md", Title = "A" },
            new() { Id = "b", FilePath = "b.md", Title = "B" },
        };
        var outcome = MemctlOutcome.Ok("list", "2 notes", (IReadOnlyList<Note>)notes);
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<NoteListResultDto>(result.Data);
        Assert.Equal(2,    dto.Count);
        Assert.Equal(2,    dto.Notes.Length);
        Assert.Equal("a",  dto.Notes[0].Id);
        Assert.Equal("b",  dto.Notes[1].Id);
    }

    [Fact]
    public void TagList_Maps_To_TagsListResultDto()
    {
        var tags = new List<TagCount>
        {
            new("alpha", 3),
            new("beta",  1),
        };
        var outcome = MemctlOutcome.Ok("tags", "2 tags", (IReadOnlyList<TagCount>)tags);
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<TagsListResultDto>(result.Data);
        Assert.Equal(2,        dto.Count);
        Assert.Equal("alpha",  dto.Tags[0].Tag);
        Assert.Equal(3,        dto.Tags[0].Count);
    }

    [Fact]
    public void GrepResult_Maps_To_GrepListResultDto_With_Pattern()
    {
        var hits = new List<GrepHit>
        {
            new("a.md", 1, "line a"),
            new("b.md", 2, "line b"),
        };
        var outcome = MemctlOutcome.Ok("grep", "2 matches", new GrepResult("foo", hits));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<GrepListResultDto>(result.Data);
        Assert.Equal("foo",   dto.Pattern);
        Assert.Equal(2,       dto.Count);
        Assert.Equal("a.md",  dto.Hits[0].File);
        Assert.Equal(1,       dto.Hits[0].Line);
    }

    [Fact]
    public void SearchHits_Maps_To_SearchResultDto_With_Query_And_Snippet()
    {
        var hits = new List<SearchHit>
        {
            new() {
                Note    = new Note { Id = "n1", FilePath = "n1.md", Title = "N1" },
                Score   = 0.9f,
                Snippet = "preview",
            },
        };
        var outcome = MemctlOutcome.Ok("search", "1 results", new SearchHitsResult("q", hits));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<SearchResultDto>(result.Data);
        Assert.Equal("q",        dto.Query);
        Assert.Equal(1,          dto.Count);
        Assert.Equal("preview",  dto.Results[0].Snippet);
        Assert.Equal(0.9f,       dto.Results[0].Score);
    }

    [Fact]
    public void SearchTagsHits_Maps_With_MatchAll()
    {
        var notes = new List<Note> { new() { Id = "n1", Title = "T", FilePath = "t.md" } };
        var outcome = MemctlOutcome.Ok("search-tags", "1 results",
            new SearchTagsHitsResult(["a", "b"], MatchAll: true, notes));
        var result = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<SearchTagsResultDto>(result.Data);
        Assert.Equal(["a", "b"],  dto.Tags);
        Assert.True(dto.MatchAll);
        Assert.Equal(1,           dto.Count);
    }

    [Fact]
    public void SearchLinksHits_Maps_With_SourceId_And_Depth()
    {
        var notes = new List<Note> { new() { Id = "n1", Title = "T", FilePath = "t.md" } };
        var outcome = MemctlOutcome.Ok("search-links", "1 linked",
            new SearchLinksHitsResult("src", Depth: 2, notes));
        var result = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<SearchLinksResultDto>(result.Data);
        Assert.Equal("src",  dto.SourceId);
        Assert.Equal(2,      dto.Depth);
        Assert.Equal(1,      dto.Count);
    }

    [Fact]
    public void SearchDateHits_Maps_With_From_And_To()
    {
        var notes = new List<Note> { new() { Id = "n1", Title = "T", FilePath = "t.md" } };
        var outcome = MemctlOutcome.Ok("search-date", "1 results",
            new SearchDateHitsResult("2026-01-01", "2026-02-01", notes));
        var result = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<SearchDateResultDto>(result.Data);
        Assert.Equal("2026-01-01",  dto.From);
        Assert.Equal("2026-02-01",  dto.To);
        Assert.Equal(1,             dto.Count);
    }

    [Fact]
    public void VaultStats_Maps_To_StatsDto()
    {
        var stats   = new VaultStats(42, 5, 12, 100_000L, "/path");
        var outcome = MemctlOutcome.Ok("stats", "ok", stats);
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<StatsDto>(result.Data);
        Assert.Equal(42,         dto.NoteCount);
        Assert.Equal(5,          dto.TagCount);
        Assert.Equal(12,         dto.LinkCount);
        Assert.Equal(100_000L,   dto.IndexBytes);
        Assert.Equal("/path",    dto.VaultPath);
    }

    [Fact]
    public void VaultStatus_Maps_To_VaultStatusDto()
    {
        var status  = new VaultStatus(true, "/m", 300, true, true, 7, "/db");
        var outcome = MemctlOutcome.Ok("status", "Ready", status);
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<VaultStatusDto>(result.Data);
        Assert.True(dto.ModelReady);
        Assert.Equal("/m",   dto.ModelPath);
        Assert.Equal(300,    dto.ModelSizeMb);
        Assert.True(dto.VaultExists);
        Assert.True(dto.VaultIndexed);
        Assert.Equal(7,      dto.NoteCount);
        Assert.Equal("/db",  dto.IndexPath);
    }

    [Fact]
    public void WeightChange_Maps_To_WeightChangeDto()
    {
        var outcome = MemctlOutcome.Ok("weight", "ok", new WeightChange("id1", "f.md", 1.5f));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<WeightChangeDto>(result.Data);
        Assert.Equal("id1",   dto.Id);
        Assert.Equal("f.md",  dto.File);
        Assert.Equal(1.5f,    dto.Weight);
    }

    [Fact]
    public void DecayReport_Maps_With_DryRun_Variant()
    {
        var outcome = MemctlOutcome.Ok("decay", "ok",
            new DecayReport(3, 1, 5, 0, AlreadyRunToday: null, DryRun: true));
        var result = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<DecayReportDto>(result.Data);
        Assert.Equal(3,    dto.Decayed);
        Assert.Equal(1,    dto.Archived);
        Assert.Equal(5,    dto.Unchanged);
        Assert.Equal(0,    dto.AlreadyArchived);
        Assert.Null(dto.AlreadyRunToday);
        Assert.True(dto.DryRun);
    }

    [Fact]
    public void CaptureReport_Maps_With_Optional_Weight()
    {
        var outcome = MemctlOutcome.Ok("capture", "ok",
            new CaptureReport(false, "f.md", 5, 0.5f));
        var result = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<CaptureReportDto>(result.Data);
        Assert.False(dto.DryRun);
        Assert.Equal("f.md",  dto.File);
        Assert.Equal(5,       dto.Turns);
        Assert.Equal(0.5f,    dto.Weight);
    }

    [Fact]
    public void IngestReport_Maps_With_Optional_Hint()
    {
        var outcome = MemctlOutcome.Ok("ingest", "ok",
            new IngestReport(10, 12, "/v", "model-x", "hint text"));
        var result = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<IngestReportDto>(result.Data);
        Assert.Equal(10,           dto.Indexed);
        Assert.Equal(12,           dto.Total);
        Assert.Equal("/v",         dto.Vault);
        Assert.Equal("model-x",    dto.Model);
        Assert.Equal("hint text",  dto.SemanticLintHint);
    }

    [Fact]
    public void OrganizeReport_Maps_To_OrganizeReportDto()
    {
        var outcome = MemctlOutcome.Ok("organize", "ok", new OrganizeReport(7, 1, "/v"));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<OrganizeReportDto>(result.Data);
        Assert.Equal(7,    dto.Updated);
        Assert.Equal(1,    dto.Errors);
        Assert.Equal("/v", dto.Vault);
    }

    [Fact]
    public void ModelInfo_Maps_To_ModelInfoDto()
    {
        var outcome = MemctlOutcome.Ok("model-download", "ok", new ModelInfo("/m", 300));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<ModelInfoDto>(result.Data);
        Assert.Equal("/m",  dto.ModelPath);
        Assert.Equal(300,   dto.ModelSizeMb);
    }

    [Fact]
    public void ModelList_Maps_With_ModelEntry_Items()
    {
        var entries = new List<ModelEntry>
        {
            new("m1", true,  300, true),
            new("m2", false, 100, false),
        };
        var outcome = MemctlOutcome.Ok("model-list", "ok", new ModelList("m1", entries));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<ModelListDto>(result.Data);
        Assert.Equal("m1",  dto.DefaultModel);
        Assert.Equal(2,     dto.Models.Length);
        Assert.True(dto.Models[0].IsDefault);
        Assert.Equal("m1",  dto.Models[0].Name);
        Assert.False(dto.Models[1].Ready);
    }

    [Fact]
    public void ModelSelection_Maps_To_ModelSelectionDto()
    {
        var outcome = MemctlOutcome.Ok("model-use", "ok", new ModelSelection("mx"));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<ModelSelectionDto>(result.Data);
        Assert.Equal("mx", dto.Model);
    }

    [Fact]
    public void VaultRef_Maps_To_VaultRefDto()
    {
        var outcome = MemctlOutcome.Ok("init", "ok", new VaultRef("/path"));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<VaultRefDto>(result.Data);
        Assert.Equal("/path", dto.Vault);
    }

    [Fact]
    public void LintReport_Maps_To_LintReportDto_With_Typed_Structural()
    {
        var structural = new LintStructural(
            Orphans:     [new("o1", "Orphan", "o.md")],
            BrokenLinks: [new("n1", "N",      "BL")],
            Duplicates:  [new("a", "A", "b", "B", 0.95)],
            DecayRisk:   [new("d", "D", 0.2f, 90, 3)]);
        var outcome = MemctlOutcome.Ok("lint", "ok", new LintReport(structural, null));
        var result  = MemctlResultMapper.ToResult(outcome);

        var dto = Assert.IsType<LintReportDto>(result.Data);
        Assert.Equal(1,           dto.Structural.Orphans.Length);
        Assert.Equal("o1",        dto.Structural.Orphans[0].Id);
        Assert.Equal("Orphan",    dto.Structural.Orphans[0].Title);
        Assert.Equal("o.md",      dto.Structural.Orphans[0].FilePath);
        Assert.Equal("BL",        dto.Structural.BrokenLinks[0].BrokenLink);
        Assert.Equal(0.95,        dto.Structural.Duplicates[0].Similarity);
        Assert.Equal(90,          dto.Structural.DecayRisk[0].DaysSinceModified);
        Assert.Null(dto.Semantic);
    }

    [Fact]
    public void Raw_String_Passes_Through()
    {
        var outcome = MemctlOutcome.Ok("fetch", "ok", "some markdown");
        var result  = MemctlResultMapper.ToResult(outcome);

        Assert.Equal("some markdown", result.Data);
    }

    [Fact]
    public void Unknown_Type_Throws_With_Type_Name_And_Action()
    {
        var outcome = MemctlOutcome.Ok("custom", "ok", new { unknown = true });
        var ex      = Assert.Throws<System.InvalidOperationException>(
            () => MemctlResultMapper.ToResult(outcome));
        Assert.Contains("unsupported Data type", ex.Message);
        Assert.Contains("custom",                ex.Message);
    }
}
