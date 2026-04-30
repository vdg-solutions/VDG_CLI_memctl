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

    /// Same walk-up algorithm but also returns the search path, strategy,
    /// and the full list of paths checked. Used to surface debug context to
    /// Claude Code when memory looks empty.
    public static VaultDiscovery Discover(string? explicitPath, string searchPath)
    {
        if (explicitPath is not null)
            return new VaultDiscovery(explicitPath, explicitPath, "explicit", [explicitPath]);

        var checkedPaths = new List<string>();
        var dir = searchPath;
        while (true)
        {
            checkedPaths.Add(dir);
            if (Directory.Exists(Path.Combine(dir, ".obsidian")))
                return new VaultDiscovery(Path.GetFullPath(dir), searchPath, "walk-up from cwd", checkedPaths);

            var parent = Directory.GetParent(dir);
            if (parent is null)
                return new VaultDiscovery(null, searchPath, "walk-up from cwd", checkedPaths);
            dir = parent.FullName;
        }
    }
}
