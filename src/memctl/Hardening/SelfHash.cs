using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Memctl.Hardening;

internal static class SelfHash
{
    private const string SENTINEL_PREFIX = "\nMEMCTL_SHA:";  // bake script appends this + 64 hex chars
    private const int    SENTINEL_TOTAL  = 12 + 64;           // prefix length + hash length

    public static void Verify()
    {
        if (Environment.GetEnvironmentVariable("MEMCTL_ALLOW_DEBUG") == "1") return;

        var path = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            using var fs = File.OpenRead(path);
            var size = fs.Length;
            if (size < SENTINEL_TOTAL) return;  // not baked, dev build

            // Read trailing sentinel
            fs.Seek(size - SENTINEL_TOTAL, SeekOrigin.Begin);
            var trailBytes = new byte[SENTINEL_TOTAL];
            var n = fs.Read(trailBytes, 0, SENTINEL_TOTAL);
            if (n != SENTINEL_TOTAL) return;

            var trail = Encoding.ASCII.GetString(trailBytes);
            if (!trail.StartsWith(SENTINEL_PREFIX, StringComparison.Ordinal)) return;  // not baked

            var expected = trail.Substring(SENTINEL_PREFIX.Length).Trim();
            if (expected.Length != 64) Environment.FailFast("");

            // Hash the binary minus trailing sentinel
            fs.Seek(0, SeekOrigin.Begin);
            using var sha = SHA256.Create();
            var buf = new byte[8192];
            long remaining = size - SENTINEL_TOTAL;
            while (remaining > 0)
            {
                var take = (int)Math.Min(buf.Length, remaining);
                var read = fs.Read(buf, 0, take);
                if (read <= 0) break;
                sha.TransformBlock(buf, 0, read, null, 0);
                remaining -= read;
            }
            sha.TransformFinalBlock([], 0, 0);
            var actual = Convert.ToHexStringLower(sha.Hash!);

            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                Environment.FailFast("");
        }
        catch
        {
            // Fail-open on IO/permission errors — don't crash legit users.
        }
    }
}
