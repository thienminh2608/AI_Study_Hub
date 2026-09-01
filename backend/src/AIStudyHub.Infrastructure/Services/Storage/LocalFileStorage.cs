using System;
using System.IO;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace AIStudyHub.Infrastructure.Services.Storage;

public class LocalFileStorage : IFileStorage
{
    private const int MoveRetryCount = 10;
    private const int MaxMoveRetryDelayMs = 500;
    private readonly IWebHostEnvironment _env;

    public LocalFileStorage(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string GetPhysicalPath(string relativePath)
    {
        string relative = relativePath.TrimStart('/');
        string rootPath = Path.GetFullPath(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
        string physicalPath = Path.GetFullPath(Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rootPrefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!physicalPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid storage path.");
        return physicalPath;
    }

    public async Task SaveFileAsync(string relativePath, Stream fileStream)
    {
        string physicalPath = GetPhysicalPath(relativePath);
        string directory = Path.GetDirectoryName(physicalPath) ?? "";
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fs = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fileStream.CopyToAsync(fs);
    }

    public void DeleteFile(string relativePath)
    {
        string physicalPath = GetPhysicalPath(relativePath);
        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }
    }

    public void MoveFile(string sourceRelativePath, string destRelativePath)
    {
        string sourcePhysical = GetPhysicalPath(sourceRelativePath);
        string destPhysical = GetPhysicalPath(destRelativePath);

        if (!File.Exists(sourcePhysical))
            return;

        string directory = Path.GetDirectoryName(destPhysical) ?? "";
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        for (int attempt = 1; attempt <= MoveRetryCount; attempt++)
        {
            try
            {
                File.Move(sourcePhysical, destPhysical, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MoveRetryCount)
            {
                // Extraction, preview, antivirus, or another request may hold a
                // short-lived read handle. On Windows that prevents rename even
                // though reading is allowed. Retry only I/O contention; access
                // and path errors must still surface immediately.
                if (!File.Exists(sourcePhysical) && File.Exists(destPhysical))
                    return;

                Thread.Sleep(Math.Min(50 * attempt, MaxMoveRetryDelayMs));
            }
        }
    }

    public bool FileExists(string relativePath)
    {
        string physicalPath = GetPhysicalPath(relativePath);
        return File.Exists(physicalPath);
    }

    public Stream OpenReadStream(string relativePath)
    {
        string physicalPath = GetPhysicalPath(relativePath);
        if (!File.Exists(physicalPath))
            throw new FileNotFoundException("File not found on storage.", physicalPath);
        return new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
