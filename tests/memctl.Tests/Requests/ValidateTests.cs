using Memctl.Boundary.Requests;
using Xunit;

namespace Memctl.Tests.Requests;

public class ValidateTests
{
    [Fact]
    public void SetWeight_valid()
    {
        var r = new SetWeightRequest { Id = "abc", Weight = 0.5f };
        Assert.Empty(r.Validate());
    }

    [Fact]
    public void SetWeight_missing_id()
    {
        var r = new SetWeightRequest { Id = "", Weight = 0.5f };
        var errs = r.Validate();
        Assert.Contains(errs, e => e.StartsWith("id:"));
    }

    [Fact]
    public void SetWeight_out_of_range()
    {
        var r = new SetWeightRequest { Id = "abc", Weight = 5f };
        var errs = r.Validate();
        Assert.Contains(errs, e => e.StartsWith("weight:"));
    }

    [Fact]
    public void Decay_valid()
    {
        var r = new DecayRequest { Days = 30, DecayFactor = 0.9, DryRun = false };
        Assert.Empty(r.Validate());
    }

    [Fact]
    public void Decay_invalid_days()
    {
        var r = new DecayRequest { Days = 5000, DecayFactor = 0.9 };
        var errs = r.Validate();
        Assert.Contains(errs, e => e.StartsWith("days:"));
    }

    [Fact]
    public void AddNote_valid()
    {
        var r = new AddNoteRequest { Text = "hello" };
        Assert.Empty(r.Validate());
    }

    [Fact]
    public void AddNote_empty_text()
    {
        var r = new AddNoteRequest { Text = "" };
        var errs = r.Validate();
        Assert.Contains(errs, e => e.StartsWith("text:"));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("feedback")]
    [InlineData("project")]
    [InlineData("reference")]
    [InlineData(null)]
    public void AddNote_valid_type(string? type)
    {
        var r = new AddNoteRequest { Text = "x", Type = type };
        Assert.Empty(r.Validate());
    }

    [Fact]
    public void AddNote_invalid_type()
    {
        var r = new AddNoteRequest { Text = "x", Type = "wrongtype" };
        var errs = r.Validate();
        Assert.Contains(errs, e => e.StartsWith("type:"));
    }

    [Fact]
    public void AddNote_type_caseInsensitive()
    {
        var r = new AddNoteRequest { Text = "x", Type = "Feedback" };
        Assert.Empty(r.Validate());
    }
}
