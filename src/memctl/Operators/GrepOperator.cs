using System.Text.RegularExpressions;
using Memctl.CoreAbstractions.Entities;

namespace Memctl.Operators;

public sealed class GrepOperator
{
    public MemctlOutcome Execute(string vaultPath, string pattern, bool useRegex, int limit)
    {
        if (!Directory.Exists(vaultPath))
            return MemctlOutcome.Fail("grep", $"Vault not found: {vaultPath}");

        Regex? regex = null;
        if (useRegex)
        {
            try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
            catch (Exception ex) { return MemctlOutcome.Fail("grep", $"Invalid regex: {ex.Message}"); }
        }

        var hits  = new List<GrepHit>();
        var files = Directory.EnumerateFiles(vaultPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".obsidian" + Path.DirectorySeparatorChar));

        foreach (var file in files)
        {
            if (hits.Count >= limit) break;
            var relative = Path.GetRelativePath(vaultPath, file).Replace('\\', '/');
            var lines    = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length && hits.Count < limit; i++)
            {
                var matched = useRegex
                    ? regex!.IsMatch(lines[i])
                    : lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase);

                if (matched)
                    hits.Add(new GrepHit(relative, i + 1, lines[i].Trim()));
            }
        }

        return MemctlOutcome.Ok("grep", $"{hits.Count} matches", new GrepResult(pattern, hits));
    }
}
