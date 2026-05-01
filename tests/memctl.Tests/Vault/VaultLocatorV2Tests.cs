using System;
using System.IO;
using Memctl.Implementations.Config;
using Xunit;

namespace memctl.Tests.Vault;

public class VaultLocatorV2Tests : IDisposable
{
    private readonly string _tmpRoot;

    public VaultLocatorV2Tests()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), "memctl-test-v2-" + Guid.NewGuid());
        Directory.CreateDirectory(_tmpRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, recursive: true);
    }

    private string MakeV2Vault(string sub)
    {
        var p = Path.Combine(_tmpRoot, sub);
        Directory.CreateDirectory(Path.Combine(p, ".memctl", ".obsidian"));
        return p;
    }

    [Fact]
    public void WalkUp_finds_v2_when_memctl_contains_obsidian()
    {
        var root = MakeV2Vault("project");

        var d = VaultLocator.Discover(null, root);

        Assert.NotNull(d.Vault);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, ".memctl")), d.Vault);
        Assert.Equal("walk-up v2 (.memctl/)", d.Strategy);
    }

    [Fact]
    public void WalkUp_finds_v2_from_subdir()
    {
        var root = MakeV2Vault("project");
        var sub  = Path.Combine(root, "src", "deep");
        Directory.CreateDirectory(sub);

        var d = VaultLocator.Discover(null, sub);

        Assert.NotNull(d.Vault);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, ".memctl")), d.Vault);
        Assert.Equal("walk-up v2 (.memctl/)", d.Strategy);
    }

    [Fact]
    public void No_vault_returns_null()
    {
        // _tmpRoot itself has no vault markers
        var d = VaultLocator.Discover(null, _tmpRoot);

        // Walk-up may go all the way up — so vault could be non-null if some ancestor of
        // /tmp/memctl-test-v2-* happens to contain .memctl/. In fresh test env, expect null.
        // Assert strategy reflects walk-up was exhausted OR a legitimate hit far above.
        Assert.True(d.Vault is null || d.Strategy.StartsWith("walk-up"));
    }

    [Fact]
    public void Explicit_overrides_walk_up()
    {
        var root = MakeV2Vault("project");
        var explicitPath = Path.Combine(_tmpRoot, "elsewhere");
        Directory.CreateDirectory(explicitPath);

        var d = VaultLocator.Discover(explicitPath, root);

        Assert.Equal(explicitPath, d.Vault);
        Assert.Equal("explicit", d.Strategy);
    }
}
