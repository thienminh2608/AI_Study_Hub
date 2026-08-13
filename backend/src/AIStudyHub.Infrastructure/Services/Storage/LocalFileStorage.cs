using System;
using System.IO;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace AIStudyHub.Infrastructure.Services.Storage;

public class LocalFileStorage : IFileStorage
{
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

        if (File.Exists(sourcePhysical))
        {
            string directory = Path.GetDirectoryName(destPhysical) ?? "";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(sourcePhysical, destPhysical, overwrite: true);
        }
    }

    public bool FileExists(string relativePath)
    {
        string physicalPath = GetPhysicalPath(relativePath);
        return File.Exists(physicalPath);
    }
}
