using System;
using System.IO;
using Memctl.Implementations.Vault;
using Xunit;

namespace memctl.Tests.Vault;

public class InitV2Tests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly ObsidianVaultReader _reader;

    public InitV2Tests()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), "memctl-test-init-" + Guid.NewGuid());
        Directory.CreateDirectory(_tmpRoot);
        _reader = new ObsidianVaultReader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpRoot)) Directory.Delete(_tmpRoot, recursive: true);
    }

    [Fact]
    public void Init_with_parent_anchor_creates_memctl_subdir()
    {
        var anchor = Path.Combine(_tmpRoot, "project");

        _reader.InitVaultStructure(anchor);

        // V2.1: parent path → .memctl/ subdir created inside
        Assert.True(Directory.Exists(Path.Combine(anchor, ".memctl")));
        Assert.True(Directory.Exists(Path.Combine(anchor, ".memctl", ".obsidian")));
        Assert.True(Directory.Exists(Path.Combine(anchor, ".memctl", ".obsidian", "memctl")));

        // 7 semantic top-level dirs
        foreach (var d in new[] { "tasks", "patterns", "lessons", "decisions", "chats", "attachments", "ai-memory" })
            Assert.True(Directory.Exists(Path.Combine(anchor, ".memctl", d)), $"Missing {d}/");

        // Obsidian config files
        Assert.True(File.Exists(Path.Combine(anchor, ".memctl", ".obsidian", "app.json")));
        Assert.True(File.Exists(Path.Combine(anchor, ".memctl", ".obsidian", "daily-notes.json")));

        // No nested .memctl/.memctl/
        Assert.False(Directory.Exists(Path.Combine(anchor, ".memctl", ".memctl")));

        // README + ai-memory/MEMORY.md
        Assert.True(File.Exists(Path.Combine(anchor, ".memctl", "README.md")));
        Assert.True(File.Exists(Path.Combine(anchor, ".memctl", "ai-memory", "MEMORY.md")));
    }

    [Fact]
    public void Init_with_direct_memctl_path_skips_nesting()
    {
        var direct = Path.Combine(_tmpRoot, "vault", ".memctl");

        _reader.InitVaultStructure(direct);

        // Direct path: <path>/.obsidian/ inside, NOT <path>/.memctl/.obsidian/
        Assert.True(Directory.Exists(Path.Combine(direct, ".obsidian")));
        Assert.True(Directory.Exists(Path.Combine(direct, ".obsidian", "memctl")));
        Assert.False(Directory.Exists(Path.Combine(direct, ".memctl")));

        // Same 7 dirs at direct path
        foreach (var d in new[] { "tasks", "patterns", "lessons", "decisions", "chats", "attachments", "ai-memory" })
            Assert.True(Directory.Exists(Path.Combine(direct, d)), $"Missing {d}/");
    }

    [Fact]
    public void Reinit_existing_v2_idempotent()
    {
        var anchor = Path.Combine(_tmpRoot, "project");

        _reader.InitVaultStructure(anchor);
        var appJsonPath = Path.Combine(anchor, ".memctl", ".obsidian", "app.json");
        File.WriteAllText(appJsonPath, """{"customized":true}""");
        var beforeContent = File.ReadAllText(appJsonPath);

        // Second init — should NOT overwrite existing customized config
        _reader.InitVaultStructure(anchor);

        var afterContent = File.ReadAllText(appJsonPath);
        Assert.Equal(beforeContent, afterContent);
    }
}
