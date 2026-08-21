using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class ChatSession
{
    public int SessionId { get; set; }

    public string SessionName { get; set; } = null!;

    public int UserId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool? IsPinned { get; set; }

    public int? AttachedDocumentId { get; set; }
    public int? AttachedDocumentVersionId { get; set; }
    public int CurrentAttachmentEpoch { get; set; } = 0;

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual User User { get; set; } = null!;
}
