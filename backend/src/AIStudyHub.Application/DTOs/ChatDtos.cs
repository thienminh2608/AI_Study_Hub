using System;
using System.Collections.Generic;

namespace AIStudyHub.Application.DTOs;

public class ChatSessionDto
{
    public int SessionId { get; set; }
    public string SessionName { get; set; } = null!;
    public int UserId { get; set; }
    public bool? IsPinned { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? AttachedDocumentId { get; set; }
    public string? AttachedDocumentTitle { get; set; }
    public int? AttachedDocumentVersionId { get; set; }
    public int CurrentAttachmentEpoch { get; set; }
}

public class ChatMessageDto
{
    public int MessageId { get; set; }
    public int SessionId { get; set; }
    public string Sender { get; set; } = null!; // USER or BOT
    public string MessageContent { get; set; } = null!;
    public bool? Display { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int AttachmentEpoch { get; set; }
    public int? ContextDocumentId { get; set; }
    public int? ContextDocumentVersionId { get; set; }
    public string MessageKind { get; set; } = "USER_MESSAGE";
    public List<ChatCitationDto> Citations { get; set; } = new();
}

public class CreateSessionDto
{
    public string SessionName { get; set; } = null!;
    public int? DocumentId { get; set; }
    public int? DocumentVersionId { get; set; }
}

public class AskQuestionDto
{
    public string MessageContent { get; set; } = null!;
    public int? DocumentId { get; set; } // Optional document context for AI Chat
    public int? DocumentVersionId { get; set; }
    public int? RetryMessageId { get; set; }
}

public class SetChatDocumentDto
{
    public int? DocumentId { get; set; }
    public int? DocumentVersionId { get; set; }
}

public class CitationResolveDto
{
    public long CitationId { get; set; }
    public int MessageId { get; set; }
    public int DocumentId { get; set; }
    public int DocumentVersionId { get; set; }
    public int? ChunkId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public string? HeadingPath { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ChatCitationDto
{
    public long CitationId { get; set; }
    public int MessageId { get; set; }
    public int DocumentId { get; set; }
    public int DocumentVersionId { get; set; }
    public int? ChunkId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public string? HeadingPath { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ChatAnswerDto
{
    public string Response { get; set; } = null!;
    public List<ChatCitationDto> Citations { get; set; } = [];
}
