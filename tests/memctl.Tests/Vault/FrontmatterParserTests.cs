using Memctl.Implementations.Vault;
using Xunit;

namespace Memctl.Tests.Vault;

public class FrontmatterParserTests
{
    [Fact]
    public void Empty_returns_empty_dict()
    {
        var d = FrontmatterParser.Parse("");
        Assert.Empty(d);
    }

    [Fact]
    public void Scalar_string_unquoted()
    {
        var d = FrontmatterParser.Parse("title: Hello World");
        Assert.Equal("Hello World", d["title"]);
    }

    [Fact]
    public void Scalar_string_quoted_double()
    {
        var d = FrontmatterParser.Parse("""title: "quoted value" """);
        Assert.Equal("quoted value", d["title"]);
    }

    [Fact]
    public void Scalar_bool()
    {
        var d = FrontmatterParser.Parse("archived: true\ndraft: false");
        Assert.Equal(true,  d["archived"]);
        Assert.Equal(false, d["draft"]);
    }

    [Fact]
    public void Scalar_int()
    {
        var d = FrontmatterParser.Parse("count: 42");
        Assert.Equal(42L, d["count"]);
    }

    [Fact]
    public void Scalar_float()
    {
        var d = FrontmatterParser.Parse("weight: 0.75");
        Assert.Equal(0.75, d["weight"]);
    }

    [Fact]
    public void Inline_array()
    {
        var d = FrontmatterParser.Parse("tags: [a, b, c]");
        var arr = Assert.IsType<string[]>(d["tags"]);
        Assert.Equal(["a", "b", "c"], arr);
    }

    [Fact]
    public void Multiline_list()
    {
        var raw = "tags:\n  - alpha\n  - beta\n  - gamma";
        var d = FrontmatterParser.Parse(raw);
        var arr = Assert.IsType<string[]>(d["tags"]);
        Assert.Equal(["alpha", "beta", "gamma"], arr);
    }

    [Fact]
    public void Multiline_list_with_quoted_wikilinks()
    {
        var raw = "links:\n  - \"[[foo]]\"\n  - \"[[bar]]\"";
        var d = FrontmatterParser.Parse(raw);
        var arr = Assert.IsType<string[]>(d["links"]);
        Assert.Equal(["[[foo]]", "[[bar]]"], arr);
    }

    [Fact]
    public void Empty_inline_array()
    {
        var d = FrontmatterParser.Parse("tags: []");
        var arr = Assert.IsType<string[]>(d["tags"]);
        Assert.Empty(arr);
    }

    [Fact]
    public void Mixed_keys_one_pass()
    {
        var raw = "id: abc123\ntitle: My Note\ntags:\n  - one\n  - two\nweight: 0.5";
        var d = FrontmatterParser.Parse(raw);
        Assert.Equal("abc123", d["id"]);
        Assert.Equal("My Note", d["title"]);
        Assert.Equal(["one", "two"], (string[])d["tags"]!);
        Assert.Equal(0.5, d["weight"]);
    }
}
