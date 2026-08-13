using System;

namespace AIStudyHub.Application.DTOs;

public class ChatSessionDto
{
    public int SessionId
    {
        get; set;
    }
    public string SessionName { get; set; } = null!;
    public int UserId
    {
        get; set;
    }
    public bool? IsPinned
    {
        get; set;
    }
    public DateTime? CreatedAt
    {
        get; set;
    }
}

public class ChatMessageDto
{
    public int MessageId
    {
        get; set;
    }
    public int SessionId
    {
        get; set;
    }
    public string Sender { get; set; } = null!; // USER or BOT
    public string MessageContent { get; set; } = null!;
    public bool? Display
    {
        get; set;
    }
    public DateTime? CreatedAt
    {
        get; set;
    }
}

public class CreateSessionDto
{
    public string SessionName { get; set; } = null!;
}

public class AskQuestionDto
{
    public string MessageContent { get; set; } = null!;
    public int? DocumentId
    {
        get; set;
    } // Optional document context for AI Chat
}
