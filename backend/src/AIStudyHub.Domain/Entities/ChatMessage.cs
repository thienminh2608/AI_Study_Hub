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

    public int AttachmentEpoch { get; set; } = 0;
    public int? ContextDocumentId { get; set; }
    public int? ContextDocumentVersionId { get; set; }
    public string MessageKind { get; set; } = "USER_MESSAGE"; // USER_MESSAGE, ASSISTANT_ANSWER, TOOL_COMMAND, DOCUMENT_CONTEXT, HISTORY_SUMMARY, SYSTEM_POLICY

    public virtual ChatSession Session { get; set; } = null!;
    public virtual ICollection<ChatMessageCitation> Citations { get; set; } = new List<ChatMessageCitation>();
}
