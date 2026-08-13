using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class ReportReasonConfig
{
    public string ReasonCode { get; set; } = null!;

    public string SeverityLevel { get; set; } = null!;

    public decimal BaseScore
    {
        get; set;
    }

    public decimal AutoFlagThreshold
    {
        get; set;
    }

    public string? Description
    {
        get; set;
    }

    public virtual ICollection<DocumentReport> DocumentReports { get; set; } = new List<DocumentReport>();
}
