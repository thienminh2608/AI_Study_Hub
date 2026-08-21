using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IAdminAnalyticsService
{
    Task<AdminOverviewAnalyticsDto> GetOverviewAnalyticsAsync(DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken = default);
    Task<UserContributionScoreDto> CalculateUserContributionScoreAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<TopContributorDto>> GetTopContributorsAsync(int limit = 10, CancellationToken cancellationToken = default);
    Task<DocumentActivitySummaryDto> GetDocumentActivitySummaryAsync(int documentId, CancellationToken cancellationToken = default);
}
