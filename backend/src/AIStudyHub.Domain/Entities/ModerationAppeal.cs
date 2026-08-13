namespace AIStudyHub.Domain.Entities;

public class ModerationAppeal
{
    public int AppealId
    {
        get; set;
    }
    public int ReportId
    {
        get; set;
    }
    public int SubmittedByUserId
    {
        get; set;
    }
    public string Explanation { get; set; } = null!;
    public string? EvidenceUrl
    {
        get; set;
    }
    public string Status { get; set; } = "PENDING";
    public int? ReviewedByUserId
    {
        get; set;
    }
    public string? ReviewNote
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
    public DateTime? ReviewedAt
    {
        get; set;
    }
    public virtual DocumentReport Report { get; set; } = null!;
}
