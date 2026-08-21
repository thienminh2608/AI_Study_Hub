using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;

namespace AIStudyHub.Infrastructure.Services.Storage;

public class MockFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _memoryFiles = new();

    public string GetPhysicalPath(string relativePath)
    {
        // For mock, just return the relative path as a simulated virtual path
        return relativePath;
    }

    public async Task SaveFileAsync(string relativePath, Stream fileStream)
    {
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);
        _memoryFiles[relativePath] = ms.ToArray();
    }

    public void DeleteFile(string relativePath)
    {
        _memoryFiles.TryRemove(relativePath, out _);
    }

    public void MoveFile(string sourceRelativePath, string destRelativePath)
    {
        if (_memoryFiles.TryRemove(sourceRelativePath, out var data))
        {
            _memoryFiles[destRelativePath] = data;
        }
    }

    public bool FileExists(string relativePath)
    {
        return _memoryFiles.ContainsKey(relativePath);
    }

    public Stream OpenReadStream(string relativePath)
    {
        if (!_memoryFiles.TryGetValue(relativePath, out var data))
            throw new FileNotFoundException("File not found on storage.", relativePath);
        return new MemoryStream(data, writable: false);
    }

    // Diagnostic helper for unit tests
    public byte[]? GetFileData(string relativePath)
    {
        _memoryFiles.TryGetValue(relativePath, out var data);
        return data;
    }
}
