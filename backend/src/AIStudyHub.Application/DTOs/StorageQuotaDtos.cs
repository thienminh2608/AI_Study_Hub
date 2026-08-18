namespace AIStudyHub.Application.DTOs;

public class StorageQuotaDto
{
    public int UserId { get; set; }
    public string TierName { get; set; } = "Free";
    public decimal UsedStorageMb { get; set; }
    public decimal MaxStorageMb { get; set; }
    public double UsagePercentage => MaxStorageMb > 0 ? (double)(UsedStorageMb / MaxStorageMb * 100) : 0;
    public bool IsQuotaExceeded => UsedStorageMb >= MaxStorageMb;
    public int AiPromptsToday { get; set; }
    public int AiPromptLimitPerDay { get; set; }
}
