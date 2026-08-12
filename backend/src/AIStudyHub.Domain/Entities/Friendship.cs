using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Friendship
{
    public int FriendshipId { get; set; }

    public int RequesterId { get; set; }

    public int AddresseeId { get; set; }

    public string? Status { get; set; }

    public int? BlockerId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User Addressee { get; set; } = null!;

    public virtual User? Blocker { get; set; }

    public virtual User Requester { get; set; } = null!;
}
