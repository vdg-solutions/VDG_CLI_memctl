namespace Memctl.Implementations.Config;

public sealed record VaultDiscovery(
    string?                Vault,
    string                 SearchPath,
    string                 Strategy,
    IReadOnlyList<string>  CheckedPaths);

public static class VaultLocator
{
    /// Hook for tests — production reads Environment directly.
    internal static Func<string, string?> EnvReader =
        name => Environment.GetEnvironmentVariable(name);

    public static string? FindVault(string? explicitPath)
        => Discover(explicitPath, Directory.GetCurrentDirectory()).Vault;

    public static string? FindVaultFrom(string startDir)
        => Discover(null, startDir).Vault;

    /// V2.1 resolver:
    /// 1. Explicit --vault flag wins
    /// 2. Walk-up looks for `<dir>/.memctl/.obsidian/` marker pair
    /// 3. MEMCTL_SHARED_VAULT env var fallback (lowest priority — per-project always wins)
    public static VaultDiscovery Discover(string? explicitPath, string searchPath)
    {
        if (explicitPath is not null)
            return new VaultDiscovery(explicitPath, explicitPath, "explicit", [explicitPath]);

        var checkedPaths = new List<string>();
        var dir = searchPath;
        while (true)
        {
            checkedPaths.Add(dir);

            var v2Vault = Path.Combine(dir, ".memctl");
            if (Directory.Exists(v2Vault) && Directory.Exists(Path.Combine(v2Vault, ".obsidian")))
                return new VaultDiscovery(Path.GetFullPath(v2Vault), searchPath, "walk-up v2 (.memctl/)", checkedPaths);

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // Lowest-priority fallback: MEMCTL_SHARED_VAULT env var.
        // Per-project .memctl/ already wins above; sensitive vaults always take priority.
        var sharedVault = EnvReader("MEMCTL_SHARED_VAULT");
        if (!string.IsNullOrWhiteSpace(sharedVault))
        {
            checkedPaths.Add($"$MEMCTL_SHARED_VAULT={sharedVault}");
            if (Directory.Exists(Path.Combine(sharedVault, ".obsidian")))
                return new VaultDiscovery(
                    Path.GetFullPath(sharedVault), searchPath, "MEMCTL_SHARED_VAULT env (shared)", checkedPaths);
        }

        return new VaultDiscovery(null, searchPath, "walk-up v2", checkedPaths);
    }
}
