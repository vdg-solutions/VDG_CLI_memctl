using Memctl.CoreAbstractions.Entities;

namespace Memctl.CoreAbstractions.Ports;

public interface IVaultReader
{
    IEnumerable<string> EnumerateMarkdownFiles(string vaultPath);
    Note ParseNote(string absolutePath, string vaultPath);
    void InitVaultStructure(string vaultPath);
    void WriteNote(Note note, string vaultPath, string? fileName = null);
    void UpdateFrontmatter(string absolutePath, string[] tags, string[] links);
    void MarkAsDistilled(string absolutePath, DateTime distilledAt, string[] distilledNoteRelPaths);
}
