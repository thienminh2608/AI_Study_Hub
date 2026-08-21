using System.Text.Json;
using AIStudyHub.Application.Services;

namespace AIStudyHub.UnitTests;

public class DocumentChunkerTests
{
    [Fact]
    public void Chunk_PreservesOrderHeadingsPagesAndOverlap()
    {
        var sectionA = string.Join("\n\n", Enumerable.Range(1, 35).Select(i => $"Đoạn nội dung phương pháp {i}. " + new string('x', 150)));
        var sectionB = string.Join("\n\n", Enumerable.Range(1, 15).Select(i => $"Kết quả quan sát {i}. " + new string('y', 150)));
        var text = $"[PAGE 1]\n\n# I. PHƯƠNG PHÁP\n\na. Chuẩn bị\n\n{sectionA}\n\n[PAGE 2]\n\n# II. KẾT QUẢ\n\n{sectionB}";

        var chunks = DocumentChunker.Chunk(42, text, DateTime.UtcNow);

        Assert.True(chunks.Count >= 2);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
        Assert.All(chunks, c => Assert.True(c.StartOffset < c.EndOffset));
        Assert.Contains(chunks, c => c.PageNumber == 1);
        Assert.Contains(chunks, c => c.PageNumber == 2);
        Assert.Contains(chunks, c => ParseHeadings(c).Any(h => h.Contains("PHƯƠNG PHÁP")));
        Assert.Contains(chunks, c => ParseHeadings(c).Any(h => h.Contains("KẾT QUẢ")));
        Assert.Contains(chunks.Zip(chunks.Skip(1)), pair => HasSharedWords(pair.First.Text, pair.Second.Text));
    }

    [Fact]
    public void Chunk_RecognizesRomanAndLetterHeadings()
    {
        var chunks = DocumentChunker.Chunk(1, "II. Nội dung\n\nĐoạn chính.\n\na) Phương pháp\n\nChi tiết.", DateTime.UtcNow);
        var headings = JsonSerializer.Deserialize<string[]>(chunks.Single().HeadingPath!);
        Assert.Contains("II. Nội dung", headings!);
        Assert.Contains("a) Phương pháp", headings!);
    }

    [Fact]
    public void Chunk_Pins_DocumentVersionId_To_Generated_Chunks()
    {
        int versionId = 99;
        var chunks = DocumentChunker.Chunk(1, "Chương 1. Giới thiệu tổng quan hệ thống.", DateTime.UtcNow, null, versionId);

        Assert.Single(chunks);
        Assert.Equal(versionId, chunks[0].DocumentVersionId);
    }

    private static bool HasSharedWords(string first, string second)
    {
        var tail = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(10).ToHashSet();
        return second.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(30).Any(tail.Contains);
    }

    private static string[] ParseHeadings(AIStudyHub.Domain.Entities.DocumentChunk chunk) =>
        string.IsNullOrWhiteSpace(chunk.HeadingPath) ? [] : JsonSerializer.Deserialize<string[]>(chunk.HeadingPath)!;
}
