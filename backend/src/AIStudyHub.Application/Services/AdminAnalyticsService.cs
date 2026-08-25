using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Application.Services;

public class AdminAnalyticsService : IAdminAnalyticsService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<AdminAnalyticsService> _logger;

    public AdminAnalyticsService(
        IStudyHubDbContext dbContext,
        IClock clock,
        ILogger<AdminAnalyticsService> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AdminOverviewAnalyticsDto> GetOverviewAnalyticsAsync(DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken = default)
    {
        var usersQuery = _dbContext.Users.AsNoTracking();
        var txsQuery = _dbContext.Transactions.AsNoTracking();
        var docsQuery = _dbContext.Documents.AsNoTracking();
        var reportsQuery = _dbContext.DocumentReports.AsNoTracking();
        var activitiesQuery = _dbContext.DocumentActivities.AsNoTracking();

        if (startDate.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.CreatedAt >= startDate.Value);
            txsQuery = txsQuery.Where(t => t.StartedAt >= startDate.Value);
            docsQuery = docsQuery.Where(d => d.CreatedAt >= startDate.Value);
            reportsQuery = reportsQuery.Where(r => r.CreatedAt >= startDate.Value);
            activitiesQuery = activitiesQuery.Where(a => a.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var exclusiveEnd = endDate.Value.Date.AddDays(1);
            usersQuery = usersQuery.Where(u => u.CreatedAt < exclusiveEnd);
            txsQuery = txsQuery.Where(t => t.StartedAt < exclusiveEnd);
            docsQuery = docsQuery.Where(d => d.CreatedAt < exclusiveEnd);
            reportsQuery = reportsQuery.Where(r => r.CreatedAt < exclusiveEnd);
            activitiesQuery = activitiesQuery.Where(a => a.CreatedAt < exclusiveEnd);
        }

        int totalUsers = await _dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        int newUsersInPeriod = await usersQuery.CountAsync(cancellationToken);
        int activeUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Status == "ACTIVE", cancellationToken);
        int suspendedUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Status == "SUSPENDED", cancellationToken);
        int premiumUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.TierId == 3, cancellationToken);
        int studentUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "STUDENT", cancellationToken);
        int freeUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "STUDENT" && u.TierId == 2, cancellationToken);

        int totalTransactions = await txsQuery.CountAsync(cancellationToken);
        int pendingTransactions = await txsQuery.CountAsync(t => t.Status == "PENDING", cancellationToken);

        decimal successfulDeposits = await txsQuery
            .Where(t => t.Status == "SUCCESS" && t.Type == "DEPOSIT")
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        decimal successfulWithdrawals = await txsQuery
            .Where(t => t.Status == "SUCCESS" && t.Type == "WITHDRAW")
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        int totalDocuments = await docsQuery.CountAsync(cancellationToken);
        int publicDocuments = await docsQuery.CountAsync(d => d.SharingPermission == "PUBLIC", cancellationToken);
        int privateDocuments = await docsQuery.CountAsync(d => d.SharingPermission == "PRIVATE", cancellationToken);
        int flaggedDocuments = await docsQuery.CountAsync(d => d.IsFlagged == true, cancellationToken);

        int totalActivities = await activitiesQuery.CountAsync(cancellationToken);

        int totalUniqueDownloads = await activitiesQuery
            .Where(a => a.ActivityType == "DOWNLOAD" && a.UserId != a.Document.UserId)
            .Select(a => new { a.DocumentId, a.UserId })
            .Distinct()
            .CountAsync(cancellationToken);

        int totalUniqueBookmarks = await _dbContext.Bookmarks.AsNoTracking()
            .Where(b => b.UserId != b.Document.UserId)
            .Select(b => new { b.DocumentId, b.UserId })
            .Distinct()
            .CountAsync(cancellationToken);

        int totalReports = await reportsQuery.CountAsync(cancellationToken);
        int pendingReports = await reportsQuery.CountAsync(r => r.Status == "PENDING", cancellationToken);

        var recentTransactions = await _dbContext.Transactions.AsNoTracking()
            .Include(t => t.User)
            .OrderByDescending(t => t.StartedAt)
            .Take(5)
            .Select(t => new RecentTransactionSummaryDto
            {
                TransactionId = t.TransactionId,
                Username = t.User != null ? t.User.Username : string.Empty,
                Amount = t.Amount,
                Type = t.Type,
                Status = t.Status,
                StartedAt = t.StartedAt
            })
            .ToListAsync(cancellationToken);

        var recentReports = await _dbContext.DocumentReports.AsNoTracking()
            .Include(r => r.Reporter)
            .Include(r => r.Document)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .Select(r => new RecentReportSummaryDto
            {
                ReportId = r.ReportId,
                Title = r.Document != null ? r.Document.Title : string.Empty,
                ReporterName = r.Reporter != null ? r.Reporter.Username : string.Empty,
                ReasonCode = r.ReasonCode,
                Status = r.Status ?? "PENDING",
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new AdminOverviewAnalyticsDto
        {
            TotalUsers = totalUsers,
            NewUsersInPeriod = newUsersInPeriod,
            ActiveUsers = activeUsers,
            SuspendedUsers = suspendedUsers,
            PremiumUsers = premiumUsers,
            StudentUsers = studentUsers,
            FreeUsers = freeUsers,
            TotalTransactions = totalTransactions,
            PendingTransactions = pendingTransactions,
            SuccessfulDeposits = successfulDeposits,
            SuccessfulWithdrawals = successfulWithdrawals,
            TotalDocuments = totalDocuments,
            PublicDocuments = publicDocuments,
            PrivateDocuments = privateDocuments,
            FlaggedDocuments = flaggedDocuments,
            TotalActivities = totalActivities,
            TotalUniqueDownloads = totalUniqueDownloads,
            TotalUniqueBookmarks = totalUniqueBookmarks,
            TotalReports = totalReports,
            PendingReports = pendingReports,
            RecentTransactions = recentTransactions,
            RecentReports = recentReports
        };
    }

    public async Task<UserContributionScoreDto> CalculateUserContributionScoreAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException($"User {userId} not found.");
        }

        int approvedPublicDocs = await _dbContext.Documents.AsNoTracking()
            .CountAsync(d => d.UserId == userId && d.SharingPermission == "PUBLIC" && d.IsDeleted != true && d.IsFlagged != true, cancellationToken);

        int uniqueDownloads = await _dbContext.DocumentActivities.AsNoTracking()
            .Where(a => a.ActivityType == "DOWNLOAD" && a.Document.UserId == userId && a.UserId != userId)
            .Select(a => new { a.DocumentId, a.UserId })
            .Distinct()
            .CountAsync(cancellationToken);

        int uniqueBookmarks = await _dbContext.Bookmarks.AsNoTracking()
            .Where(b => b.Document.UserId == userId && b.UserId != userId)
            .Select(b => new { b.DocumentId, b.UserId })
            .Distinct()
            .CountAsync(cancellationToken);

        int moderationApproved = await _dbContext.Documents.AsNoTracking()
            .CountAsync(d => d.UserId == userId && (d.ModerationStatus == "APPROVED" || (d.SharingPermission == "PUBLIC" && d.IsFlagged != true)), cancellationToken);

        int upheldViolations = await _dbContext.DocumentReports.AsNoTracking()
            .CountAsync(r => r.Document.UserId == userId && (r.Status == "ACTION_TAKEN" || r.Status == "UPHELD"), cancellationToken);

        int removedDocs = await _dbContext.Documents.AsNoTracking()
            .CountAsync(d => d.UserId == userId && d.IsDeleted == true && d.DeletedByUserId != null && d.DeletedByUserId != userId, cancellationToken);

        // Formula: ContributionScore = ApprovedPublicDocuments * 2 + UniqueDownloads * 0.1 + UniqueBookmarks * 0.5 + ModerationApprovedDocuments * 3 - UpheldViolations * 10 - RemovedDocuments * 15
        double score = (approvedPublicDocs * 2.0)
                     + (uniqueDownloads * 0.1)
                     + (uniqueBookmarks * 0.5)
                     + (moderationApproved * 3.0)
                     - (upheldViolations * 10.0)
                     - (removedDocs * 15.0);

        return new UserContributionScoreDto
        {
            UserId = userId,
            Username = user.Username,
            ContributionScore = Math.Round(score, 2),
            ApprovedPublicDocuments = approvedPublicDocs,
            UniqueDownloads = uniqueDownloads,
            UniqueBookmarks = uniqueBookmarks,
            ModerationApprovedDocuments = moderationApproved,
            UpheldViolations = upheldViolations,
            RemovedDocuments = removedDocs,
            CalculatedAt = _clock.Now
        };
    }

    public async Task<List<TopContributorDto>> GetTopContributorsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Role == "STUDENT" && u.Status == "ACTIVE")
            .Take(100)
            .ToListAsync(cancellationToken);

        var contributorList = new List<TopContributorDto>();

        foreach (var u in users)
        {
            var stats = await CalculateUserContributionScoreAsync(u.UserId, cancellationToken);
            contributorList.Add(new TopContributorDto
            {
                UserId = u.UserId,
                Username = u.Username,
                Email = u.Email,
                ContributionScore = stats.ContributionScore,
                ApprovedPublicDocuments = stats.ApprovedPublicDocuments,
                UniqueDownloads = stats.UniqueDownloads,
                UniqueBookmarks = stats.UniqueBookmarks,
                ModerationApprovedDocuments = stats.ModerationApprovedDocuments
            });
        }

        return contributorList
            .OrderByDescending(c => c.ContributionScore)
            .Take(limit)
            .ToList();
    }

    public async Task<DocumentActivitySummaryDto> GetDocumentActivitySummaryAsync(int documentId, CancellationToken cancellationToken = default)
    {
        var doc = await _dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
        if (doc == null)
        {
            throw new KeyNotFoundException($"Document {documentId} not found.");
        }

        int totalActivities = await _dbContext.DocumentActivities.AsNoTracking()
            .CountAsync(a => a.DocumentId == documentId, cancellationToken);

        int uniqueDownloads = await _dbContext.DocumentActivities.AsNoTracking()
            .Where(a => a.DocumentId == documentId && a.ActivityType == "DOWNLOAD" && a.UserId != doc.UserId)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        int uniqueBookmarks = await _dbContext.Bookmarks.AsNoTracking()
            .Where(b => b.DocumentId == documentId && b.UserId != doc.UserId)
            .Select(b => b.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new DocumentActivitySummaryDto
        {
            DocumentId = documentId,
            Title = doc.Title,
            TotalActivities = totalActivities,
            UniqueDownloads = uniqueDownloads,
            UniqueBookmarks = uniqueBookmarks,
            ViewCount = doc.ViewCount ?? 0
        };
    }

    public async Task<CommunityAnalyticsSummaryDto> GetCommunityAnalyticsSummaryAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var reportedAccounts = await GetReportedAccountsAsync(1, 1, null, startDate, endDate, cancellationToken: cancellationToken);
        var downloadedDocuments = await GetDocumentEngagementRankingAsync("downloads", 1, 1, null, startDate, endDate, cancellationToken: cancellationToken);
        var bookmarkedDocuments = await GetDocumentEngagementRankingAsync("bookmarks", 1, 1, null, startDate, endDate, cancellationToken: cancellationToken);

        return new CommunityAnalyticsSummaryDto
        {
            MostReportedAccount = reportedAccounts.Items.FirstOrDefault(),
            MostDownloadedDocument = downloadedDocuments.Items.FirstOrDefault(),
            MostBookmarkedDocument = bookmarkedDocuments.Items.FirstOrDefault()
        };
    }

    public async Task<PagedResult<ReportedAccountAnalyticsDto>> GetReportedAccountsAsync(
        int pageNumber,
        int pageSize,
        string? search,
        DateTime? startDate,
        DateTime? endDate,
        string? sortBy = null,
        string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var reports = _dbContext.DocumentReports.AsNoTracking().AsQueryable();
        if (startDate.HasValue) reports = reports.Where(r => r.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
        {
            var exclusiveEnd = endDate.Value.Date.AddDays(1);
            reports = reports.Where(r => r.CreatedAt < exclusiveEnd);
        }

        var confirmedStatuses = new[] { "VIOLATION_CONFIRMED", "ACTION_TAKEN", "ACTIONED", "UPHELD" };
        var query = reports
            .Where(r => r.Document != null && r.Document.User != null)
            .GroupBy(r => new
            {
                r.Document.UserId,
                r.Document.User.Username,
                r.Document.User.Email,
                r.Document.User.Status
            })
            .Select(group => new ReportedAccountAnalyticsDto
            {
                UserId = group.Key.UserId,
                Username = group.Key.Username,
                Email = group.Key.Email,
                Status = group.Key.Status ?? string.Empty,
                ReportedDocumentCount = group.Select(r => r.DocumentId).Distinct().Count(),
                TotalReports = group.Count(),
                PendingReports = group.Count(r => r.Status == null || r.Status == "PENDING"),
                ConfirmedReports = group.Count(r => confirmedStatuses.Contains(r.Status!))
            });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(row => row.Username.ToLower().Contains(normalizedSearch) ||
                                       (row.Email != null && row.Email.ToLower().Contains(normalizedSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<ReportedAccountAnalyticsDto> orderedQuery = normalizedSort switch
        {
            "rank" => descending ? query.OrderBy(row => row.TotalReports) : query.OrderByDescending(row => row.TotalReports),
            "username" => descending ? query.OrderByDescending(row => row.Username) : query.OrderBy(row => row.Username),
            "reporteddocumentcount" => descending ? query.OrderByDescending(row => row.ReportedDocumentCount) : query.OrderBy(row => row.ReportedDocumentCount),
            "pendingreports" => descending ? query.OrderByDescending(row => row.PendingReports) : query.OrderBy(row => row.PendingReports),
            "confirmedreports" => descending ? query.OrderByDescending(row => row.ConfirmedReports) : query.OrderBy(row => row.ConfirmedReports),
            "status" => descending ? query.OrderByDescending(row => row.Status) : query.OrderBy(row => row.Status),
            _ => descending ? query.OrderByDescending(row => row.TotalReports) : query.OrderBy(row => row.TotalReports)
        };
        var items = await orderedQuery
            .ThenByDescending(row => row.ReportedDocumentCount)
            .ThenBy(row => row.UserId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<ReportedAccountAnalyticsDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PagedResult<DocumentEngagementAnalyticsDto>> GetDocumentEngagementRankingAsync(
        string metric,
        int pageNumber,
        int pageSize,
        string? search,
        DateTime? startDate,
        DateTime? endDate,
        string? sortBy = null,
        string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedMetric = metric.Trim().ToLowerInvariant();
        if (normalizedMetric is not ("downloads" or "bookmarks"))
            throw new ArgumentException("Metric must be 'downloads' or 'bookmarks'.", nameof(metric));

        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var documents = _dbContext.Documents.AsNoTracking().Where(d => d.IsDeleted != true);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            documents = documents.Where(d => d.Title.ToLower().Contains(normalizedSearch) ||
                                             d.User.Username.ToLower().Contains(normalizedSearch));
        }

        var rows = documents.Select(document => new DocumentEngagementAnalyticsDto
        {
            DocumentId = document.DocumentId,
            Title = document.Title,
            OwnerUserId = document.UserId,
            OwnerUsername = document.User.Username ?? string.Empty,
            FileExtension = document.FileExtension,
            SharingPermission = document.SharingPermission ?? string.Empty,
            UniqueDownloads = _dbContext.DocumentActivities
                .Where(activity => activity.DocumentId == document.DocumentId &&
                                   activity.ActivityType == "DOWNLOAD" &&
                                   activity.UserId != document.UserId &&
                                   (!startDate.HasValue || activity.CreatedAt >= startDate.Value) &&
                                   (!endDate.HasValue || activity.CreatedAt < endDate.Value.Date.AddDays(1)))
                .Select(activity => activity.UserId)
                .Distinct()
                .Count(),
            UniqueBookmarks = _dbContext.Bookmarks
                .Where(bookmark => bookmark.DocumentId == document.DocumentId &&
                                   bookmark.UserId != document.UserId &&
                                   (!startDate.HasValue || bookmark.CreatedAt >= startDate.Value) &&
                                   (!endDate.HasValue || bookmark.CreatedAt < endDate.Value.Date.AddDays(1)))
                .Select(bookmark => bookmark.UserId)
                .Distinct()
                .Count(),
            ViewCount = document.ViewCount ?? 0
        });

        rows = normalizedMetric == "downloads"
            ? rows.Where(row => row.UniqueDownloads > 0)
            : rows.Where(row => row.UniqueBookmarks > 0);
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<DocumentEngagementAnalyticsDto> orderedRows = normalizedSort switch
        {
            "rank" when normalizedMetric == "downloads" => descending ? rows.OrderBy(row => row.UniqueDownloads) : rows.OrderByDescending(row => row.UniqueDownloads),
            "rank" => descending ? rows.OrderBy(row => row.UniqueBookmarks) : rows.OrderByDescending(row => row.UniqueBookmarks),
            "title" => descending ? rows.OrderByDescending(row => row.Title) : rows.OrderBy(row => row.Title),
            "ownerusername" => descending ? rows.OrderByDescending(row => row.OwnerUsername) : rows.OrderBy(row => row.OwnerUsername),
            "uniquedownloads" => descending ? rows.OrderByDescending(row => row.UniqueDownloads) : rows.OrderBy(row => row.UniqueDownloads),
            "uniquebookmarks" => descending ? rows.OrderByDescending(row => row.UniqueBookmarks) : rows.OrderBy(row => row.UniqueBookmarks),
            "viewcount" => descending ? rows.OrderByDescending(row => row.ViewCount) : rows.OrderBy(row => row.ViewCount),
            "sharingpermission" => descending ? rows.OrderByDescending(row => row.SharingPermission) : rows.OrderBy(row => row.SharingPermission),
            _ when normalizedMetric == "downloads" => rows.OrderByDescending(row => row.UniqueDownloads),
            _ => rows.OrderByDescending(row => row.UniqueBookmarks)
        };

        var totalCount = await orderedRows.CountAsync(cancellationToken);
        var items = await orderedRows
            .ThenBy(row => row.DocumentId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<DocumentEngagementAnalyticsDto>(items, totalCount, pageNumber, pageSize);
    }
}
