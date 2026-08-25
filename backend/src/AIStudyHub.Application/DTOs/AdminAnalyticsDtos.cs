using System;
using System.Collections.Generic;

namespace AIStudyHub.Application.DTOs;

public class AdminOverviewAnalyticsDto
{
    public int TotalUsers { get; set; }
    public int NewUsersInPeriod { get; set; }
    public int ActiveUsers { get; set; }
    public int SuspendedUsers { get; set; }
    public int PremiumUsers { get; set; }
    public int StudentUsers { get; set; }
    public int FreeUsers { get; set; }

    public int TotalTransactions { get; set; }
    public int PendingTransactions { get; set; }
    public decimal SuccessfulDeposits { get; set; }
    public decimal SuccessfulWithdrawals { get; set; }

    public int TotalDocuments { get; set; }
    public int PublicDocuments { get; set; }
    public int PrivateDocuments { get; set; }
    public int FlaggedDocuments { get; set; }

    public int TotalActivities { get; set; }
    public int TotalUniqueDownloads { get; set; }
    public int TotalUniqueBookmarks { get; set; }

    public int TotalReports { get; set; }
    public int PendingReports { get; set; }

    public List<RecentTransactionSummaryDto> RecentTransactions { get; set; } = new();
    public List<RecentReportSummaryDto> RecentReports { get; set; } = new();
}

public class RecentTransactionSummaryDto
{
    public int TransactionId { get; set; }
    public string Username { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
}

public class RecentReportSummaryDto
{
    public int ReportId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ReporterName { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public class UserContributionScoreDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public double ContributionScore { get; set; }
    public int ApprovedPublicDocuments { get; set; }
    public int UniqueDownloads { get; set; }
    public int UniqueBookmarks { get; set; }
    public int ModerationApprovedDocuments { get; set; }
    public int UpheldViolations { get; set; }
    public int RemovedDocuments { get; set; }
    public DateTime CalculatedAt { get; set; }
}

public class TopContributorDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public double ContributionScore { get; set; }
    public int ApprovedPublicDocuments { get; set; }
    public int UniqueDownloads { get; set; }
    public int UniqueBookmarks { get; set; }
    public int ModerationApprovedDocuments { get; set; }
}

public class DocumentActivitySummaryDto
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TotalActivities { get; set; }
    public int UniqueDownloads { get; set; }
    public int UniqueBookmarks { get; set; }
    public int ViewCount { get; set; }
}

public class CommunityAnalyticsSummaryDto
{
    public ReportedAccountAnalyticsDto? MostReportedAccount { get; set; }
    public DocumentEngagementAnalyticsDto? MostDownloadedDocument { get; set; }
    public DocumentEngagementAnalyticsDto? MostBookmarkedDocument { get; set; }
}

public class ReportedAccountAnalyticsDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ReportedDocumentCount { get; set; }
    public int TotalReports { get; set; }
    public int PendingReports { get; set; }
    public int ConfirmedReports { get; set; }
}

public class DocumentEngagementAnalyticsDto
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public string? FileExtension { get; set; }
    public string SharingPermission { get; set; } = string.Empty;
    public int UniqueDownloads { get; set; }
    public int UniqueBookmarks { get; set; }
    public int ViewCount { get; set; }
}
