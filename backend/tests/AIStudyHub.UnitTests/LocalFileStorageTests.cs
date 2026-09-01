using AIStudyHub.Infrastructure.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace AIStudyHub.UnitTests;

public class LocalFileStorageTests
{
    [Fact]
    public async Task MoveFile_RetriesWhileSourceHasTransientReadLock()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = Path.Combine(Path.GetTempPath(), $"aistudyhub-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        FileStream? readHandle = null;
        try
        {
            string source = Path.Combine(root, "uploads", "1", "temp.docx");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllTextAsync(source, "document content");

            var storage = new LocalFileStorage(new TestWebHostEnvironment(root));
            readHandle = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var releaseThread = new Thread(() =>
            {
                Thread.Sleep(200);
                readHandle.Dispose();
            });
            releaseThread.Start();

            storage.MoveFile("uploads/1/temp.docx", "uploads/1/final.docx");
            releaseThread.Join();

            Assert.False(File.Exists(source));
            Assert.Equal("document content", await File.ReadAllTextAsync(Path.Combine(root, "uploads", "1", "final.docx")));
        }
        finally
        {
            readHandle?.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string webRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AIStudyHub.UnitTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = webRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = webRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
