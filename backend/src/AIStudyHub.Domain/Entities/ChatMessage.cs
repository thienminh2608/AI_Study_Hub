using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class ChatMessage
{
    public int MessageId { get; set; }

    public int SessionId { get; set; }

    public string Sender { get; set; } = null!;

    public string MessageContent { get; set; } = null!;

    public bool? Display { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ChatSession Session { get; set; } = null!;
}
