using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Subscription
{
    public int TierId
    {
        get; set;
    }

    public string TierName { get; set; } = null!;

    public int MaxStorageMb
    {
        get; set;
    }

    public int AiPromptLimitPerDay
    {
        get; set;
    }

    public decimal? Price
    {
        get; set;
    }

    public int TotalStorageMb
    {
        get; set;
    }

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
