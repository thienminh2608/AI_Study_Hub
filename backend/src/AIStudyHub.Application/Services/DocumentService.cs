using AIStudyHub.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using PdfSharp.Pdf.IO;

namespace AIStudyHub.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IClock _clock;
    private const string UploadFolder = "uploads";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "pdf", "docx", "txt", "xlsx", "pptx", "md" };
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;

    public DocumentService(IStudyHubDbContext dbContext, IFileStorage fileStorage, IClock clock)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _clock = clock;
    }

    public async Task<DocumentResponseDto> UploadDocumentAsync(int userId, int? folderId, string originalFileName, string fileExtension, long fileSizeInBytes, Stream fileStream)
    {
        fileExtension = fileExtension.Trim().TrimStart('.').ToLowerInvariant();
        if (!AllowedExtensions.Contains(fileExtension))
            throw new ArgumentException("Unsupported file type.");
        if (fileSizeInBytes <= 0 || fileSizeInBytes > MaxFileSizeBytes)
            throw new ArgumentException("File size must be between 1 byte and 50 MB.");
        if (!fileStream.CanRead) throw new ArgumentException("The uploaded file cannot be read.");

        double fileSizeMb = fileSizeInBytes / (1024.0 * 1024.0);
        fileSizeMb = Math.Round(fileSizeMb, 2);

        // Check storage quota for non-Premium users
        var user = await _dbContext.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
        {
            throw new ArgumentException("Người dùng không tồn tại.");
        }

        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");

        // If not Premium tier
        if (user.TierId < 3)
        {
            var currentStorageUsed = await _dbContext.Documents
                .Where(d => d.UserId == userId)
                .SumAsync(d => (double?)d.FileSizeMb) ?? 0.0;

            double maxStorageMb = user.Tier?.MaxStorageMb ?? 50.0; // Free defaults to 50MB, guest 0

            if (currentStorageUsed + fileSizeMb > maxStorageMb)
            {
                throw new InvalidOperationException($"Không đủ dung lượng trống! Đã dùng {currentStorageUsed:F2} MB / {maxStorageMb:F2} MB. File tải lên: {fileSizeMb:F2} MB.");
            }
        }

        // Sanitize and save temp file
        string uuidPrefix = Guid.NewGuid().ToString().Substring(0, 8);
        string sanitizedName = SanitizeFileName(originalFileName);
        string savedFileName = $"temp_{uuidPrefix}_{sanitizedName}";
        string relativeFilePath = $"{UploadFolder}/{userId}/{savedFileName}";
        string cloudStorageUrl = $"/{UploadFolder}/{userId}/{savedFileName}";

        await _fileStorage.SaveFileAsync(relativeFilePath, fileStream);

        // Insert pending document metadata
        string shareToken = Guid.NewGuid().ToString();

        var document = new Document
        {
            UserId = userId,
            FolderId = folderId,
            Title = StripExtension(originalFileName),
            FileExtension = fileExtension.ToLower().Trim(),
            CloudStorageUrl = cloudStorageUrl,
            FileSizeMb = (decimal)fileSizeMb,
            AiParsingStatus = "PENDING",
            SharingPermission = "PRIVATE",
            ShareLinkToken = shareToken,
            TotalReportScore = 0.00m,
            IsFlagged = false,
            BookmarkCount = 0,
            DownloadCount = 0,
            CreatedAt = _clock.Now,
            UpdatedAt = _clock.Now
        };

        try
        {
            _dbContext.Documents.Add(document);
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            if (_fileStorage.FileExists(relativeFilePath)) _fileStorage.DeleteFile(relativeFilePath);
            throw;
        }

        // Extract text and save to DocumentExtractedText
        try
        {
            string savedPhysicalPath = _fileStorage.GetPhysicalPath(relativeFilePath);
            string extractedText = ExtractTextFromFile(savedPhysicalPath, fileExtension);
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                var textEntity = new DocumentExtractedText
                {
                    DocumentId = document.DocumentId,
                    ExtractedText = extractedText,
                    CreatedAt = _clock.Now
                };
                _dbContext.DocumentExtractedTexts.Add(textEntity);
                document.AiParsingStatus = "READY";
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TextExtraction] Failed to extract text: {ex.Message}");
            document.AiParsingStatus = "FAILED";
            await _dbContext.SaveChangesAsync();
        }

        return MapToDto(document, user.Username);
    }

    public async Task<DocumentResponseDto> ConfirmDocumentAsync(int userId, int documentId, string title, string sharingPermission, int? folderId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId && d.UserId == userId);
        if (doc == null)
        {
            throw new ArgumentException("Tài liệu không tồn tại hoặc bạn không có quyền.");
        }

        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");
        sharingPermission = NormalizeSharingPermission(sharingPermission);

        // Check if user is suspended (then they cannot make new documents PUBLIC)
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) throw new ArgumentException("Người dùng không tồn tại.");

        if ("PUBLIC".Equals(sharingPermission, StringComparison.OrdinalIgnoreCase) && 
            "SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            sharingPermission = "PRIVATE";
        }

        // Clean titles and handle duplicates
        string finalTitle = string.IsNullOrWhiteSpace(title) ? doc.Title : StripExtension(title);
        bool hasDuplicate = await CheckDuplicateTitleAsync(userId, finalTitle, folderId, documentId);
        if (hasDuplicate)
        {
            throw new InvalidOperationException("Tiêu đề tài liệu đã tồn tại trong thư mục này.");
        }

        // Rename physical file from temp to final
        string oldUrl = doc.CloudStorageUrl;
        string newUrl = RenamePhysicalFile(oldUrl, userId, finalTitle, doc.FileExtension);

        doc.Title = finalTitle;
        doc.FolderId = folderId;
        doc.SharingPermission = sharingPermission.ToUpper();
        doc.CloudStorageUrl = newUrl;
        doc.UpdatedAt = _clock.Now;

        await _dbContext.SaveChangesAsync();
        return MapToDto(doc, user.Username);
    }

    public async Task<DocumentResponseDto> ReplaceDocumentAsync(int userId, int pendingDocId, int duplicateDocId, string title, string sharingPermission, int? folderId)
    {
        sharingPermission = NormalizeSharingPermission(sharingPermission);
        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");
        var pendingDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == pendingDocId && d.UserId == userId);
        var oldDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == duplicateDocId && d.UserId == userId);
        if (pendingDoc == null || oldDoc == null)
        {
            throw new ArgumentException("Không tìm thấy tài liệu cần thiết.");
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) throw new ArgumentException("Người dùng không tồn tại.");

        if ("PUBLIC".Equals(sharingPermission, StringComparison.OrdinalIgnoreCase) && 
            "SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            sharingPermission = "PRIVATE";
        }

        // Delete old physical file
        DeletePhysicalFileByUrl(oldDoc.CloudStorageUrl);

        // Rename pending physical file to target final name
        string newUrl = RenamePhysicalFile(pendingDoc.CloudStorageUrl, userId, title, pendingDoc.FileExtension);

        // Replace metadata on the old document
        oldDoc.CloudStorageUrl = newUrl;
        oldDoc.FileSizeMb = pendingDoc.FileSizeMb;
        oldDoc.FileExtension = pendingDoc.FileExtension;
        oldDoc.Title = title;
        oldDoc.FolderId = folderId;
        oldDoc.SharingPermission = sharingPermission.ToUpper();
        oldDoc.UpdatedAt = _clock.Now;

        // Transfer extracted text
        var oldText = await _dbContext.DocumentExtractedTexts.FirstOrDefaultAsync(t => t.DocumentId == duplicateDocId);
        var pendingText = await _dbContext.DocumentExtractedTexts.FirstOrDefaultAsync(t => t.DocumentId == pendingDocId);
        if (oldText != null)
        {
            _dbContext.DocumentExtractedTexts.Remove(oldText);
        }
        if (pendingText != null)
        {
            pendingText.DocumentId = duplicateDocId;
        }

        // Delete pending document metadata
        _dbContext.Documents.Remove(pendingDoc);

        await _dbContext.SaveChangesAsync();
        return MapToDto(oldDoc, user.Username);
    }

    public async Task<DocumentResponseDto> KeepBothDocumentsAsync(int userId, int pendingDocId, string title, string sharingPermission, int? folderId)
    {
        sharingPermission = NormalizeSharingPermission(sharingPermission);
        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");
        var pendingDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == pendingDocId && d.UserId == userId);
        if (pendingDoc == null)
        {
            throw new ArgumentException("Tài liệu không tồn tại.");
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) throw new ArgumentException("Người dùng không tồn tại.");

        if ("PUBLIC".Equals(sharingPermission, StringComparison.OrdinalIgnoreCase) && 
            "SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            sharingPermission = "PRIVATE";
        }

        // Generate unique title
        string uniqueTitle = title;
        int counter = 1;
        while (await CheckDuplicateTitleAsync(userId, uniqueTitle, folderId, pendingDocId))
        {
            uniqueTitle = $"{title} ({counter})";
            counter++;
        }

        string newUrl = RenamePhysicalFile(pendingDoc.CloudStorageUrl, userId, uniqueTitle, pendingDoc.FileExtension);

        pendingDoc.Title = uniqueTitle;
        pendingDoc.FolderId = folderId;
        pendingDoc.SharingPermission = sharingPermission.ToUpper();
        pendingDoc.CloudStorageUrl = newUrl;
        pendingDoc.UpdatedAt = _clock.Now;

        await _dbContext.SaveChangesAsync();
        return MapToDto(pendingDoc, user.Username);
    }

    public async Task CancelUploadAsync(int userId, int pendingDocId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == pendingDocId && d.UserId == userId);
        if (doc != null)
        {
            DeletePhysicalFileByUrl(doc.CloudStorageUrl);
            _dbContext.Documents.Remove(doc);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<DocumentResponseDto>> GetUserDocumentsAsync(int userId, int? folderId)
    {
        var uploader = await _dbContext.Users.FindAsync(userId);
        string uploaderName = uploader?.Username ?? "N/A";

        var query = _dbContext.Documents.Where(d => d.UserId == userId);
        if (folderId.HasValue)
        {
            query = query.Where(d => d.FolderId == folderId.Value);
        }
        else
        {
            query = query.Where(d => d.FolderId == null);
        }

        var docs = await query.ToListAsync();
        return docs.Select(d => MapToDto(d, uploaderName)).ToList();
    }

    public async Task<List<DocumentResponseDto>> GetPublicDocumentsAsync()
    {
        var docs = await _dbContext.Documents
            .Include(d => d.User)
            .Where(d => d.SharingPermission == "PUBLIC" && d.IsFlagged == false)
            .ToListAsync();

        return docs.Select(d => MapToDto(d, d.User.Username)).ToList();
    }

    public async Task<DocumentResponseDto?> GetDocumentByIdAsync(int documentId)
    {
        var doc = await _dbContext.Documents.Include(d => d.User).FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) return null;
        return MapToDto(doc, doc.User.Username);
    }

    public async Task<bool> DeleteDocumentAsync(int userId, int documentId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId && (d.UserId == userId || _dbContext.Users.Any(u => u.UserId == userId && u.Role == "ADMIN")));
        if (doc != null)
        {
            DeletePhysicalFileByUrl(doc.CloudStorageUrl);
            _dbContext.Documents.Remove(doc);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        return false;
    }

    public async Task<string?> GetExtractedTextAsync(int documentId)
    {
        var text = await _dbContext.DocumentExtractedTexts.FirstOrDefaultAsync(t => t.DocumentId == documentId);
        return text?.ExtractedText;
    }

    public async Task<bool> CheckDuplicateTitleAsync(int userId, string title, int? folderId, int? excludeDocId)
    {
        var query = _dbContext.Documents.Where(d => d.UserId == userId && d.Title == title);
        if (folderId.HasValue)
        {
            query = query.Where(d => d.FolderId == folderId);
        }
        else
        {
            query = query.Where(d => d.FolderId == null);
        }

        if (excludeDocId.HasValue)
        {
            query = query.Where(d => d.DocumentId != excludeDocId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<List<DocumentReportResponseDto>> GetReportsAsync()
    {
        var reports = await _dbContext.DocumentReports
            .Include(r => r.Document)
            .Include(r => r.Reporter)
            .Include(r => r.ResolvedByAdmin)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return reports.Select(r => new DocumentReportResponseDto
        {
            ReportId = r.ReportId,
            DocumentId = r.DocumentId,
            DocumentTitle = r.Document.Title,
            ReporterId = r.ReporterId,
            ReporterName = r.Reporter.Username,
            ReasonCode = r.ReasonCode,
            AdditionalDetails = r.AdditionalDetails,
            Status = r.Status ?? "PENDING",
            CreatedAt = r.CreatedAt,
            ResolvedAt = r.ResolvedAt,
            ResolvedByAdminName = r.ResolvedByAdmin?.Username
        }).ToList();
    }

    public async Task<bool> ReportDocumentAsync(int reporterId, DocumentReportDto reportDto)
    {
        var doc = await _dbContext.Documents.FindAsync(reportDto.DocumentId);
        var reason = await _dbContext.ReportReasonConfigs.FindAsync(reportDto.ReasonCode);
        if (doc == null || reason == null) return false;
        if (doc.UserId == reporterId || doc.SharingPermission != "PUBLIC" || doc.IsFlagged == true) return false;
        if (await _dbContext.DocumentReports.AnyAsync(r => r.DocumentId == reportDto.DocumentId && r.ReporterId == reporterId))
            return false;

        var report = new DocumentReport
        {
            DocumentId = reportDto.DocumentId,
            ReporterId = reporterId,
            ReasonCode = reportDto.ReasonCode,
            AdditionalDetails = reportDto.AdditionalDetails,
            Status = "PENDING",
            CreatedAt = _clock.Now
        };

        _dbContext.DocumentReports.Add(report);

        // Update document's total report score
        doc.TotalReportScore = (doc.TotalReportScore ?? 0) + reason.BaseScore;

        // Auto-flag checking
        if (doc.TotalReportScore >= reason.AutoFlagThreshold)
        {
            doc.IsFlagged = true;
            doc.SharingPermission = "PRIVATE"; // Force private if flagged
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResolveReportAsync(int adminId, int reportId, string action)
    {
        var report = await _dbContext.DocumentReports.Include(r => r.Document).FirstOrDefaultAsync(r => r.ReportId == reportId);
        if (report == null || report.Status != "PENDING") return false;

        report.ResolvedByAdminId = adminId;
        report.ResolvedAt = _clock.Now;

        if ("DISMISS".Equals(action, StringComparison.OrdinalIgnoreCase))
        {
            report.Status = "DISMISSED";
        }
        else if ("TAKE_ACTION".Equals(action, StringComparison.OrdinalIgnoreCase))
        {
            report.Status = "ACTION_TAKEN";
            report.Document.IsFlagged = true;
            report.Document.SharingPermission = "PRIVATE";
        }
        else
        {
            return false;
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FILE MANAGEMENT HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private string SanitizeFileName(string fileName)
    {
        return Regex.Replace(fileName, @"[^a-zA-Z0-9.\-_]", "_");
    }

    private string StripExtension(string fileName)
    {
        int dotIndex = fileName.LastIndexOf('.');
        return (dotIndex > 0) ? fileName.Substring(0, dotIndex) : fileName;
    }

    private static string NormalizeSharingPermission(string sharingPermission)
    {
        var permission = sharingPermission?.Trim().ToUpperInvariant();
        if (permission is not ("PRIVATE" or "PUBLIC"))
            throw new ArgumentException("Invalid sharing permission.");
        return permission;
    }

    private void DeletePhysicalFileByUrl(string fileUrl)
    {
        try
        {
            string relativePath = fileUrl.TrimStart('/');
            _fileStorage.DeleteFile(relativePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting physical file: {ex.Message}");
        }
    }

    private string RenamePhysicalFile(string tempUrl, int userId, string finalTitle, string fileExtension)
    {
        string tempRelativePath = tempUrl.TrimStart('/');
        if (!_fileStorage.FileExists(tempRelativePath))
        {
            return tempUrl;
        }

        string sanitizedName = SanitizeFileName(finalTitle);
        string ext = fileExtension.StartsWith(".") ? fileExtension : $".{fileExtension}";

        string finalFileName = $"{sanitizedName}{ext}";
        string finalRelativePath = $"{UploadFolder}/{userId}/{finalFileName}";

        int dupCounter = 0;
        while (_fileStorage.FileExists(finalRelativePath))
        {
            dupCounter++;
            finalFileName = $"{sanitizedName}_dup{dupCounter}{ext}";
            finalRelativePath = $"{UploadFolder}/{userId}/{finalFileName}";
        }

        _fileStorage.MoveFile(tempRelativePath, finalRelativePath);

        return $"/{UploadFolder}/{userId}/{finalFileName}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEXT EXTRACTION LOGIC
    // ─────────────────────────────────────────────────────────────────────────

    private string ExtractTextFromFile(string filePath, string fileExtension)
    {
        string ext = fileExtension.ToLower().TrimStart('.');
        switch (ext)
        {
            case "txt":
            case "md":
                return File.ReadAllText(filePath, Encoding.UTF8);

            case "docx":
                return ExtractTextFromDocx(filePath);

            case "xlsx":
                return ExtractTextFromXlsx(filePath);

            case "pdf":
                return ExtractTextFromPdf(filePath);

            default:
                Console.WriteLine($"Unsupported file type for text extraction: {fileExtension}");
                return "";
        }
    }

    private string ExtractTextFromDocx(string filePath)
    {
        try
        {
            using (var zip = ZipFile.OpenRead(filePath))
            {
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null) return "";
                using (var stream = entry.Open())
                {
                    var doc = XDocument.Load(stream);
                    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    var texts = doc.Descendants(w + "t");
                    var sb = new StringBuilder();
                    foreach (var text in texts)
                    {
                        sb.Append(text.Value).Append(" ");
                    }
                    return sb.ToString().Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting DOCX: {ex.Message}");
            return "";
        }
    }

    private string ExtractTextFromXlsx(string filePath)
    {
        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("AIStudyHub");
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var sb = new StringBuilder();
                foreach (var sheet in package.Workbook.Worksheets)
                {
                    sb.AppendLine($"--- Worksheet: {sheet.Name} ---");
                    int rows = sheet.Dimension?.Rows ?? 0;
                    int cols = sheet.Dimension?.Columns ?? 0;
                    for (int r = 1; r <= rows; r++)
                    {
                        var rowVals = new List<string>();
                        for (int c = 1; c <= cols; c++)
                        {
                            var val = sheet.Cells[r, c].Value?.ToString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                rowVals.Add(val);
                            }
                        }
                        if (rowVals.Count > 0)
                        {
                            sb.AppendLine(string.Join("\t", rowVals));
                        }
                    }
                }
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting XLSX: {ex.Message}");
            return "";
        }
    }

    private string ExtractTextFromPdf(string filePath)
    {
        try
        {
            using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
            {
                var sb = new StringBuilder();
                foreach (var page in document.Pages)
                {
                    var contents = page.Contents;
                    if (contents != null)
                    {
                        foreach (var content in contents)
                        {
                            var text = ExtractTextFromPdfStream(content.Stream.Value);
                            sb.AppendLine(text);
                        }
                    }
                }
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting PDF: {ex.Message}");
            return "";
        }
    }

    private string ExtractTextFromPdfStream(byte[] streamBytes)
    {
        if (streamBytes == null || streamBytes.Length == 0) return "";
        string streamText = Encoding.UTF8.GetString(streamBytes);
        var sb = new StringBuilder();
        int index = 0;
        bool inParentheses = false;
        while (index < streamText.Length)
        {
            char c = streamText[index];
            if (c == '(' && !inParentheses)
            {
                inParentheses = true;
            }
            else if (c == ')' && inParentheses)
            {
                inParentheses = false;
                sb.Append(' ');
            }
            else if (inParentheses)
            {
                if (c == '\\' && index + 1 < streamText.Length)
                {
                    char next = streamText[index + 1];
                    if (next == '(' || next == ')' || next == '\\')
                    {
                        sb.Append(next);
                        index++;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            index++;
        }
        return sb.ToString().Trim();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DTO MAPPING
    // ─────────────────────────────────────────────────────────────────────────

    private DocumentResponseDto MapToDto(Document doc, string uploaderName)
    {
        return new DocumentResponseDto
        {
            DocumentId = doc.DocumentId,
            UserId = doc.UserId,
            UploaderName = uploaderName,
            FolderId = doc.FolderId,
            Title = doc.Title,
            FileExtension = doc.FileExtension,
            CloudStorageUrl = doc.CloudStorageUrl,
            FileSizeMb = doc.FileSizeMb,
            AiParsingStatus = doc.AiParsingStatus ?? "PENDING",
            SharingPermission = doc.SharingPermission ?? "PRIVATE",
            ShareLinkToken = doc.ShareLinkToken,
            TotalReportScore = doc.TotalReportScore,
            IsFlagged = doc.IsFlagged,
            BookmarkCount = doc.BookmarkCount,
            DownloadCount = doc.DownloadCount,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }
}
