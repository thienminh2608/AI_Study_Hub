using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class DocumentReport
{
    public int ReportId
    {
        get; set;
    }

    public int DocumentId
    {
        get; set;
    }

    public int ReporterId
    {
        get; set;
    }

    public string ReasonCode { get; set; } = null!;

    public string? AdditionalDetails
    {
        get; set;
    }

    public string ReportType { get; set; } = "COMMUNITY";
    public string? ClaimantName
    {
        get; set;
    }
    public string? ClaimantEmail
    {
        get; set;
    }
    public string? OriginalWorkUrl
    {
        get; set;
    }
    public string? EvidenceDescription
    {
        get; set;
    }
    public int? AssignedModeratorId
    {
        get; set;
    }
    public string? ModeratorNote
    {
        get; set;
    }
    public string? PreviousSharingPermission
    {
        get; set;
    }
    public DateTime? RestrictedAt
    {
        get; set;
    }

    public string? Status
    {
        get; set;
    }

    public DateTime? CreatedAt
    {
        get; set;
    }

    public DateTime? ResolvedAt
    {
        get; set;
    }

    public int? ResolvedByAdminId { get; set; }
    public int? ReportedVersionId { get; set; }

    public virtual Document Document { get; set; } = null!;
    public virtual DocumentVersion? ReportedVersion { get; set; }

    public virtual ReportReasonConfig ReasonCodeNavigation { get; set; } = null!;

    public virtual User Reporter { get; set; } = null!;
    public virtual User? AssignedModerator { get; set; }

    public virtual User? ResolvedByAdmin
    {
        get; set;
    }
}
