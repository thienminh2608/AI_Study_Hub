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
    private readonly IDocumentProcessingQueue _queue;
    private readonly IGeminiService _geminiService;
    private readonly ISubjectService _subjectService;
    private const string UploadFolder = "uploads";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "pdf", "docx", "txt", "xlsx", "pptx", "md", "png", "jpg", "jpeg", "webp", "bmp", "gif" };
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;

    public DocumentService(IStudyHubDbContext dbContext, IFileStorage fileStorage, IClock clock, IDocumentProcessingQueue queue, IGeminiService geminiService, ISubjectService subjectService)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _clock = clock;
        _queue = queue;
        _geminiService = geminiService;
        _subjectService = subjectService;
    }

    public async Task<DocumentResponseDto> UploadDocumentAsync(int userId, int? folderId, string originalFileName, string fileExtension, long fileSizeInBytes, Stream fileStream)
    {
        fileExtension = fileExtension.Trim().TrimStart('.').ToLowerInvariant();
        if (!AllowedExtensions.Contains(fileExtension))
            throw new ArgumentException("Định dạng tệp không được hỗ trợ.");
        if (fileSizeInBytes <= 0 || fileSizeInBytes > MaxFileSizeBytes)
            throw new ArgumentException("Dung lượng tệp phải từ 1 byte đến 50 MB.");
        if (!fileStream.CanRead)
            throw new ArgumentException("Không thể đọc luồng tệp tải lên.");

        // Security Magic Bytes & OOXML Zip structure verification
        FileSecurityValidator.ValidateFile(fileStream, fileExtension);

        double fileSizeMb = fileSizeInBytes / (1024.0 * 1024.0);
        fileSizeMb = Math.Round(fileSizeMb, 2);

        // Check storage quota for non-Premium users
        var user = await _dbContext.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
        {
            throw new ArgumentException("Người dùng không tồn tại.");
        }

        if (folderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == folderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Thư mục đích không hợp lệ hoặc không có quyền truy cập.");

        // If not Premium tier
        if (user.TierId < 3)
        {
            var currentStorageUsed = await _dbContext.Documents
                .Where(d => d.UserId == userId && !d.IsDeleted)
                .SumAsync(d => (decimal?)d.FileSizeMb) ?? 0m;

            decimal maxStorageMb = user.Tier?.MaxStorageMb ?? 50m; // Free defaults to 50MB, guest 0

            if (currentStorageUsed + (decimal)fileSizeMb > maxStorageMb)
            {
                throw new InvalidOperationException($"Không đủ dung lượng trống! Đã dùng {currentStorageUsed:F2} MB / {maxStorageMb:F2} MB. File tải lên: {fileSizeMb:F2} MB.");
            }
        }

        // Ensure stream is at start before saving
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
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

            // Enqueue durable background processing job
            await _queue.EnqueueJobAsync(document.DocumentId);
        }
        catch
        {
            if (_fileStorage.FileExists(relativeFilePath))
                _fileStorage.DeleteFile(relativeFilePath);
            throw;
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
        subject = await _subjectService.CreateOrResolveSubjectAsync(NormalizeSubject(subject), userId);

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
        await SyncRenamedVersionPathsAsync(doc.DocumentId, oldUrl, newUrl);

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
        subject = await _subjectService.CreateOrResolveSubjectAsync(NormalizeSubject(subject), userId);
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

        string originalPendingUrl = pendingDoc.CloudStorageUrl;
        string newUrl = RenamePhysicalFile(pendingDoc.CloudStorageUrl, userId, title, pendingDoc.FileExtension);
        bool fileMoved = !string.Equals(newUrl, originalPendingUrl, StringComparison.OrdinalIgnoreCase);

        try
        {
            using var tx = await _dbContext.Database.BeginTransactionAsync();

            // 1. Ensure baseline version exists for oldDoc if missing
            if (!oldDoc.CurrentVersionId.HasValue || oldDoc.CurrentVersionId.Value == 0)
            {
                var existingBaseline = await _dbContext.DocumentVersions
                    .FirstOrDefaultAsync(v => v.DocumentId == duplicateDocId && v.VersionNumber == 1);
                if (existingBaseline == null)
                {
                    var baseline = new DocumentVersion
                    {
                        DocumentId = duplicateDocId,
                        VersionNumber = 1,
                        CloudStorageUrl = oldDoc.CloudStorageUrl,
                        FileExtension = oldDoc.FileExtension,
                        FileSizeMb = oldDoc.FileSizeMb,
                        ChangeSummary = "Phiên bản gốc trước khi cập nhật thay thế",
                        CreatedByUserId = oldDoc.UserId,
                        CreatedAt = oldDoc.CreatedAt ?? _clock.UtcNow
                    };
                    _dbContext.DocumentVersions.Add(baseline);
                    await _dbContext.SaveChangesAsync();
                    oldDoc.CurrentVersionId = baseline.VersionId;
                }
                else
                {
                    oldDoc.CurrentVersionId = existingBaseline.VersionId;
                }
            }

            // 2. Compute next VersionNumber
            int maxVersionNumber = await _dbContext.DocumentVersions
                .Where(v => v.DocumentId == duplicateDocId)
                .MaxAsync(v => (int?)v.VersionNumber) ?? 1;
            int newVersionNumber = maxVersionNumber + 1;

            // 3. Create new DocumentVersion for duplicateDocId
            var newVersion = new DocumentVersion
            {
                DocumentId = duplicateDocId,
                VersionNumber = newVersionNumber,
                CloudStorageUrl = newUrl,
                FileExtension = pendingDoc.FileExtension,
                FileSizeMb = pendingDoc.FileSizeMb,
                ChangeSummary = "Thay thế tài liệu trùng lặp (Cập nhật phiên bản mới)",
                CreatedByUserId = userId,
                CreatedAt = _clock.UtcNow
            };
            _dbContext.DocumentVersions.Add(newVersion);
            await _dbContext.SaveChangesAsync();

            // 4. Update old document metadata & point CurrentVersionId to newVersion
            oldDoc.CurrentVersionId = newVersion.VersionId;
            oldDoc.CloudStorageUrl = newUrl;
            oldDoc.FileSizeMb = pendingDoc.FileSizeMb;
            oldDoc.FileExtension = pendingDoc.FileExtension;
            oldDoc.Title = title;
            oldDoc.Subject = subject;
            oldDoc.FolderId = folderId;
            ApplyRequestedVisibility(oldDoc, sharingPermission);
            await AddModeratorDocumentNoticesAsync(oldDoc);
            oldDoc.UpdatedAt = _clock.Now;

            // 5. Transfer extracted text and chunks from pending document to the new version of duplicateDocId
            var pendingTexts = await _dbContext.DocumentExtractedTexts.Where(t => t.DocumentId == pendingDocId).ToListAsync();
            var pendingChunks = await _dbContext.DocumentChunks.Where(c => c.DocumentId == pendingDocId).ToListAsync();
            foreach (var chunk in pendingChunks)
            {
                chunk.DocumentId = duplicateDocId;
                chunk.DocumentVersionId = newVersion.VersionId;
            }
            foreach (var text in pendingTexts)
            {
                text.DocumentId = duplicateDocId;
                text.DocumentVersionId = newVersion.VersionId;
            }

            // 6. Remove pending document metadata
            _dbContext.Documents.Remove(pendingDoc);

            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();

            return MapToDto(oldDoc, user.Username);
        }
        catch
        {
            if (fileMoved)
            {
                try
                {
                    string newRel = newUrl.TrimStart('/');
                    string origRel = originalPendingUrl.TrimStart('/');
                    _fileStorage.MoveFile(newRel, origRel);
                }
                catch (Exception compEx)
                {
                    Console.WriteLine($"[ReplaceCompensation] Revert file move failed: {compEx.Message}");
                }
            }
            throw;
        }
    }

    public async Task<DocumentResponseDto> KeepBothDocumentsAsync(int userId, int pendingDocId, string title, string subject, string sharingPermission, int? folderId)
    {
        sharingPermission = NormalizeSharingPermission(sharingPermission);
        subject = await _subjectService.CreateOrResolveSubjectAsync(NormalizeSubject(subject), userId);
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

        string oldUrl = pendingDoc.CloudStorageUrl;
        string newUrl = RenamePhysicalFile(oldUrl, userId, uniqueTitle, pendingDoc.FileExtension);
        await SyncRenamedVersionPathsAsync(pendingDoc.DocumentId, oldUrl, newUrl);

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

    public async Task<PagedResult<DocumentResponseDto>> GetPublicDocumentsPagedAsync(int pageNumber, int pageSize, string? search, string? subject, List<string>? extensions, string? sortBy, string? sortDirection)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _dbContext.Documents
            .AsNoTracking()
            .Include(d => d.User)
            .Where(d => d.SharingPermission == "PUBLIC" && d.IsFlagged == false && !d.IsDeleted);

        if (!string.IsNullOrWhiteSpace(subject) && !subject.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var descendantSubjects = _subjectService != null
                ? await _subjectService.GetDescendantSubjectNamesAsync(subject)
                : new List<string> { subject };
            query = query.Where(d => descendantSubjects.Contains(d.Subject));
        }

        if (extensions is { Count: > 0 })
        {
            var lowered = extensions.Select(e => e.ToLower()).ToList();
            query = query.Where(d => lowered.Contains(d.FileExtension.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            query = query.Where(d => d.Title.ToLower().Contains(keyword) || d.User.Username.ToLower().Contains(keyword) || d.FileExtension.ToLower().Contains(keyword));
        }

        var totalCount = await query.CountAsync();
        bool ascending = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var ordered = (sortBy?.ToLowerInvariant()) switch
        {
            "title" => ascending ? query.OrderBy(d => d.Title).ThenBy(d => d.DocumentId) : query.OrderByDescending(d => d.Title).ThenByDescending(d => d.DocumentId),
            "bookmarks" => ascending ? query.OrderBy(d => d.BookmarkCount).ThenBy(d => d.DocumentId) : query.OrderByDescending(d => d.BookmarkCount).ThenByDescending(d => d.DocumentId),
            "downloads" => ascending ? query.OrderBy(d => d.DownloadCount).ThenBy(d => d.DocumentId) : query.OrderByDescending(d => d.DownloadCount).ThenByDescending(d => d.DocumentId),
            "views" => ascending ? query.OrderBy(d => d.ViewCount).ThenBy(d => d.DocumentId) : query.OrderByDescending(d => d.ViewCount).ThenByDescending(d => d.DocumentId),
            _ => ascending ? query.OrderBy(d => d.CreatedAt).ThenBy(d => d.DocumentId) : query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DocumentId)
        };

        var docs = await ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<DocumentResponseDto>(docs.Select(d => MapToDto(d, d.User.Username)).ToList(), totalCount, pageNumber, pageSize);
    }

    public async Task<DocumentResponseDto?> GetDocumentByIdAsync(int documentId)
    {
        var doc = await _dbContext.Documents.Include(d => d.User).FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null)
            return null;
        var dto = MapToDto(doc, doc.User.Username);
        var currentExtraction = await _dbContext.DocumentExtractedTexts.AsNoTracking()
            .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.DocumentVersionId == doc.CurrentVersionId);
        if (currentExtraction != null)
        {
            dto.ExtractionCoverage = currentExtraction.ExtractionCoverage;
            dto.ImageContentDetected = currentExtraction.ImageContentDetected;
            dto.UnreadImageContentWarning = currentExtraction.UnreadImageContentWarning;
            dto.OcrRegionCount = currentExtraction.OcrRegionCount;
        }
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

    public async Task<string?> GetExtractedTextAsync(int documentId, int? versionId = null)
    {
        if (versionId.HasValue && versionId.Value > 0)
        {
            var textByVersion = await _dbContext.DocumentExtractedTexts
                .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.DocumentVersionId == versionId.Value);
            return textByVersion?.ExtractedText;
        }

        var doc = await _dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc?.CurrentVersionId != null)
        {
            var currentVersionText = await _dbContext.DocumentExtractedTexts
                .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.DocumentVersionId == doc.CurrentVersionId);
            if (currentVersionText != null) return currentVersionText.ExtractedText;
        }

        // Fallback for legacy text when no version was specified
        var fallback = await _dbContext.DocumentExtractedTexts
            .OrderByDescending(t => t.ExtractionId)
            .FirstOrDefaultAsync(t => t.DocumentId == documentId);
        return fallback?.ExtractedText;
    }

    public async Task<bool> CheckDuplicateTitleAsync(int userId, string title, string fileExtension, int? folderId, int? excludeDocId)
    {
        var normalizedTitle = StripExtension(title).Trim();
        var normalizedExtension = fileExtension.Trim().TrimStart('.').ToLowerInvariant();
        var query = _dbContext.Documents.Where(d => d.UserId == userId
            && !d.IsDeleted
            && !d.CloudStorageUrl.Contains("/temp_")
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
            RestrictedAt = r.RestrictedAt,
            ReportedVersionId = r.ReportedVersionId
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
            ReportedVersionId = doc.CurrentVersionId,
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
        var moderators = await _dbContext.Users
            .Where(u => u.Role == "MODERATOR" && u.Status == "ACTIVE" && u.UserId != reporterId)
            .Select(u => u.UserId)
            .ToListAsync();
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
        var document = await _dbContext.Documents.Include(d => d.User).Include(d => d.DocumentExtractedTexts).FirstOrDefaultAsync(d => d.DocumentId == documentId);
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

        var currentText = document.DocumentExtractedTexts
            .FirstOrDefault(t => t.DocumentVersionId == document.CurrentVersionId)
            ?? document.DocumentExtractedTexts.OrderByDescending(t => t.ExtractionId).FirstOrDefault();
        var extracted = currentText?.ExtractedText?.Trim();

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

    private async Task SyncRenamedVersionPathsAsync(int documentId, string oldUrl, string newUrl)
    {
        if (string.Equals(oldUrl, newUrl, StringComparison.OrdinalIgnoreCase))
            return;

        var versions = await _dbContext.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.CloudStorageUrl == oldUrl)
            .ToListAsync();

        foreach (var version in versions)
            version.CloudStorageUrl = newUrl;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TEXT EXTRACTION LOGIC
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record ExtractionResult(
        string Text,
        double? CoveragePercent,
        IReadOnlyDictionary<int, double>? PageOcrConfidence = null,
        bool ImageContentDetected = false,
        bool UnreadImageContentWarning = false);

    private async Task<ExtractionResult> ExtractTextFromFileAsync(string filePath, string fileExtension)
    {
        string ext = fileExtension.ToLower().TrimStart('.');
        switch (ext)
        {
            case "txt":
            case "md":
                return new ExtractionResult(await File.ReadAllTextAsync(filePath, Encoding.UTF8), 1.0);

            case "docx":
                return CreateOfficeExtractionResult(ExtractTextFromDocx(filePath), filePath, "word/media/");

            case "xlsx":
                return CreateOfficeExtractionResult(ExtractTextFromXlsx(filePath), filePath, "xl/media/");

            case "pdf":
                return ExtractTextFromPdf(filePath);

            case "pptx":
                var pptxResult = ExtractTextFromPptx(filePath);
                bool pptxHasImages = HasEmbeddedMedia(filePath, "ppt/media/");
                return pptxResult with
                {
                    ImageContentDetected = pptxHasImages,
                    UnreadImageContentWarning = pptxHasImages
                };

            case "png":
            case "jpg":
            case "jpeg":
            case "webp":
            case "bmp":
            case "gif":
                return await ExtractTextFromImageAsync(filePath, ext);

            default:
                Console.WriteLine($"Unsupported file type for text extraction: {fileExtension}");
                return new ExtractionResult("", null);
        }
    }

    private async Task<ExtractionResult> ExtractTextFromImageAsync(string filePath, string ext)
    {
        try
        {
            byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
            string mimeType = ext switch
            {
                "png" => "image/png",
                "webp" => "image/webp",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                _ => "image/jpeg"
            };

            string extractedText = await _geminiService.ExtractTextFromImageAsync(imageBytes, mimeType);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                extractedText = $"[Hình ảnh không có văn bản nhận diện được: {Path.GetFileName(filePath)}]";
            }
            return new ExtractionResult(extractedText, 1.0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageOCR] Error extracting text from image {filePath}: {ex.Message}");
            return new ExtractionResult($"[Lỗi trích xuất OCR từ ảnh: {ex.Message}]", 0.0);
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
                    const int maxRows = 10_000;
                    const int maxColumns = 250;
                    var (detectedRows, detectedColumns) = GetWorksheetBounds(sheet);
                    int rows = Math.Min(detectedRows, maxRows);
                    int cols = Math.Min(detectedColumns, maxColumns);
                    for (int r = 1; r <= rows; r++)
                    {
                        var rowVals = new List<string>();
                        for (int c = 1; c <= cols; c++)
                        {
                            // Text returns the formatted/cached display value and is more useful
                            // than Value.ToString() for dates, percentages and formula cells.
                            var val = sheet.Cells[r, c].Text;
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
            // Do not turn parser failures into a successful placeholder extraction.
            // ProcessExtractionAsync will mark the document FAILED and the worker can retry it.
            throw new InvalidDataException($"Unable to extract XLSX content from '{Path.GetFileName(filePath)}'.", ex);
        }
    }

    private static (int Rows, int Columns) GetWorksheetBounds(ExcelWorksheet sheet)
    {
        if (sheet.Dimension != null)
            return (sheet.Dimension.Rows, sheet.Dimension.Columns);

        // Some valid XLSX producers omit the optional worksheet <dimension>
        // element. EPPlus still loads their cells, but Dimension remains null.
        int maxRow = 0;
        int maxColumn = 0;
        foreach (var cell in sheet.Cells)
        {
            if (cell.Value == null && string.IsNullOrWhiteSpace(cell.Text))
                continue;

            maxRow = Math.Max(maxRow, cell.Start.Row);
            maxColumn = Math.Max(maxColumn, cell.Start.Column);
        }

        return (maxRow, maxColumn);
    }

    private static ExtractionResult CreateOfficeExtractionResult(string text, string filePath, string mediaPrefix)
    {
        bool hasEmbeddedImages = HasEmbeddedMedia(filePath, mediaPrefix);
        return new ExtractionResult(text, null, null, hasEmbeddedImages, hasEmbeddedImages);
    }

    private static bool HasEmbeddedMedia(string filePath, string mediaPrefix)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            return archive.Entries.Any(entry =>
                entry.FullName.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(entry.Name));
        }
        catch
        {
            return false;
        }
    }

    private const string OcrLanguages = "vie+eng";
    private static readonly string TessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

    private ExtractionResult ExtractTextFromPdf(string filePath)
    {
        try
        {
            byte[] pdfBytes = File.ReadAllBytes(filePath);
            bool hasPdfImages = Regex.IsMatch(Encoding.ASCII.GetString(pdfBytes), @"/Subtype\s*/Image\b");
            // Parse the same in-memory snapshot used by OCR. The upload flow can
            // rename the temporary file while the background job is running;
            // reopening by path after ReadAllBytes creates a race that used to
            // turn a valid PDF into an empty successful extraction.
            using (var document = PdfDocument.Open(pdfBytes))
            {
                var sb = new StringBuilder();
                int totalPages = 0;
                int coveredPages = 0;
                var pageOcrConfidence = new Dictionary<int, double>();
                bool hasNativeText = false;
                Tesseract.TesseractEngine? ocrEngine = null;
                try
                {
                    foreach (var page in document.GetPages())
                    {
                        totalPages++;
                        sb.AppendLine($"[PAGE {page.Number}]");
                        var words = page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom)
                            .ThenBy(w => w.BoundingBox.Left).ToList();
                        if (words.Count == 0)
                        {
                            var (ocrText, confidence) = TryOcrPdfPage(pdfBytes, page.Number - 1, page.Width, page.Height, ref ocrEngine);
                            if (!string.IsNullOrWhiteSpace(ocrText))
                            {
                                coveredPages++;
                                sb.AppendLine(ocrText.Trim());
                                sb.AppendLine();
                                pageOcrConfidence[page.Number] = confidence;
                            }
                            continue;
                        }
                        hasNativeText = true;
                        coveredPages++;
                        var lines = words.GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4.0) * 4)
                            .OrderByDescending(group => group.Key)
                            .Select(group => string.Join(" ", group.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
                        foreach (var line in lines)
                            sb.AppendLine(line);
                        sb.AppendLine();
                    }
                }
                finally
                {
                    ocrEngine?.Dispose();
                }
                double? coverage = totalPages > 0 ? (double)coveredPages / totalPages : null;
                return new ExtractionResult(
                    sb.ToString(),
                    coverage,
                    pageOcrConfidence.Count > 0 ? pageOcrConfidence : null,
                    hasPdfImages,
                    hasPdfImages && hasNativeText);
            }
        }
        catch (Exception ex)
        {
            // Let the durable processing queue retry transient storage/parser
            // failures instead of persisting a placeholder and marking READY.
            throw new InvalidDataException($"Unable to extract PDF content from '{Path.GetFileName(filePath)}'.", ex);
        }
    }

    private (string Text, double Confidence) TryOcrPdfPage(byte[] pdfBytes, int pageIndex, double pageWidthPoints, double pageHeightPoints, ref Tesseract.TesseractEngine? engine)
    {
        try
        {
            if (!Directory.Exists(TessDataPath))
                return ("", 0);

            int targetWidth = Math.Max(1, (int)(pageWidthPoints / 72.0 * 200));
            int targetHeight = Math.Max(1, (int)(pageHeightPoints / 72.0 * 200));

            using var docReader = Docnet.Core.DocLib.Instance.GetDocReader(pdfBytes, new Docnet.Core.Models.PageDimensions(targetWidth, targetHeight));
            using var pageReader = docReader.GetPageReader(pageIndex);
            var raw = pageReader.GetImage();
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();
            var bmp = EncodeBgr24Bmp(raw, width, height);

            engine ??= new Tesseract.TesseractEngine(TessDataPath, OcrLanguages, Tesseract.EngineMode.Default);

            using var pix = Tesseract.Pix.LoadFromMemory(bmp);
            using var ocrPage = engine.Process(pix);
            string text = ocrPage.GetText();
            float confidence = ocrPage.GetMeanConfidence();
            return (text, confidence);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error OCR-ing PDF page: {ex.Message}");
            return ("", 0);
        }
    }

    private static byte[] EncodeBgr24Bmp(byte[] bgraPixels, int width, int height)
    {
        int rowSizeUnpadded = width * 3;
        int rowSize = (rowSizeUnpadded + 3) / 4 * 4;
        int pixelDataSize = rowSize * height;
        int fileSize = 54 + pixelDataSize;
        var buffer = new byte[fileSize];

        buffer[0] = (byte)'B';
        buffer[1] = (byte)'M';
        WriteLeInt32(buffer, 2, fileSize);
        WriteLeInt32(buffer, 10, 54);
        WriteLeInt32(buffer, 14, 40);
        WriteLeInt32(buffer, 18, width);
        WriteLeInt32(buffer, 22, height);
        WriteLeInt16(buffer, 26, 1);
        WriteLeInt16(buffer, 28, 24);
        WriteLeInt32(buffer, 34, pixelDataSize);

        for (int y = 0; y < height; y++)
        {
            int srcRow = height - 1 - y;
            int destOffset = 54 + y * rowSize;
            int srcOffset = srcRow * width * 4;
            for (int x = 0; x < width; x++)
            {
                buffer[destOffset + x * 3 + 0] = bgraPixels[srcOffset + x * 4 + 0];
                buffer[destOffset + x * 3 + 1] = bgraPixels[srcOffset + x * 4 + 1];
                buffer[destOffset + x * 3 + 2] = bgraPixels[srcOffset + x * 4 + 2];
            }
        }
        return buffer;
    }

    private static void WriteLeInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteLeInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private ExtractionResult ExtractTextFromPptx(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var slides = archive.Entries
                .Where(entry => Regex.IsMatch(entry.FullName, @"^ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
                .OrderBy(entry => int.Parse(Regex.Match(entry.Name, @"\d+").Value))
                .ToList();
            var sb = new StringBuilder();
            int coveredSlides = 0;
            foreach (var slide in slides)
            {
                using var stream = slide.Open();
                var xml = XDocument.Load(stream);
                var text = string.Join(" ", xml.Descendants(drawing + "t").Select(node => node.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    coveredSlides++;
                    var slideNumber = int.Parse(Regex.Match(slide.Name, @"\d+").Value);
                    sb.AppendLine($"[PAGE {slideNumber}]").AppendLine(text).AppendLine();
                }
            }
            double? coverage = slides.Count > 0 ? (double)coveredSlides / slides.Count : null;
            return new ExtractionResult(sb.ToString().Trim(), coverage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting PPTX: {ex.Message}");
            return new ExtractionResult("", null);
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
            GeneralAccess = doc.GeneralAccess ?? "RESTRICTED",
            IsShareLinkRevoked = doc.IsShareLinkRevoked,
            ShareLinkExpiresAt = doc.ShareLinkExpiresAt,
            TotalReportScore = doc.TotalReportScore,
            IsFlagged = doc.IsFlagged,
            BookmarkCount = doc.BookmarkCount,
            DownloadCount = doc.DownloadCount,
            ViewCount = doc.ViewCount,
            ExtractionCoveragePercent = doc.ExtractionCoveragePercent,
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

    public async Task ProcessExtractionAsync(int documentId, int? versionId = null)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) return;

        string relativePath = doc.CloudStorageUrl.TrimStart('/');
        string fileExtension = doc.FileExtension;

        DocumentVersion? processingVersion = null;
        if (versionId.HasValue)
        {
            processingVersion = await _dbContext.DocumentVersions.FirstOrDefaultAsync(v => v.VersionId == versionId.Value && v.DocumentId == documentId);
            if (processingVersion != null && !string.IsNullOrWhiteSpace(processingVersion.CloudStorageUrl))
            {
                relativePath = processingVersion.CloudStorageUrl.TrimStart('/');
                fileExtension = processingVersion.FileExtension;
            }
        }

        var extractionStartTime = _clock.UtcNow.AddSeconds(-2);
        try
        {
            if (processingVersion != null) processingVersion.AiParsingStatus = "PROCESSING";
            if (!versionId.HasValue || doc.CurrentVersionId == versionId) doc.AiParsingStatus = "PROCESSING";
            await _dbContext.SaveChangesAsync();

            string physicalPath = _fileStorage.GetPhysicalPath(relativePath);
            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException($"File not found on storage: {physicalPath}");
            }

            var extraction = await ExtractTextFromFileAsync(physicalPath, fileExtension);
            string extractedText = extraction.Text;

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                extractedText = $"[Tài liệu không chứa lớp văn bản trích xuất được hoặc tệp ảnh rỗng: {doc.Title}]";
            }

            if (processingVersion != null) processingVersion.AiParsingStatus = "CHUNKING";
            if (!versionId.HasValue || doc.CurrentVersionId == versionId) doc.AiParsingStatus = "CHUNKING";
            await _dbContext.SaveChangesAsync();

            int? targetVersionId = versionId ?? doc.CurrentVersionId;
            if (!targetVersionId.HasValue || targetVersionId.Value == 0)
            {
                targetVersionId = await _dbContext.DocumentVersions
                    .Where(v => v.DocumentId == documentId)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (int?)v.VersionId)
                    .FirstOrDefaultAsync();
            }

            // If still null, create baseline Version 1 automatically
            if (!targetVersionId.HasValue || targetVersionId.Value == 0)
            {
                var existingV1 = await _dbContext.DocumentVersions
                    .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.VersionNumber == 1);
                if (existingV1 != null)
                {
                    targetVersionId = existingV1.VersionId;
                }
                else
                {
                    var baselineVersion = new DocumentVersion
                    {
                        DocumentId = documentId,
                        VersionNumber = 1,
                        CloudStorageUrl = doc.CloudStorageUrl,
                        FileExtension = doc.FileExtension,
                        FileSizeMb = doc.FileSizeMb,
                        ChangeSummary = "Phiên bản khởi tạo tự động (Baseline v1)",
                        CreatedByUserId = doc.UserId,
                        CreatedAt = _clock.UtcNow
                    };
                    _dbContext.DocumentVersions.Add(baselineVersion);
                    try
                    {
                        await _dbContext.SaveChangesAsync();
                        targetVersionId = baselineVersion.VersionId;
                    }
                    catch (DbUpdateException)
                    {
                        foreach (var entry in _dbContext.ChangeTracker.Entries<DocumentVersion>().ToList())
                        {
                            entry.State = EntityState.Detached;
                        }
                        var concurrentV1 = await _dbContext.DocumentVersions
                            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.VersionNumber == 1);
                        if (concurrentV1 != null)
                        {
                            targetVersionId = concurrentV1.VersionId;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                if (doc.CurrentVersionId != targetVersionId)
                {
                    doc.CurrentVersionId = targetVersionId;
                    await _dbContext.SaveChangesAsync();
                }
            }

            processingVersion ??= targetVersionId.HasValue
                ? await _dbContext.DocumentVersions.FirstOrDefaultAsync(v => v.VersionId == targetVersionId.Value && v.DocumentId == documentId)
                : null;

            // 1. Upsert DocumentExtractedText for (documentId, targetVersionId)
            var existingText = await _dbContext.DocumentExtractedTexts
                .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.DocumentVersionId == targetVersionId);
            if (existingText == null)
            {
                _dbContext.DocumentExtractedTexts.Add(new DocumentExtractedText
                {
                    DocumentId = documentId,
                    DocumentVersionId = targetVersionId,
                    ExtractedText = extractedText,
                    ExtractionCoverage = (decimal)Math.Clamp(extraction.CoveragePercent ?? 1.0, 0, 1),
                    ImageContentDetected = extraction.ImageContentDetected,
                    UnreadImageContentWarning = extraction.UnreadImageContentWarning,
                    OcrRegionCount = extraction.PageOcrConfidence?.Count ?? 0,
                    CreatedAt = _clock.UtcNow
                });
            }
            else
            {
                existingText.ExtractedText = extractedText;
                existingText.ExtractionCoverage = (decimal)Math.Clamp(extraction.CoveragePercent ?? 1.0, 0, 1);
                existingText.ImageContentDetected = extraction.ImageContentDetected;
                existingText.UnreadImageContentWarning = extraction.UnreadImageContentWarning;
                existingText.OcrRegionCount = extraction.PageOcrConfidence?.Count ?? 0;
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // In case of concurrent extraction insertion race condition:
                // 1. Detach all faulted DocumentExtractedText entities in tracker
                foreach (var entry in _dbContext.ChangeTracker.Entries<DocumentExtractedText>().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                // 2. Reload the concurrently created record and update
                var concurrentText = await _dbContext.DocumentExtractedTexts
                    .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.DocumentVersionId == targetVersionId);
                if (concurrentText != null)
                {
                    concurrentText.ExtractedText = extractedText;
                    concurrentText.ExtractionCoverage = (decimal)Math.Clamp(extraction.CoveragePercent ?? 1.0, 0, 1);
                    concurrentText.ImageContentDetected = extraction.ImageContentDetected;
                    concurrentText.UnreadImageContentWarning = extraction.UnreadImageContentWarning;
                    concurrentText.OcrRegionCount = extraction.PageOcrConfidence?.Count ?? 0;
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    throw;
                }
            }

            bool isBaselineVersion = false;
            if (targetVersionId.HasValue)
            {
                var targetVer = await _dbContext.DocumentVersions
                    .FirstOrDefaultAsync(v => v.VersionId == targetVersionId.Value);
                if (targetVer != null && targetVer.VersionNumber == 1)
                {
                    isBaselineVersion = true;
                }
            }

            // 2. Replace Chunks Idempotently for this version
            var oldChunks = await _dbContext.DocumentChunks
                .Where(c => c.DocumentId == documentId && (c.DocumentVersionId == targetVersionId || (isBaselineVersion && c.DocumentVersionId == null)))
                .ToListAsync();
            if (oldChunks.Any())
            {
                _dbContext.DocumentChunks.RemoveRange(oldChunks);
            }

            var newChunks = DocumentChunker.Chunk(documentId, extractedText, _clock.UtcNow, extraction.PageOcrConfidence, targetVersionId);
            _dbContext.DocumentChunks.AddRange(newChunks);

            if (processingVersion != null) processingVersion.AiParsingStatus = "READY";
            if (doc.CurrentVersionId == targetVersionId)
            {
                doc.ExtractionCoveragePercent = extraction.CoveragePercent ?? 1.0;
                doc.AiParsingStatus = "READY";
                doc.UpdatedAt = _clock.UtcNow;
            }

            // 3. Add ModerationNotice
            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = doc.UserId,
                DocumentId = doc.DocumentId,
                Type = "DOCUMENT_AI_READY",
                Title = "Tài liệu đã sẵn sàng cho AI",
                Message = $"Tài liệu “{doc.Title}” đã tải lên và trích xuất nội dung thành công ({newChunks.Count} đoạn). Bạn có thể bắt đầu chat với AI.",
                ActionUrl = $"/chat?documentId={doc.DocumentId}",
                IsRead = false,
                CreatedAt = _clock.Now
            });

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // 1. Check if error is a unique constraint violation
                if (!IsUniqueConstraintViolation(ex))
                {
                    throw;
                }

                // 2. In case of concurrent extraction worker race condition on chunks:
                // Detach all faulted DocumentChunks and ModerationNotices in tracker
                foreach (var entry in _dbContext.ChangeTracker.Entries<DocumentChunk>().ToList())
                {
                    entry.State = EntityState.Detached;
                }
                foreach (var entry in _dbContext.ChangeTracker.Entries<ModerationNotice>().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                // 3. Verify that winner chunks actually exist for (documentId, targetVersionId) created during this run
                var winnerChunksExist = await _dbContext.DocumentChunks
                    .AnyAsync(c => c.DocumentId == documentId && c.DocumentVersionId == targetVersionId && c.CreatedAt >= extractionStartTime);
                if (winnerChunksExist)
                {
                    // Winner succeeded; ensure doc status is set to READY
                    var reloadedDoc = await _dbContext.Documents.FindAsync(documentId);
                    if (reloadedDoc != null && reloadedDoc.AiParsingStatus != "READY")
                    {
                        reloadedDoc.AiParsingStatus = "READY";
                        reloadedDoc.ExtractionCoveragePercent = extraction.CoveragePercent ?? 1.0;
                        reloadedDoc.UpdatedAt = _clock.UtcNow;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                else
                {
                    // Not a verified concurrent chunk race, rethrow
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TextExtraction] Failed to extract text for doc {documentId}: {ex.Message}");
            if (processingVersion != null) processingVersion.AiParsingStatus = "FAILED";
            if (!versionId.HasValue || doc.CurrentVersionId == versionId) doc.AiParsingStatus = "FAILED";
            await _dbContext.SaveChangesAsync();
            throw; // Rethrow so queue marks job FAILED/DEAD
        }
    }

    public async Task RetryExtractionAsync(int documentId, int userId)
    {
        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Tài liệu không tồn tại");
        if (doc.UserId != userId) throw new UnauthorizedAccessException("Chỉ chủ sở hữu mới có quyền yêu cầu trích xuất lại");

        doc.AiParsingStatus = "QUEUED";
        if (doc.CurrentVersionId.HasValue)
        {
            var currentVersion = await _dbContext.DocumentVersions
                .FirstOrDefaultAsync(v => v.VersionId == doc.CurrentVersionId.Value && v.DocumentId == documentId);
            if (currentVersion != null) currentVersion.AiParsingStatus = "QUEUED";
        }
        await _dbContext.SaveChangesAsync();

        await _queue.EnqueueJobAsync(documentId, doc.CurrentVersionId);
    }

    public async Task<StorageQuotaDto> GetUserStorageQuotaAsync(int userId)
    {
        var user = await _dbContext.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) throw new KeyNotFoundException("User not found");

        decimal usedMb = await _dbContext.Documents
            .Where(d => d.UserId == userId && !d.IsDeleted)
            .SumAsync(d => (decimal?)d.FileSizeMb) ?? 0m;

        decimal maxStorageMb = user.Tier?.MaxStorageMb ?? 50m;
        string tierName = user.Tier?.TierName ?? "Free";

        var aiPromptsToday = user.AiPromptsToday ?? 0;
        if (user.LastPromptReset.HasValue && (DateTime.Now - user.LastPromptReset.Value).TotalHours >= 24)
            aiPromptsToday = 0;

        return new StorageQuotaDto
        {
            UserId = userId,
            TierName = tierName,
            UsedStorageMb = Math.Round((decimal)usedMb, 2),
            MaxStorageMb = maxStorageMb,
            AiPromptsToday = aiPromptsToday,
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
            var descendantSubjects = _subjectService != null
                ? await _subjectService.GetDescendantSubjectNamesAsync(subject)
                : new List<string> { subject };
            query = query.Where(d => descendantSubjects.Contains(d.Subject));
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner != null)
        {
            var msg = inner.Message;
            if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Violation of UNIQUE KEY", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Violation of PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("2601") || msg.Contains("2627") || msg.Contains("2067"))
            {
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }
}
