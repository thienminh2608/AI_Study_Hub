using System.Diagnostics;
using System.Text;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace AIStudyHub.UnitTests;

public sealed class ChunkBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MeasureOldAndChunkedPipeline()
    {
        var bytes = BuildDocument(466_227);
        var oldUpload = new List<double>();
        var newUpload = new List<double>();
        var metadata = new List<double>();
        var fullText = new List<double>();
        var topChunks = new List<double>();
        var chunkCounts = new List<int>();
        var chunkPayloads = new List<int>();

        for (var run = 0; run < 6; run++)
        {
            var old = await MeasureOld(bytes);
            var current = await MeasureCurrent(bytes);
            if (run == 0)
                continue; // warm-up
            oldUpload.Add(old);
            newUpload.Add(current.UploadMs);
            metadata.Add(current.MetadataMs);
            fullText.Add(current.FullTextMs);
            topChunks.Add(current.TopChunksMs);
            chunkCounts.Add(current.ChunkCount);
            chunkPayloads.Add(current.PayloadCharacters);
        }

        output.WriteLine($"OLD_UPLOAD_MEDIAN_MS={Median(oldUpload):F3}");
        output.WriteLine($"NEW_UPLOAD_MEDIAN_MS={Median(newUpload):F3}");
        output.WriteLine($"METADATA_MEDIAN_MS={Median(metadata):F3}");
        output.WriteLine($"FULL_TEXT_MEDIAN_MS={Median(fullText):F3}");
        output.WriteLine($"TOP8_CHUNKS_MEDIAN_MS={Median(topChunks):F3}");
        output.WriteLine($"CHUNK_COUNT_MEDIAN={Median(chunkCounts.Select(x => (double)x).ToList()):F0}");
        output.WriteLine($"TOP8_PAYLOAD_CHARS_MEDIAN={Median(chunkPayloads.Select(x => (double)x).ToList()):F0}");
        output.WriteLine($"FULL_TEXT_CHARS={bytes.Length}");
    }

    private static async Task<double> MeasureOld(byte[] bytes)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        Seed(db);
        await db.SaveChangesAsync();
        var sw = Stopwatch.StartNew();
        var doc = NewDocument();
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        db.DocumentExtractedTexts.Add(new DocumentExtractedText { DocumentId = doc.DocumentId, ExtractedText = Encoding.UTF8.GetString(bytes), CreatedAt = DateTime.UtcNow });
        doc.AiParsingStatus = "READY";
        await db.SaveChangesAsync();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task<Result> MeasureCurrent(byte[] bytes)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        Seed(db);
        await db.SaveChangesAsync();
        using var storage = new TempStorage();
        var service = new DocumentService(db, storage, new TestClock());
        var sw = Stopwatch.StartNew();
        await service.UploadDocumentAsync(1, null, "benchmark.txt", "txt", bytes.Length, new MemoryStream(bytes));
        sw.Stop();
        var upload = sw.Elapsed.TotalMilliseconds;
        db.ChangeTracker.Clear();
        sw.Restart();
        var document = await db.Documents.AsNoTracking().SingleAsync();
        sw.Stop();
        var metadata = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var text = await db.DocumentExtractedTexts.AsNoTracking().Where(x => x.DocumentId == document.DocumentId).Select(x => x.ExtractedText).SingleAsync();
        sw.Stop();
        var full = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var chunks = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == document.DocumentId).OrderBy(x => x.ChunkIndex).Take(8).ToListAsync();
        sw.Stop();
        return new(upload, metadata, full, sw.Elapsed.TotalMilliseconds, await db.DocumentChunks.CountAsync(), chunks.Sum(x => x.Text.Length));
    }

    private static byte[] BuildDocument(int size)
    {
        var builder = new StringBuilder("# I. PHƯƠNG PHÁP\n\n");
        var i = 0;
        while (Encoding.UTF8.GetByteCount(builder.ToString()) < size)
            builder.Append($"a. Mục {++i}\n\nNội dung học tập và phương pháp nghiên cứu số {i}. Dữ liệu dùng để kiểm tra truy hồi chunk và thời gian xử lý.\n\n");
        var data = Encoding.UTF8.GetBytes(builder.ToString());
        return data[..size];
    }

    private static void Seed(TestStudyHubDbContext db)
    {
        db.Subscriptions.Add(new Subscription { TierId = 3, TierName = "Premium", MaxStorageMb = 1000, TotalStorageMb = 1000, AiPromptLimitPerDay = 100, Price = 0 });
        db.Users.Add(new User { UserId = 1, Username = "bench", Email = "bench@test.local", PasswordHash = "x", Role = "STUDENT", Status = "ACTIVE", TierId = 3 });
    }
    private static Document NewDocument() => new() { UserId = 1, Title = "benchmark", FileExtension = "txt", CloudStorageUrl = "/x", FileSizeMb = 0.45m, AiParsingStatus = "PENDING", SharingPermission = "PRIVATE", ShareLinkToken = Guid.NewGuid().ToString() };
    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
    private sealed record Result(double UploadMs, double MetadataMs, double FullTextMs, double TopChunksMs, int ChunkCount, int PayloadCharacters);
    private sealed class TempStorage : IFileStorage, IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "aistudyhub-bench-" + Guid.NewGuid().ToString("N"));
        public Task SaveFileAsync(string path, Stream stream)
        {
            var target = GetPhysicalPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var file = File.Create(target);
            stream.CopyTo(file);
            return Task.CompletedTask;
        }
        public void DeleteFile(string path)
        {
            var target = GetPhysicalPath(path);
            if (File.Exists(target))
                File.Delete(target);
        }
        public void MoveFile(string source, string destination) => File.Move(GetPhysicalPath(source), GetPhysicalPath(destination), true);
        public bool FileExists(string path) => File.Exists(GetPhysicalPath(path));
        public string GetPhysicalPath(string path) => Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
