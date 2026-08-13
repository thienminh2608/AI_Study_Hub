namespace AIStudyHub.Domain.Entities;

public class ModerationNotice
{
    public long NoticeId
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public int? DocumentId
    {
        get; set;
    }
    public int? ReportId
    {
        get; set;
    }
    public int? TransactionId { get; set; }
    public int? RelatedUserId { get; set; }
    public string? ActionUrl { get; set; }
    public string Type { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public bool CanAppeal
    {
        get; set;
    }
    public bool IsRead
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
