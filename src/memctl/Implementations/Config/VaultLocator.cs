namespace Memctl.Implementations.Config;

public static class VaultLocator
{
    /// <summary>
    /// Returns the vault path to use.
    /// If explicitPath is provided, returns it directly.
    /// Otherwise walks up from cwd looking for a .obsidian/ marker.
    /// Returns null if no vault found.
    /// </summary>
    public static string? FindVault(string? explicitPath)
    {
        if (explicitPath is not null)
            return explicitPath;

        var dir = Directory.GetCurrentDirectory();
        while (true)
        {
            if (Directory.Exists(Path.Combine(dir, ".obsidian")))
                return Path.GetFullPath(dir);

            var parent = Directory.GetParent(dir);
            if (parent is null) return null;
            dir = parent.FullName;
        }
    }
}
