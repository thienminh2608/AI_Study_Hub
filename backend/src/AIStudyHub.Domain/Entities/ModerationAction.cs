namespace AIStudyHub.Domain.Entities;

public class ModerationAction
{
    public long ActionId
    {
        get; set;
    }
    public int ActorUserId
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
    public string Action { get; set; } = null!;
    public string? PreviousStatus
    {
        get; set;
    }
    public string? NewStatus
    {
        get; set;
    }
    public string? Note
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
    public virtual User Actor { get; set; } = null!;
}
