namespace AIStudyHub.Domain.Entities;

public class DocumentActivity
{
    public long ActivityId
    {
        get; set;
    }
    public int DocumentId
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public string ActivityType { get; set; } = null!;
    public DateTime CreatedAt
    {
        get; set;
    }
    public virtual Document Document { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
