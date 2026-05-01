namespace Memctl.Implementations.Config;

public sealed record VaultDiscovery(
    string?                Vault,
    string                 SearchPath,
    string                 Strategy,
    IReadOnlyList<string>  CheckedPaths);

public static class VaultLocator
{
    public static string? FindVault(string? explicitPath)
        => Discover(explicitPath, Directory.GetCurrentDirectory()).Vault;

    public static string? FindVaultFrom(string startDir)
        => Discover(null, startDir).Vault;

    /// V2.1 resolver: walk-up looks for `<dir>/.memctl/` containing `.obsidian/` (V2 marker pair).
    /// Returns vault path = path of `.memctl/` itself (vault root container).
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

        return new VaultDiscovery(null, searchPath, "walk-up v2", checkedPaths);
    }
}
