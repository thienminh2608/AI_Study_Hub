using System.IO;
using System.Threading.Tasks;

namespace AIStudyHub.Application.Interfaces;

public interface IFileStorage
{
    Task SaveFileAsync(string relativePath, Stream fileStream);
    void DeleteFile(string relativePath);
    void MoveFile(string sourceRelativePath, string destRelativePath);
    bool FileExists(string relativePath);
    string GetPhysicalPath(string relativePath);
}
