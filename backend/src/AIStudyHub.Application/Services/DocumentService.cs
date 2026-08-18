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
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using UglyToad.PdfPig;

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
        if (!fileStream.CanRead)
            throw new ArgumentException("The uploaded file cannot be read.");

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
                .Where(d => d.UserId == userId && !d.IsDeleted)
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
            RequestedVisibility = "PRIVATE",
            ModerationStatus = "NOT_REQUESTED",
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
            if (_fileStorage.FileExists(relativeFilePath))
                _fileStorage.DeleteFile(relativeFilePath);
            throw;
        }

        // Extract text and save to DocumentExtractedText
        try
        {
            string savedPhysicalPath = _fileStorage.GetPhysicalPath(relativeFilePath);
            string extractedText = ExtractTextFromFile(savedPhysicalPath, fileExtension);
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                document.AiParsingStatus = "CHUNKING";
                var textEntity = new DocumentExtractedText
                {
                    DocumentId = document.DocumentId,
                    ExtractedText = extractedText,
                    CreatedAt = _clock.Now
                };
                _dbContext.DocumentExtractedTexts.Add(textEntity);
                _dbContext.DocumentChunks.AddRange(DocumentChunker.Chunk(document.DocumentId, extractedText, _clock.Now));
                document.AiParsingStatus = "READY";
                _dbContext.ModerationNotices.Add(new ModerationNotice
                {
                    UserId = userId,
                    DocumentId = document.DocumentId,
                    Type = "DOCUMENT_AI_READY",
                    Title = "Tài liệu đã sẵn sàng cho AI",
                    Message = $"Tài liệu “{document.Title}” đã tải lên và xử lý nội dung thành công. Bạn có thể bắt đầu chat với AI.",
                    ActionUrl = $"/chat?documentId={document.DocumentId}",
                    IsRead = false,
                    CreatedAt = _clock.Now
                });
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

    public async Task<DocumentResponseDto> ConfirmDocumentAsync(int userId, int documentId, string title, string subject, string sharingPermission, int? folderId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId && d.UserId == userId);
        if (doc == null)
        {
            throw new ArgumentException("Tài liệu không tồn tại hoặc bạn không có quyền.");
        }

        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");
        sharingPermission = NormalizeSharingPermission(sharingPermission);
        subject = NormalizeSubject(subject);

        // Check if user is suspended (then they cannot make new documents PUBLIC)
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            throw new ArgumentException("Người dùng không tồn tại.");

        if ("PUBLIC".Equals(sharingPermission, StringComparison.OrdinalIgnoreCase) &&
            "SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            sharingPermission = "PRIVATE";
        }
        if (sharingPermission == "PUBLIC")
            await EnsurePublicReviewAllowedAsync(doc.DocumentId);

        // Clean titles and handle duplicates
        string finalTitle = string.IsNullOrWhiteSpace(title) ? doc.Title : StripExtension(title);
        bool hasDuplicate = await CheckDuplicateTitleAsync(userId, finalTitle, doc.FileExtension, folderId, documentId);
        if (hasDuplicate)
        {
            throw new InvalidOperationException("Tiêu đề tài liệu đã tồn tại trong thư mục này.");
        }

        // Rename physical file from temp to final
        string oldUrl = doc.CloudStorageUrl;
        string newUrl = RenamePhysicalFile(oldUrl, userId, finalTitle, doc.FileExtension);

        doc.Title = finalTitle;
        doc.Subject = subject;
        doc.FolderId = folderId;
        ApplyRequestedVisibility(doc, sharingPermission);
        await AddModeratorDocumentNoticesAsync(doc);
        doc.CloudStorageUrl = newUrl;
        doc.UpdatedAt = _clock.Now;

        await _dbContext.SaveChangesAsync();
        return MapToDto(doc, user.Username);
    }

    public async Task<DocumentResponseDto> ReplaceDocumentAsync(int userId, int pendingDocId, int duplicateDocId, string title, string subject, string sharingPermission, int? folderId)
    {
        sharingPermission = NormalizeSharingPermission(sharingPermission);
        subject = NormalizeSubject(subject);
        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");
        var pendingDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == pendingDocId && d.UserId == userId);
        var oldDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == duplicateDocId && d.UserId == userId);
        if (pendingDoc == null || oldDoc == null)
        {
            throw new ArgumentException("Không tìm thấy tài liệu cần thiết.");
        }

        title = StripExtension(title).Trim();
        if (!oldDoc.Title.Equals(title, StringComparison.OrdinalIgnoreCase)
            || !oldDoc.FileExtension.Equals(pendingDoc.FileExtension, StringComparison.OrdinalIgnoreCase)
            || oldDoc.FolderId != folderId)
            throw new InvalidOperationException("Tài liệu được chọn thay thế không còn khớp tên, kiểu file hoặc thư mục.");

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            throw new ArgumentException("Người dùng không tồn tại.");

        if ("PUBLIC".Equals(sharingPermission, StringComparison.OrdinalIgnoreCase) &&
            "SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            sharingPermission = "PRIVATE";
        }
        if (sharingPermission == "PUBLIC")
            await EnsurePublicReviewAllowedAsync(oldDoc.DocumentId);

        // Delete old physical file
        DeletePhysicalFileByUrl(oldDoc.CloudStorageUrl);

        // Rename pending physical file to target final name
        string newUrl = RenamePhysicalFile(pendingDoc.CloudStorageUrl, userId, title, pendingDoc.FileExtension);

        // Replace metadata on the old document
        oldDoc.CloudStorageUrl = newUrl;
        oldDoc.FileSizeMb = pendingDoc.FileSizeMb;
        oldDoc.FileExtension = pendingDoc.FileExtension;
        oldDoc.Title = title;
        oldDoc.Subject = subject;
        oldDoc.FolderId = folderId;
        ApplyRequestedVisibility(oldDoc, sharingPermission);
        await AddModeratorDocumentNoticesAsync(oldDoc);
        oldDoc.UpdatedAt = _clock.Now;

        // Transfer extracted text
        var oldText = await _dbContext.DocumentExtractedTexts.FirstOrDefaultAsync(t => t.DocumentId == duplicateDocId);
        var pendingText = await _dbContext.DocumentExtractedTexts.FirstOrDefaultAsync(t => t.DocumentId == pendingDocId);
        var oldChunks = await _dbContext.DocumentChunks.Where(c => c.DocumentId == duplicateDocId).ToListAsync();
        var pendingChunks = await _dbContext.DocumentChunks.Where(c => c.DocumentId == pendingDocId).ToListAsync();
        _dbContext.DocumentChunks.RemoveRange(oldChunks);
        foreach (var chunk in pendingChunks)
            chunk.DocumentId = duplicateDocId;
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

    public async Task<DocumentResponseDto> KeepBothDocumentsAsync(int userId, int pendingDocId, string title, string subject, string sharingPermission, int? folderId)
    {
        sharingPermission = NormalizeSharingPermission(sharingPermission);
        subject = NormalizeSubject(subject);
        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Invalid destination folder.");
        var pendingDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == pendingDocId && d.UserId == userId);
        if (pendingDoc == null)
        {
            throw new ArgumentException("Tài liệu không tồn tại.");
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            throw new ArgumentException("Người dùng không tồn tại.");

        if ("PUBLIC".Equals(sharingPermission, StringComparison.OrdinalIgnoreCase) &&
            "SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            sharingPermission = "PRIVATE";
        }

        // Generate unique title
        title = StripExtension(title).Trim();
        string uniqueTitle = title;
        int counter = 1;
        while (await CheckDuplicateTitleAsync(userId, uniqueTitle, pendingDoc.FileExtension, folderId, pendingDocId))
        {
            uniqueTitle = $"{title}({counter})";
            counter++;
        }

        string newUrl = RenamePhysicalFile(pendingDoc.CloudStorageUrl, userId, uniqueTitle, pendingDoc.FileExtension);

        pendingDoc.Title = uniqueTitle;
        pendingDoc.Subject = subject;
        pendingDoc.FolderId = folderId;
        ApplyRequestedVisibility(pendingDoc, sharingPermission);
        await AddModeratorDocumentNoticesAsync(pendingDoc);
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

        IQueryable<Document> query;
        if (folderId.HasValue)
        {
            query = _dbContext.Documents.Where(d => d.FolderId == folderId.Value && !d.IsDeleted);
        }
        else
        {
            query = _dbContext.Documents.Where(d => d.UserId == userId && d.FolderId == null && !d.IsDeleted);
        }

        var docs = await query.ToListAsync();
        var documentIds = docs.Select(d => d.DocumentId).ToList();
        var appealableDocumentIds = await _dbContext.DocumentReports.AsNoTracking()
            .Where(r => documentIds.Contains(r.DocumentId) &&
                (r.Status == "VIOLATION_CONFIRMED" || r.Status == "ACTION_TAKEN" || r.Status == "ACTIONED") &&
                !_dbContext.ModerationAppeals.Any(a => a.ReportId == r.ReportId))
            .Select(r => r.DocumentId).ToListAsync();
        var appealable = appealableDocumentIds.ToHashSet();
        var appealStates = await GetAppealStatesAsync(documentIds);
        return docs.Select(d =>
        {
            var dto = MapToDto(d, uploaderName);
            dto.RequiresAppeal = appealable.Contains(d.DocumentId);
            if (appealStates.TryGetValue(d.DocumentId, out var appealState))
            {
                dto.AppealStatus = appealState;
                dto.PublicReviewBlocked = appealState != "RESTORED";
            }
            return dto;
        }).ToList();
    }

    public async Task<List<DocumentResponseDto>> GetPublicDocumentsAsync()
    {
        var docs = await _dbContext.Documents
            .Include(d => d.User)
            .Where(d => d.SharingPermission == "PUBLIC" && d.IsFlagged == false && !d.IsDeleted)
            .ToListAsync();

        return docs.Select(d => MapToDto(d, d.User.Username)).ToList();
    }

    public async Task<DocumentResponseDto?> GetDocumentByIdAsync(int documentId)
    {
        var doc = await _dbContext.Documents.Include(d => d.User).FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null)
            return null;
        var dto = MapToDto(doc, doc.User.Username);
        dto.RequiresAppeal = await _dbContext.DocumentReports.AsNoTracking().AnyAsync(r =>
            r.DocumentId == documentId &&
            (r.Status == "VIOLATION_CONFIRMED" || r.Status == "ACTION_TAKEN" || r.Status == "ACTIONED") &&
            !_dbContext.ModerationAppeals.Any(a => a.ReportId == r.ReportId));
        var appealStates = await GetAppealStatesAsync([documentId]);
        if (appealStates.TryGetValue(documentId, out var appealState))
        {
            dto.AppealStatus = appealState;
            dto.PublicReviewBlocked = appealState != "RESTORED";
        }
        return dto;
    }

    public async Task<bool> DeleteDocumentAsync(int userId, int documentId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId && (d.UserId == userId || _dbContext.Users.Any(u => u.UserId == userId && u.Role == "ADMIN")));
        if (doc != null)
        {
            doc.IsDeleted = true;
            doc.DeletedAt = _clock.Now;
            doc.DeletedByUserId = userId;
            doc.LifeCycleStatus = "TRASHED";
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

    public async Task<bool> CheckDuplicateTitleAsync(int userId, string title, string fileExtension, int? folderId, int? excludeDocId)
    {
        var normalizedTitle = StripExtension(title).Trim();
        var normalizedExtension = fileExtension.Trim().TrimStart('.').ToLowerInvariant();
        var query = _dbContext.Documents.Where(d => d.UserId == userId
            && d.Title == normalizedTitle
            && d.FileExtension == normalizedExtension);
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
            ResolvedByAdminName = r.ResolvedByAdmin?.Username,
            ReportType = r.ReportType,
            ClaimantName = r.ClaimantName,
            ClaimantEmail = r.ClaimantEmail,
            OriginalWorkUrl = r.OriginalWorkUrl,
            EvidenceDescription = r.EvidenceDescription,
            ModeratorNote = r.ModeratorNote,
            AssignedModeratorId = r.AssignedModeratorId,
            PreviousSharingPermission = r.PreviousSharingPermission,
            RestrictedAt = r.RestrictedAt
        }).ToList();
    }

    public async Task<bool> ReportDocumentAsync(int reporterId, DocumentReportDto reportDto)
    {
        var doc = await _dbContext.Documents.FindAsync(reportDto.DocumentId);
        var reason = await _dbContext.ReportReasonConfigs.FindAsync(reportDto.ReasonCode);
        if (doc == null || reason == null)
            return false;
        if (doc.UserId == reporterId || doc.SharingPermission != "PUBLIC" || doc.IsFlagged == true)
            return false;
        if (await _dbContext.DocumentReports.AnyAsync(r => r.DocumentId == reportDto.DocumentId && r.ReporterId == reporterId))
            return false;

        var reportType = reportDto.ReportType?.Trim().ToUpperInvariant() == "COPYRIGHT" ? "COPYRIGHT" : "COMMUNITY";
        if (reportType == "COPYRIGHT" && (!reportDto.InformationConfirmed || string.IsNullOrWhiteSpace(reportDto.ClaimantName)
            || string.IsNullOrWhiteSpace(reportDto.ClaimantEmail) || string.IsNullOrWhiteSpace(reportDto.EvidenceDescription)))
            return false;
        var report = new DocumentReport
        {
            DocumentId = reportDto.DocumentId,
            ReporterId = reporterId,
            ReasonCode = reportDto.ReasonCode,
            AdditionalDetails = reportDto.AdditionalDetails,
            ReportType = reportType,
            ClaimantName = reportDto.ClaimantName?.Trim(),
            ClaimantEmail = reportDto.ClaimantEmail?.Trim(),
            OriginalWorkUrl = reportDto.OriginalWorkUrl?.Trim(),
            EvidenceDescription = reportDto.EvidenceDescription?.Trim(),
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
        var moderators = await _dbContext.Users.Where(u => u.Role == "MODERATOR" && u.Status == "ACTIVE").Select(u => u.UserId).ToListAsync();
        _dbContext.ModerationNotices.AddRange(moderators.Select(moderatorId => new ModerationNotice
        {
            UserId = moderatorId,
            DocumentId = report.DocumentId,
            ReportId = report.ReportId,
            Type = "REPORT_PENDING",
            Title = "Báo cáo tài liệu mới",
            Message = $"Tài liệu “{doc.Title}” vừa nhận một báo cáo {reportType.ToLowerInvariant()} cần xử lý.",
            ActionUrl = $"/moderator?tab=reports&reportId={report.ReportId}",
            IsRead = false,
            CreatedAt = _clock.Now
        }));
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<List<PublicReportReasonDto>> GetReportReasonsAsync()
    {
        return await _dbContext.ReportReasonConfigs
            .OrderBy(r => r.SeverityLevel)
            .ThenBy(r => r.ReasonCode)
            .Select(r => new PublicReportReasonDto
            {
                ReasonCode = r.ReasonCode,
                SeverityLevel = r.SeverityLevel,
                Description = string.IsNullOrWhiteSpace(r.Description) ? r.ReasonCode : r.Description
            }).ToListAsync();
    }

    public async Task<bool> ResolveReportAsync(int adminId, int reportId, string action)
    {
        var report = await _dbContext.DocumentReports.Include(r => r.Document).FirstOrDefaultAsync(r => r.ReportId == reportId);
        if (report == null || report.Status != "PENDING")
            return false;

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

    public async Task<List<int>> GetBookmarkedDocumentIdsAsync(int userId)
    {
        return await _dbContext.Bookmarks
            .Where(bookmark => bookmark.UserId == userId)
            .Select(bookmark => bookmark.DocumentId)
            .ToListAsync();
    }

    public async Task<int?> AddBookmarkAsync(int userId, int documentId)
    {
        var document = await _dbContext.Documents.FirstOrDefaultAsync(item =>
            item.DocumentId == documentId && item.SharingPermission == "PUBLIC" && item.IsFlagged == false);
        if (document == null)
            return null;

        var exists = await _dbContext.Bookmarks.AnyAsync(bookmark =>
            bookmark.UserId == userId && bookmark.DocumentId == documentId);
        if (!exists)
        {
            _dbContext.Bookmarks.Add(new Bookmark
            {
                UserId = userId,
                DocumentId = documentId,
                CreatedAt = _clock.Now
            });
            document.BookmarkCount = (document.BookmarkCount ?? 0) + 1;
            await _dbContext.SaveChangesAsync();
        }

        return document.BookmarkCount ?? 0;
    }

    public async Task<int?> RemoveBookmarkAsync(int userId, int documentId)
    {
        var document = await _dbContext.Documents.FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null)
            return null;

        var bookmark = await _dbContext.Bookmarks.FirstOrDefaultAsync(item =>
            item.UserId == userId && item.DocumentId == documentId);
        if (bookmark != null)
        {
            _dbContext.Bookmarks.Remove(bookmark);
            document.BookmarkCount = Math.Max(0, (document.BookmarkCount ?? 0) - 1);
            await _dbContext.SaveChangesAsync();
        }

        return document.BookmarkCount ?? 0;
    }

    public async Task<int?> IncrementDownloadCountAsync(int documentId, int userId)
    {
        var document = await _dbContext.Documents.FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null)
            return null;
        document.DownloadCount = (document.DownloadCount ?? 0) + 1;
        _dbContext.DocumentActivities.Add(new DocumentActivity { DocumentId = documentId, UserId = userId, ActivityType = "DOWNLOAD", CreatedAt = _clock.Now });
        await _dbContext.SaveChangesAsync();
        return document.DownloadCount;
    }

    public async Task<int?> IncrementViewCountAsync(int documentId, int userId)
    {
        var document = await _dbContext.Documents.FirstOrDefaultAsync(item => item.DocumentId == documentId);
        if (document == null)
            return null;
        if (document.UserId != userId)
        {
            document.ViewCount = (document.ViewCount ?? 0) + 1;
            _dbContext.DocumentActivities.Add(new DocumentActivity { DocumentId = documentId, UserId = userId, ActivityType = "VIEW", CreatedAt = _clock.Now });
            await _dbContext.SaveChangesAsync();
        }
        return document.ViewCount ?? 0;
    }

    public async Task<DocumentAnalyticsDto> GetUserAnalyticsAsync(int userId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        var allDocuments = await _dbContext.Documents.Where(d => d.UserId == userId).OrderByDescending(d => d.CreatedAt).ToListAsync();
        var documents = allDocuments.Where(d => d.SharingPermission == "PUBLIC").ToList();
        var pending = allDocuments.Where(d => d.ModerationStatus is "PENDING_REVIEW" or "IN_REVIEW").ToList();
        return new DocumentAnalyticsDto
        {
            TotalDocuments = documents.Count,
            PublicDocuments = documents.Count,
            PrivateDocuments = 0,
            TotalDownloads = documents.Sum(d => d.DownloadCount ?? 0),
            TotalViews = documents.Sum(d => d.ViewCount ?? 0),
            TotalBookmarks = documents.Sum(d => d.BookmarkCount ?? 0),
            Documents = documents.Select(d => MapToDto(d, user?.Username ?? "N/A")).ToList(),
            PendingReviewCount = pending.Count,
            PendingReviewDocuments = pending.Select(d => MapToDto(d, user?.Username ?? "N/A")).ToList()
        };
    }

    public async Task<DocumentDetailDto?> GetDocumentDetailAsync(int documentId)
    {
        var document = await _dbContext.Documents.Include(d => d.User).Include(d => d.DocumentExtractedText).FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document == null)
            return null;
        var activities = await _dbContext.DocumentActivities.Include(a => a.User).Where(a => a.DocumentId == documentId).ToListAsync();
        var audience = activities.GroupBy(a => new { a.UserId, a.User.Username, a.User.Email }).Select(group => new DocumentAudienceDto
        {
            UserId = group.Key.UserId,
            Username = group.Key.Username,
            Email = group.Key.Email,
            DownloadCount = group.Count(a => a.ActivityType == "DOWNLOAD"),
            ViewCount = group.Count(a => a.ActivityType == "VIEW"),
            LastActivityAt = group.Max(a => a.CreatedAt)
        }).OrderByDescending(a => a.LastActivityAt).ToList();
        var extracted = document.DocumentExtractedText?.ExtractedText?.Trim();
        return new DocumentDetailDto
        {
            Document = MapToDto(document, document.User.Username),
            Description = string.IsNullOrWhiteSpace(extracted) ? "Chưa có mô tả hoặc nội dung trích xuất." : extracted[..Math.Min(600, extracted.Length)],
            Audience = audience
        };
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
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        var nameOnly = System.IO.Path.GetFileName(fileName);
        int dotIndex = nameOnly.LastIndexOf('.');
        return (dotIndex > 0) ? nameOnly.Substring(0, dotIndex) : nameOnly;
    }

    private static string NormalizeSharingPermission(string sharingPermission)
    {
        var permission = sharingPermission?.Trim().ToUpperInvariant();
        if (permission is not ("PRIVATE" or "PUBLIC"))
            throw new ArgumentException("Invalid sharing permission.");
        return permission;
    }

    private static void ApplyRequestedVisibility(Document document, string permission)
    {
        document.RequestedVisibility = permission;
        document.SharingPermission = "PRIVATE";
        document.ModerationStatus = permission == "PUBLIC" ? "PENDING_REVIEW" : "NOT_REQUESTED";
        document.ModerationSubmittedAt = permission == "PUBLIC" ? DateTime.Now : null;
        document.ModerationNote = null;
    }

    private async Task EnsurePublicReviewAllowedAsync(int documentId)
    {
        var state = (await GetAppealStatesAsync([documentId])).GetValueOrDefault(documentId);
        if (state == null || state == "RESTORED")
            return;
        if (state == "NOT_SUBMITTED")
            throw new InvalidOperationException("Tài liệu đã bị xác nhận vi phạm. Bạn phải gửi giải trình trước khi xin duyệt công khai.");
        if (state == "PENDING")
            throw new InvalidOperationException("Giải trình đang chờ Moderator giải quyết. Chưa thể xin duyệt công khai.");
        throw new InvalidOperationException("Moderator đã giữ nguyên quyết định vi phạm. Tài liệu này chưa đủ điều kiện xin duyệt công khai.");
    }

    private async Task<Dictionary<int, string>> GetAppealStatesAsync(IReadOnlyCollection<int> documentIds)
    {
        var violations = await _dbContext.DocumentReports.AsNoTracking()
            .Where(r => documentIds.Contains(r.DocumentId) &&
                (r.Status == "VIOLATION_CONFIRMED" || r.Status == "ACTION_TAKEN" || r.Status == "ACTIONED" ||
                 r.Status == "APPEALED" || r.Status == "CLOSED" || r.Status == "RESTORED"))
            .Select(r => new
            {
                r.DocumentId,
                r.ResolvedAt,
                AppealStatus = _dbContext.ModerationAppeals.Where(a => a.ReportId == r.ReportId)
                    .Select(a => a.Status).FirstOrDefault()
            }).ToListAsync();
        return violations.GroupBy(item => item.DocumentId).ToDictionary(group => group.Key,
            group => group.OrderByDescending(item => item.ResolvedAt).First().AppealStatus ?? "NOT_SUBMITTED");
    }

    private async Task AddModeratorDocumentNoticesAsync(Document document)
    {
        if (document.ModerationStatus != "PENDING_REVIEW")
            return;
        var moderators = await _dbContext.Users
            .Where(user => user.Role == "MODERATOR" && user.Status == "ACTIVE")
            .Select(user => user.UserId)
            .ToListAsync();
        _dbContext.ModerationNotices.AddRange(moderators.Select(moderatorId => new ModerationNotice
        {
            UserId = moderatorId,
            DocumentId = document.DocumentId,
            Type = "DOCUMENT_REVIEW_PENDING",
            Title = "Tài liệu mới cần xét duyệt",
            Message = $"Tài liệu “{document.Title}” vừa yêu cầu công khai và đang chờ xét duyệt.",
            ActionUrl = $"/moderator?tab=queue&documentId={document.DocumentId}",
            IsRead = false,
            CreatedAt = _clock.Now
        }));
    }

    private static string NormalizeSubject(string? subject)
    {
        var value = string.IsNullOrWhiteSpace(subject) ? "Khác" : subject.Trim();
        if (value.Length > 100)
            throw new ArgumentException("Môn học không được dài quá 100 ký tự.");
        return value;
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

            case "pptx":
                return ExtractTextFromPptx(filePath);

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
                if (entry == null)
                    return "";
                using (var stream = entry.Open())
                {
                    var doc = XDocument.Load(stream);
                    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    var sb = new StringBuilder();
                    foreach (var paragraph in doc.Descendants(w + "p"))
                    {
                        var value = string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value)).Trim();
                        if (value.Length == 0)
                            continue;
                        var style = paragraph.Descendants(w + "pStyle").FirstOrDefault()?.Attribute(w + "val")?.Value;
                        if (style?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true
                            && int.TryParse(Regex.Match(style, @"\d+").Value, out var level))
                            value = $"{new string('#', Math.Clamp(level, 1, 6))} {value}";
                        sb.AppendLine(value).AppendLine();
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
            using (var document = PdfDocument.Open(filePath))
            {
                var sb = new StringBuilder();
                foreach (var page in document.GetPages())
                {
                    sb.AppendLine($"[PAGE {page.Number}]");
                    var words = page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom)
                        .ThenBy(w => w.BoundingBox.Left).ToList();
                    if (words.Count == 0)
                        continue;
                    var lines = words.GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4.0) * 4)
                        .OrderByDescending(group => group.Key)
                        .Select(group => string.Join(" ", group.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
                    foreach (var line in lines)
                        sb.AppendLine(line);
                    sb.AppendLine();
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

    private string ExtractTextFromPptx(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var slides = archive.Entries
                .Where(entry => Regex.IsMatch(entry.FullName, @"^ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
                .OrderBy(entry => int.Parse(Regex.Match(entry.Name, @"\d+").Value));
            var sb = new StringBuilder();
            foreach (var slide in slides)
            {
                using var stream = slide.Open();
                var xml = XDocument.Load(stream);
                var text = string.Join(" ", xml.Descendants(drawing + "t").Select(node => node.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var slideNumber = int.Parse(Regex.Match(slide.Name, @"\d+").Value);
                    sb.AppendLine($"[PAGE {slideNumber}]").AppendLine(text).AppendLine();
                }
            }
            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting PPTX: {ex.Message}");
            return "";
        }
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
            Subject = doc.Subject,
            FileExtension = doc.FileExtension,
            CloudStorageUrl = doc.CloudStorageUrl,
            FileAvailable = IsFileAvailable(doc.CloudStorageUrl),
            FileSizeMb = doc.FileSizeMb,
            AiParsingStatus = doc.AiParsingStatus ?? "PENDING",
            SharingPermission = doc.SharingPermission ?? "PRIVATE",
            RequestedVisibility = doc.RequestedVisibility,
            ModerationStatus = doc.ModerationStatus,
            ModerationNote = doc.ModerationNote,
            ModerationSubmittedAt = doc.ModerationSubmittedAt,
            ModeratedAt = doc.ModeratedAt,
            ShareLinkToken = doc.ShareLinkToken,
            TotalReportScore = doc.TotalReportScore,
            IsFlagged = doc.IsFlagged,
            BookmarkCount = doc.BookmarkCount,
            DownloadCount = doc.DownloadCount,
            ViewCount = doc.ViewCount,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }

    private bool IsFileAvailable(string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return false;
        try
        {
            return _fileStorage.FileExists(fileUrl.TrimStart('/'));
        }
        catch { return false; }
    }

    public async Task ProcessExtractionAsync(int documentId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) return;

        try
        {
            doc.AiParsingStatus = "PROCESSING";
            await _dbContext.SaveChangesAsync();

            // Perform extraction logic if file exists
            if (IsFileAvailable(doc.CloudStorageUrl))
            {
                // Simple extraction or text simulation
                var existingText = await _dbContext.DocumentExtractedTexts.FirstOrDefaultAsync(e => e.DocumentId == documentId);
                if (existingText == null)
                {
                    _dbContext.DocumentExtractedTexts.Add(new DocumentExtractedText
                    {
                        DocumentId = documentId,
                        ExtractedText = $"Trích xuất văn bản tự động cho tài liệu {doc.Title}",
                        CreatedAt = _clock.Now
                    });
                }
            }

            doc.AiParsingStatus = "READY";
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception)
        {
            doc.AiParsingStatus = "FAILED";
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task RetryExtractionAsync(int documentId, int userId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Document not found");
        if (doc.UserId != userId) throw new UnauthorizedAccessException("Only owner can retry extraction");

        doc.AiParsingStatus = "PROCESSING";
        await _dbContext.SaveChangesAsync();

        await ProcessExtractionAsync(documentId);
    }

    public async Task<StorageQuotaDto> GetUserStorageQuotaAsync(int userId)
    {
        var user = await _dbContext.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        double usedMb = await _dbContext.Documents
            .Where(d => d.UserId == userId && !d.IsDeleted)
            .SumAsync(d => (double?)d.FileSizeMb) ?? 0.0;

        decimal maxStorageMb = user.Tier?.MaxStorageMb ?? 50m;
        string tierName = user.Tier?.TierName ?? "Free";

        return new StorageQuotaDto
        {
            UserId = userId,
            TierName = tierName,
            UsedStorageMb = Math.Round((decimal)usedMb, 2),
            MaxStorageMb = maxStorageMb,
            AiPromptsToday = user.AiPromptsToday ?? 0,
            AiPromptLimitPerDay = user.Tier?.AiPromptLimitPerDay ?? 10
        };
    }

    public async Task<PagedResult<DocumentResponseDto>> GetMyDocumentsPagedAsync(int userId, int? folderId, int pageNumber, int pageSize, string? search, string? subject)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        string uploaderName = user?.Username ?? "Unknown";

        var query = _dbContext.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId && !d.IsDeleted);

        if (folderId.HasValue)
        {
            query = query.Where(d => d.FolderId == folderId.Value);
        }
        else
        {
            query = query.Where(d => d.FolderId == null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d => d.Title.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(subject) && subject != "ALL")
        {
            query = query.Where(d => d.Subject == subject);
        }

        int totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(d => MapToDto(d, uploaderName)).ToList();
        return new PagedResult<DocumentResponseDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task<PagedResult<DocumentResponseDto>> GetSharedWithMePagedAsync(int userId, int pageNumber, int pageSize)
    {
        var sharesQuery = _dbContext.DocumentShares
            .AsNoTracking()
            .Include(s => s.Document)
                .ThenInclude(d => d.User)
            .Where(s => s.SharedWithUserId == userId && !s.Document.IsDeleted);

        int totalCount = await sharesQuery.CountAsync();
        var shares = await sharesQuery
            .OrderByDescending(s => s.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = shares.Select(s => MapToDto(s.Document, s.Document.User.Username)).ToList();
        return new PagedResult<DocumentResponseDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task<PagedResult<DocumentResponseDto>> GetBookmarksPagedAsync(int userId, int pageNumber, int pageSize)
    {
        var bookmarksQuery = _dbContext.Bookmarks
            .AsNoTracking()
            .Include(b => b.Document)
                .ThenInclude(d => d.User)
            .Where(b => b.UserId == userId && !b.Document.IsDeleted);

        int totalCount = await bookmarksQuery.CountAsync();
        var bookmarks = await bookmarksQuery
            .OrderByDescending(b => b.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = bookmarks.Select(b => MapToDto(b.Document, b.Document.User.Username)).ToList();
        return new PagedResult<DocumentResponseDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task BulkDeleteDocumentsAsync(List<int> documentIds, int userId)
    {
        var docs = await _dbContext.Documents.Where(d => documentIds.Contains(d.DocumentId) && d.UserId == userId).ToListAsync();
        foreach (var doc in docs)
        {
            doc.IsDeleted = true;
            doc.DeletedAt = _clock.Now;
            doc.DeletedByUserId = userId;
            doc.LifeCycleStatus = "TRASHED";
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task BulkMoveDocumentsAsync(List<int> documentIds, int? targetFolderId, int userId)
    {
        if (targetFolderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == targetFolderId.Value && f.UserId == userId))
        {
            throw new UnauthorizedAccessException("Target folder invalid or access denied");
        }

        var docs = await _dbContext.Documents.Where(d => documentIds.Contains(d.DocumentId) && d.UserId == userId).ToListAsync();
        foreach (var doc in docs)
        {
            doc.FolderId = targetFolderId;
            doc.UpdatedAt = _clock.Now;
        }
        await _dbContext.SaveChangesAsync();
    }
}
