using System.Text.Json;
using System.Text.RegularExpressions;
using AIStudyHub.Domain.Entities;

namespace AIStudyHub.Application.Services;

public static partial class DocumentChunker
{
    private const int TargetCharacters = 4000; // approximately 800-1,200 tokens
    private const int MaximumCharacters = 6000; // approximately 1,500 tokens
    private const int OverlapCharacters = 600; // approximately 100-200 tokens

    public static IReadOnlyList<DocumentChunk> Chunk(int documentId, string text, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var blocks = ParseBlocks(text);
        var chunks = new List<DocumentChunk>();
        var headings = new SortedDictionary<int, string>();
        var current = new List<Block>();
        var currentLength = 0;

        foreach (var block in blocks)
        {
            if (block.HeadingLevel is int level)
            {
                if (currentLength >= TargetCharacters)
                {
                    Flush();
                    if (level <= 2)
                    {
                        current.Clear();
                        currentLength = 0;
                    }
                }
                foreach (var key in headings.Keys.Where(k => k >= level).ToList())
                    headings.Remove(key);
                headings[level] = block.Text;
            }

            if (currentLength > 0 && currentLength + block.Text.Length > MaximumCharacters)
                Flush();
            current.Add(block);
            currentLength += block.Text.Length + 2;
            if (currentLength >= TargetCharacters && block.IsBoundary)
                Flush();
        }
        Flush();
        return chunks;

        void Flush()
        {
            if (current.Count == 0)
                return;
            var first = current[0];
            var last = current[^1];
            chunks.Add(new DocumentChunk
            {
                DocumentId = documentId,
                ChunkIndex = chunks.Count,
                HeadingPath = headings.Count == 0 ? null : JsonSerializer.Serialize(headings.Values),
                PageNumber = current.Select(b => b.Page).LastOrDefault(p => p.HasValue),
                Text = string.Join("\n\n", current.Select(b => b.Text)),
                StartOffset = first.Start,
                EndOffset = last.End,
                CreatedAt = createdAt
            });

            var overlap = new List<Block>();
            var length = 0;
            for (var i = current.Count - 1; i >= 0 && length < OverlapCharacters; i--)
            {
                if (current[i].HeadingLevel is <= 2)
                    break;
                overlap.Insert(0, current[i]);
                length += current[i].Text.Length;
            }
            current = overlap;
            currentLength = length;
        }
    }

    private static List<Block> ParseBlocks(string text)
    {
        var blocks = new List<Block>();
        var page = (int?)null;
        foreach (Match match in BlockRegex().Matches(text.Replace("\r\n", "\n")))
        {
            var value = match.Value.Trim();
            if (value.Length == 0)
                continue;
            var marker = PageMarkerRegex().Match(value);
            if (marker.Success)
            {
                page = int.Parse(marker.Groups[1].Value);
                continue;
            }
            blocks.Add(new Block(value, match.Index, match.Index + match.Length, page,
                GetHeadingLevel(value), value.EndsWith('.') || value.Contains('\n')));
        }
        return blocks;
    }

    private static int? GetHeadingLevel(string value)
    {
        if (MarkdownHeadingRegex().Match(value) is { Success: true } md)
            return md.Groups[1].Length;
        if (RomanHeadingRegex().IsMatch(value))
            return 1;
        if (NumberedHeadingRegex().IsMatch(value))
            return 2;
        if (LetterHeadingRegex().IsMatch(value))
            return 3;
        if (value.Length <= 100 && value == value.ToUpperInvariant() && value.Any(char.IsLetter))
            return 1;
        return null;
    }

    private sealed record Block(string Text, int Start, int End, int? Page, int? HeadingLevel, bool IsBoundary);

    [GeneratedRegex(@"(?ms)(?:^\s*$\n)*(.+?)(?=\n\s*\n|\z)")]
    private static partial Regex BlockRegex();
    [GeneratedRegex(@"^\[PAGE\s+(\d+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex PageMarkerRegex();
    [GeneratedRegex(@"^(#{1,6})\s+")]
    private static partial Regex MarkdownHeadingRegex();
    [GeneratedRegex(@"^(?:CHAPTER|CHƯƠNG|PHẦN|MỤC|[IVXLCDM]+)[\s.:\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex RomanHeadingRegex();
    [GeneratedRegex(@"^\d+(?:\.\d+)*[.)]?\s+\S+")]
    private static partial Regex NumberedHeadingRegex();
    [GeneratedRegex(@"^[a-zA-Z][.)]\s+\S+")]
    private static partial Regex LetterHeadingRegex();
}
