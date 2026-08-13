namespace AIStudyHub.Domain.Entities;

public class TransferConfiguration
{
    public int ConfigurationId
    {
        get; set;
    }
    public string BankCode { get; set; } = "";
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string QrTemplate { get; set; } = "compact2";
    public string TransferContentPrefix { get; set; } = "AIStudyHub";
    public bool IsActive
    {
        get; set;
    }
    public DateTime UpdatedAt
    {
        get; set;
    }
}
