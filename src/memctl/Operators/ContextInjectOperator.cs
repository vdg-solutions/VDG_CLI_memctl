using System.Text;
using System.Text.RegularExpressions;
using Memctl.CoreAbstractions.Entities;
using Memctl.CoreAbstractions.Ports;

namespace Memctl.Operators;

public sealed class ContextInjectOperator(IVaultReader vaultReader, INoteIndex index)
{
    private const int SearchLimitPerKeyword = 3;
    private const int SearchLimit           = 6;
    private const int ListSecondary         = 3;
    private const int ListFallback          = 6;
    private const int ContentMaxLen         = 500;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "must", "shall", "can", "need", "dare",
        "ought", "used", "and", "or", "but", "if", "in", "on", "at", "to",
        "for", "of", "with", "by", "as", "from", "into", "about", "up",
        "out", "then", "than", "that", "this", "these", "those",
        "i", "you", "he", "she", "it", "we", "they",
        "me", "him", "her", "us", "them",
        "my", "your", "his", "its", "our", "their"
    };

    public string? Execute(string vaultPath, string promptText)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);
        index.Initialize(IngestOperator.DbPath(vaultPath));

        var keywords = ExtractKeywords(promptText);
        List<Note> results;

        if (keywords.Count == 0)
        {
            results = index.GetAll().Take(ListFallback).ToList();
        }
        else
        {
            var searchNotes = SearchKeywords(keywords);
            if (searchNotes.Count == 0)
            {
                results = index.GetAll().Take(ListFallback).ToList();
            }
            else
            {
                var searchIds = searchNotes.Select(n => n.Id).ToHashSet();
                var listNotes = index.GetAll()
                    .Where(n => !searchIds.Contains(n.Id))
                    .Take(ListSecondary)
                    .ToList();
                results = [.. searchNotes, .. listNotes];
            }
        }

        if (results.Count == 0) return null;

        foreach (var note in results)
            index.IncrementAccess(note.Id);

        return FormatContext(results);
    }

    private IReadOnlyList<Note> SearchKeywords(IReadOnlyList<string> keywords)
    {
        var seen  = new HashSet<string>();
        var notes = new List<Note>();
        foreach (var kw in keywords)
        {
            foreach (var hit in index.SearchBm25(kw, SearchLimitPerKeyword))
            {
                if (seen.Add(hit.Note.Id))
                    notes.Add(hit.Note);
                if (notes.Count >= SearchLimit) break;
            }
            if (notes.Count >= SearchLimit) break;
        }
        return notes;
    }

    private static List<string> ExtractKeywords(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"\W+")
             .Where(t => t.Length >= 2 && !StopWords.Contains(t))
             .Distinct()
             .ToList();

    private static string FormatContext(IReadOnlyList<Note> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Memory Context");
        sb.AppendLine();
        foreach (var note in notes)
        {
            sb.AppendLine($"### {note.Title}");
            var content = note.Content.Length > ContentMaxLen
                ? note.Content[..ContentMaxLen] + "..."
                : note.Content;
            sb.AppendLine(content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
