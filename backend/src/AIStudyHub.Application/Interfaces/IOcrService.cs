using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IOcrService
{
    Task<OcrResultDto> ExtractAsync(string filePath, string fileName, CancellationToken cancellationToken = default);
}