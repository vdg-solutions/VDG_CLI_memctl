using System.Globalization;

namespace Memctl.Implementations.Vault;

public static class FrontmatterParser
{
    public static Dictionary<string, object?> Parse(string raw)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw)) return dict;

        var lines = raw.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("#")) continue;
            // top-level item only — keys at column 0
            if (line.StartsWith(' ') || line.StartsWith('\t') || line.StartsWith("- ")) continue;

            var sep = line.IndexOf(':');
            if (sep <= 0) continue;
            var key   = line[..sep].Trim();
            var rest  = line[(sep + 1)..].Trim();

            if (rest.Length == 0)
            {
                // multi-line list: read subsequent indented `  - item` lines
                var items = new List<string>();
                while (i + 1 < lines.Length)
                {
                    var next = lines[i + 1].TrimEnd('\r');
                    var t    = next.TrimStart();
                    if (!(t.StartsWith("- ") || t.StartsWith("-\t") || t == "-"))
                    {
                        if (next.Length == 0) { i++; continue; }
                        break;
                    }
                    var itemRaw = t[1..].TrimStart();
                    items.Add(StripQuotes(itemRaw));
                    i++;
                }
                dict[key] = items.Count > 0 ? items.ToArray() : Array.Empty<string>();
            }
            else
            {
                dict[key] = ParseScalar(rest);
            }
        }
        return dict;
    }

    private static object? ParseScalar(string s)
    {
        if (s.Length == 0) return "";
        // inline list [a, b, c]
        if (s.StartsWith('[') && s.EndsWith(']'))
        {
            var inner = s[1..^1];
            if (inner.Length == 0) return Array.Empty<string>();
            return inner.Split(',').Select(x => StripQuotes(x.Trim())).ToArray();
        }
        if ((s.StartsWith('"') && s.EndsWith('"')) || (s.StartsWith('\'') && s.EndsWith('\'')))
            return s[1..^1];
        if (s.Equals("true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return s;
    }

    private static string StripQuotes(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1];
        return s;
    }
}
