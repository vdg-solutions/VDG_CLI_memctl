using System;
using System.IO;
using Memctl.Implementations.Config;
using Xunit;

namespace memctl.Tests.Vault;

public class VaultLocatorEnvVarTests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly Func<string, string?> _origEnvReader;

    public VaultLocatorEnvVarTests()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), "memctl-test-envvar-" + Guid.NewGuid());
        Directory.CreateDirectory(_tmpRoot);
        _origEnvReader = VaultLocator.EnvReader;
    }

    public void Dispose()
    {
        VaultLocator.EnvReader = _origEnvReader;
        if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, recursive: true);
    }

    /// V2.1 vault root — `<path>/.obsidian/` is the marker.
    private string MakeV2VaultRoot(string sub)
    {
        var p = Path.Combine(_tmpRoot, sub);
        Directory.CreateDirectory(Path.Combine(p, ".obsidian"));
        return p;
    }

    /// V2.1 project — `<path>/.memctl/.obsidian/` is the marker pair.
    private string MakeV2Project(string sub)
    {
        var p = Path.Combine(_tmpRoot, sub);
        Directory.CreateDirectory(Path.Combine(p, ".memctl", ".obsidian"));
        return p;
    }

    [Fact]
    public void EnvVar_used_when_no_walk_up_match()
    {
        var sharedVault = MakeV2VaultRoot("shared");
        var noVaultDir = Path.Combine(_tmpRoot, "elsewhere");
        Directory.CreateDirectory(noVaultDir);
        VaultLocator.EnvReader = name => name == "MEMCTL_SHARED_VAULT" ? sharedVault : null;

        var d = VaultLocator.Discover(null, noVaultDir);

        // Walk-up may climb above _tmpRoot; only assert env strategy when not pre-empted by ancestor walk-up hit.
        // To isolate: when walk-up returns null, env var should kick in.
        if (d.Strategy.StartsWith("walk-up v2 (.memctl/)"))
        {
            // Ancestor of tmp had a vault — env fallback never reached. Skip this assertion path.
            return;
        }
        Assert.NotNull(d.Vault);
        Assert.Equal(Path.GetFullPath(sharedVault), d.Vault);
        Assert.Equal("MEMCTL_SHARED_VAULT env (shared)", d.Strategy);
    }

    [Fact]
    public void Walk_up_wins_over_env_var()
    {
        var projectRoot = MakeV2Project("project");
        var sharedVault = MakeV2VaultRoot("shared");
        VaultLocator.EnvReader = name => name == "MEMCTL_SHARED_VAULT" ? sharedVault : null;

        var d = VaultLocator.Discover(null, projectRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(projectRoot, ".memctl")), d.Vault);
        Assert.Equal("walk-up v2 (.memctl/)", d.Strategy);
    }

    [Fact]
    public void Explicit_flag_wins_over_env_var()
    {
        var explicitVault = MakeV2VaultRoot("explicit");
        var sharedVault   = MakeV2VaultRoot("shared");
        VaultLocator.EnvReader = name => name == "MEMCTL_SHARED_VAULT" ? sharedVault : null;

        var d = VaultLocator.Discover(explicitVault, _tmpRoot);

        Assert.Equal(explicitVault, d.Vault);
        Assert.Equal("explicit", d.Strategy);
    }

    [Fact]
    public void Env_var_pointing_at_invalid_dir_falls_through_to_null()
    {
        var noVaultDir = Path.Combine(_tmpRoot, "elsewhere");
        Directory.CreateDirectory(noVaultDir);
        VaultLocator.EnvReader = name => name == "MEMCTL_SHARED_VAULT" ? "/nonexistent/path-" + Guid.NewGuid() : null;

        var d = VaultLocator.Discover(null, noVaultDir);

        // Same caveat as test 1: ancestor may match. If it does, skip null check.
        if (!d.Strategy.StartsWith("walk-up v2 (.memctl/)"))
        {
            Assert.Null(d.Vault);
        }
    }
}
