using Memctl.Operators;
using Xunit;

namespace Memctl.Tests.Operators;

public class EventLogTests : IDisposable
{
    private readonly string _vaultPath;

    public EventLogTests()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), "memctl-test-eventlog-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_vaultPath, "events"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_vaultPath))
            Directory.Delete(_vaultPath, recursive: true);
    }

    [Fact]
    public void Record_WritesFileToEventsFolder()
    {
        EventLog.Record(_vaultPath, "operator_run", "info", "capture", "2 turns → chats/2026-05-07-abc.md");

        var files = Directory.GetFiles(Path.Combine(_vaultPath, "events"), "*.md");
        Assert.Single(files);
        Assert.Contains("capture", Path.GetFileName(files[0]));
    }

    [Fact]
    public void Record_FrontmatterContainsAllFields()
    {
        EventLog.Record(_vaultPath, "operator_run", "info", "ingest", "3 indexed, 0 pruned", "conv123");

        var file    = Directory.GetFiles(Path.Combine(_vaultPath, "events"), "*.md")[0];
        var content = File.ReadAllText(file);

        Assert.Contains("type: operator_run",  content);
        Assert.Contains("severity: info",      content);
        Assert.Contains("source: ingest",      content);
        Assert.Contains("payload:",            content);
        Assert.Contains("timestamp:",          content);
        Assert.Contains("conversation_id: conv123", content);
        Assert.Contains("archived: true",      content);
        Assert.Contains("- event",             content);
        Assert.Contains("- info",              content);
    }

    [Fact]
    public void Record_SilentOnInvalidVaultPath()
    {
        var ex = Record.Exception(() =>
            EventLog.Record(@"Z:\does\not\exist\vault", "operator_run", "error", "capture", "bad path"));

        Assert.Null(ex);
    }
}
