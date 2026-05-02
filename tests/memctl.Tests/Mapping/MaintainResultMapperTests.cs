using Memctl.Boundary;
using Memctl.CoreAbstractions.Entities;
using Memctl.Operators;
using Memctl.Operators.Mapping;
using Xunit;

namespace Memctl.Tests.Mapping;

public class MaintainResultMapperTests
{
    [Fact]
    public void MaintainResult_Maps_To_MaintainResultDto()
    {
        var outcome = MemctlOutcome.Ok("maintain", "2 actions applied",
            new MaintainResult(["ingest", "decay"], ["lint (recent)"], null, false, null));

        var result = MemctlResultMapper.ToResult(outcome);
        var dto    = Assert.IsType<MaintainResultDto>(result.Data);

        Assert.Equal(2, dto.Actions.Length);
        Assert.Equal("ingest", dto.Actions[0]);
        Assert.Equal("decay",  dto.Actions[1]);
        Assert.Single(dto.Skipped);
        Assert.Null(dto.SkippedReason);
        Assert.False(dto.Throttled);
        Assert.Null(dto.DryRun);
    }

    [Fact]
    public void Throttled_MaintainResult_Carries_Reason()
    {
        var outcome = MemctlOutcome.Ok("maintain", "Throttled — skipped",
            new MaintainResult([], [], "throttled (12s ago, limit 60s)", true, null));

        var result = MemctlResultMapper.ToResult(outcome);
        var dto    = Assert.IsType<MaintainResultDto>(result.Data);

        Assert.Empty(dto.Actions);
        Assert.Empty(dto.Skipped);
        Assert.Equal("throttled (12s ago, limit 60s)", dto.SkippedReason);
        Assert.True(dto.Throttled);
    }

    [Fact]
    public void DryRun_MaintainResult_Sets_DryRun_Flag()
    {
        var outcome = MemctlOutcome.Ok("maintain", "1 action planned",
            new MaintainResult(["ingest (planned)"], [], null, false, true));

        var result = MemctlResultMapper.ToResult(outcome);
        var dto    = Assert.IsType<MaintainResultDto>(result.Data);

        Assert.True(dto.DryRun);
    }

    [Fact]
    public void NothingDue_Sets_SkippedReason()
    {
        var outcome = MemctlOutcome.Ok("maintain", "Nothing due — vault healthy",
            new MaintainResult([], ["ingest (vault unchanged)", "lint (recent)", "decay (recent)"],
                "nothing_due", false, null));

        var result = MemctlResultMapper.ToResult(outcome);
        var dto    = Assert.IsType<MaintainResultDto>(result.Data);

        Assert.Empty(dto.Actions);
        Assert.Equal(3, dto.Skipped.Length);
        Assert.Equal("nothing_due", dto.SkippedReason);
    }
}
