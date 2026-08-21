using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIStudyHub.Application.Services;

public class ChatService : IChatService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IGeminiService _geminiService;
    private readonly IPermissionService _permissionService;
    private readonly IConfiguration _configuration;
    private const int MaxAiLoop = 5;

    private const int DefaultMaxInputTokensPerRequest = 30_000;
    private const int DefaultMaxSingleMessageTokens = 8_000;
    private const int DefaultMaxHistoryTokens = 20_000;
    private const int HistoryKeepRecentCount = 10;
    private const int DefaultMaxContextTokens = 8_000;
    private const int DefaultMaxMapReduceGroups = 20;

    private const string HistorySummaryMarker = "[TÓM TẮT LỊCH SỬ CŨ]";

    public ChatService(IStudyHubDbContext dbContext, IGeminiService geminiService, IPermissionService permissionService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _geminiService = geminiService;
        _permissionService = permissionService;
        _configuration = configuration;
    }

    private int GetConfigInt(string key, int defaultValue) =>
        int.TryParse(_configuration[key], out var value) && value > 0 ? value : defaultValue;

    private int MaxInputTokensPerRequest => GetConfigInt("Gemini:MaxInputTokensPerRequest", DefaultMaxInputTokensPerRequest);
    private int MaxHistoryTokens => GetConfigInt("Gemini:MaxHistoryTokens", DefaultMaxHistoryTokens);
    private int MaxContextTokens => GetConfigInt("Gemini:MaxContextTokens", DefaultMaxContextTokens);
    private int MaxMapReduceGroups => GetConfigInt("Gemini:MaxMapReduceGroups", DefaultMaxMapReduceGroups);

    private static int EstimateTokens(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;

    private static int EstimateTokens(IEnumerable<ChatMessageDto> messages) =>
        messages.Sum(m => EstimateTokens(m.MessageContent));

    public async Task<List<ChatSessionDto>> GetUserSessionsAsync(int userId)
    {
        var sessions = await _dbContext.ChatSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync();

        var documentTitles = await _dbContext.Documents.AsNoTracking()
            .Where(d => sessions.Select(s => s.AttachedDocumentId).Contains(d.DocumentId))
            .ToDictionaryAsync(d => d.DocumentId, d => d.Title);
        return sessions.Select(s => new ChatSessionDto
        {
            SessionId = s.SessionId,
            SessionName = s.SessionName,
            UserId = s.UserId,
            IsPinned = s.IsPinned,
            CreatedAt = s.CreatedAt,
            AttachedDocumentId = s.AttachedDocumentId,
            AttachedDocumentTitle = s.AttachedDocumentId.HasValue && documentTitles.TryGetValue(s.AttachedDocumentId.Value, out var title) ? title : null,
            AttachedDocumentVersionId = s.AttachedDocumentVersionId,
            CurrentAttachmentEpoch = s.CurrentAttachmentEpoch
        }).ToList();
    }

    public async Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionDto dto)
    {
        Document? attachedDocument = null;
        int? attachedVersionId = null;

        if (dto.DocumentId.HasValue)
        {
            attachedDocument = await _dbContext.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DocumentId == dto.DocumentId.Value && !d.IsDeleted && d.AiParsingStatus == "READY");
            if (attachedDocument == null || !await _permissionService.CanViewDocumentAsync(attachedDocument.DocumentId, userId))
                throw new ArgumentException("Tài liệu không tồn tại, chưa sẵn sàng hoặc bạn không có quyền truy cập.");

            if (dto.DocumentVersionId.HasValue)
            {
                var versionExists = await _dbContext.DocumentVersions.AnyAsync(v =>
                    v.VersionId == dto.DocumentVersionId.Value && v.DocumentId == attachedDocument.DocumentId);
                if (!versionExists)
                    throw new ArgumentException("Phiên bản tài liệu không tồn tại.");
                attachedVersionId = dto.DocumentVersionId.Value;
            }
            else
            {
                attachedVersionId = await ResolveOrCreateDocumentVersionAsync(attachedDocument, cancellationToken: default);
            }
        }

        var session = new ChatSession
        {
            UserId = userId,
            SessionName = attachedDocument?.Title ?? dto.SessionName,
            IsPinned = false,
            AttachedDocumentId = attachedDocument?.DocumentId,
            AttachedDocumentVersionId = attachedVersionId,
            CurrentAttachmentEpoch = attachedDocument == null ? 0 : 1,
            CreatedAt = DateTime.Now
        };

        _dbContext.ChatSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        string initPrompt = await CreateInitPromptAsync(userId, attachedDocument);
        var initMsg = new ChatMessage
        {
            SessionId = session.SessionId,
            Sender = "USER",
            MessageContent = initPrompt,
            Display = false,
            AttachmentEpoch = 0,
            MessageKind = "SYSTEM_POLICY",
            CreatedAt = DateTime.Now
        };
        _dbContext.ChatMessages.Add(initMsg);

        if (attachedDocument != null)
        {
            _dbContext.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.SessionId,
                Sender = "USER",
                MessageContent = $"[HỆ THỐNG: Người dùng vừa đính kèm tài liệu ID '{attachedDocument.DocumentId}' ({attachedDocument.Title}) phiên bản ID '{attachedVersionId}'. BẠN PHẢI DÙNG LỆNH: VIEW/{attachedDocument.DocumentId} để đọc tài liệu này.]",
                Display = false,
                AttachmentEpoch = 1,
                ContextDocumentId = attachedDocument.DocumentId,
                ContextDocumentVersionId = attachedVersionId,
                MessageKind = "DOCUMENT_CONTEXT",
                CreatedAt = DateTime.Now
            });
        }
        await _dbContext.SaveChangesAsync();

        return new ChatSessionDto
        {
            SessionId = session.SessionId,
            SessionName = session.SessionName,
            UserId = session.UserId,
            IsPinned = session.IsPinned,
            CreatedAt = session.CreatedAt,
            AttachedDocumentId = attachedDocument?.DocumentId,
            AttachedDocumentTitle = attachedDocument?.Title,
            AttachedDocumentVersionId = attachedVersionId,
            CurrentAttachmentEpoch = session.CurrentAttachmentEpoch
        };
    }

    public async Task<bool> PinSessionAsync(int userId, int sessionId, bool pin)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);
        if (session == null)
            return false;

        session.IsPinned = pin;
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteSessionAsync(int userId, int sessionId)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);
        if (session == null)
            return false;

        _dbContext.ChatSessions.Remove(session);
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<ChatSessionDto?> SetAttachedDocumentAsync(int userId, int sessionId, int? documentId, int? versionId = null)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);
        if (session == null)
            return null;

        Document? document = null;
        int? targetVersionId = null;

        if (documentId.HasValue)
        {
            var doc = await _dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId.Value && !d.IsDeleted && d.AiParsingStatus == "READY");
            if (doc == null || !await _permissionService.CanViewDocumentAsync(doc.DocumentId, userId))
                throw new ArgumentException("Tài liệu không tồn tại, chưa sẵn sàng hoặc bạn không có quyền truy cập.");

            document = doc;

            if (versionId.HasValue)
            {
                var vExists = await _dbContext.DocumentVersions.AnyAsync(v => v.VersionId == versionId.Value && v.DocumentId == doc.DocumentId);
                if (!vExists) throw new ArgumentException("Phiên bản tài liệu không tồn tại.");
                targetVersionId = versionId.Value;
            }
            else
            {
                targetVersionId = await ResolveOrCreateDocumentVersionAsync(doc, cancellationToken: default);
            }
        }

        // Atomically increment attachment epoch and pin version
        session.AttachedDocumentId = documentId;
        session.AttachedDocumentVersionId = targetVersionId;
        session.CurrentAttachmentEpoch++;

        var systemPolicy = await _dbContext.ChatMessages.FirstOrDefaultAsync(m =>
            m.SessionId == sessionId && m.MessageKind == "SYSTEM_POLICY");
        if (systemPolicy != null)
            systemPolicy.MessageContent = await CreateInitPromptAsync(userId, document);

        if (document != null)
        {
            string attachmentText = $"[HỆ THỐNG: Người dùng vừa đính kèm tài liệu ID '{document.DocumentId}' ({document.Title}) phiên bản ID '{targetVersionId}'. BẠN PHẢI DÙNG LỆNH: VIEW/{document.DocumentId} để đọc tài liệu này.]";
            var sysMsg = new ChatMessage
            {
                SessionId = sessionId,
                Sender = "USER",
                MessageContent = attachmentText,
                Display = false,
                AttachmentEpoch = session.CurrentAttachmentEpoch,
                ContextDocumentId = document.DocumentId,
                ContextDocumentVersionId = targetVersionId,
                MessageKind = "DOCUMENT_CONTEXT",
                CreatedAt = DateTime.Now
            };
            _dbContext.ChatMessages.Add(sysMsg);
        }

        await _dbContext.SaveChangesAsync();

        return new ChatSessionDto
        {
            SessionId = session.SessionId,
            SessionName = session.SessionName,
            UserId = session.UserId,
            IsPinned = session.IsPinned,
            CreatedAt = session.CreatedAt,
            AttachedDocumentId = documentId,
            AttachedDocumentTitle = document?.Title,
            AttachedDocumentVersionId = targetVersionId,
            CurrentAttachmentEpoch = session.CurrentAttachmentEpoch
        };
    }

    public async Task<CitationResolveDto?> ResolveCitationAsync(int userId, long citationId, CancellationToken cancellationToken = default)
    {
        var citation = await _dbContext.ChatMessageCitations
            .Include(c => c.Message)
            .ThenInclude(m => m.Session)
            .FirstOrDefaultAsync(c => c.CitationId == citationId, cancellationToken);

        if (citation == null)
            return null;

        bool isSessionOwner = citation.Message?.Session?.UserId == userId;
        bool canViewDoc = await _permissionService.CanViewDocumentAsync(citation.DocumentId, userId);

        if (!isSessionOwner && !canViewDoc)
            return null;

        return new CitationResolveDto
        {
            CitationId = citation.CitationId,
            MessageId = citation.MessageId,
            DocumentId = citation.DocumentId,
            DocumentVersionId = citation.DocumentVersionId,
            ChunkId = citation.ChunkId,
            DocumentTitle = citation.DocumentTitleSnapshot,
            VersionNumber = citation.VersionNumberSnapshot,
            FileExtension = citation.FileExtensionSnapshot,
            PageNumber = citation.PageNumberSnapshot,
            StartOffset = citation.StartOffsetSnapshot,
            EndOffset = citation.EndOffsetSnapshot,
            HeadingPath = citation.HeadingPathSnapshot,
            Snippet = citation.Snippet,
            CreatedAt = citation.CreatedAt
        };
    }

    public async Task<List<ChatMessageDto>> GetSessionMessagesAsync(int userId, int sessionId)
    {
        var sessionExists = await _dbContext.ChatSessions.AnyAsync(s => s.SessionId == sessionId && s.UserId == userId);
        if (!sessionExists)
            return new List<ChatMessageDto>();

        return await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId && m.Display == true)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageDto
            {
                MessageId = m.MessageId,
                SessionId = m.SessionId,
                Sender = m.Sender,
                MessageContent = m.MessageContent,
                Display = m.Display,
                CreatedAt = m.CreatedAt,
                AttachmentEpoch = m.AttachmentEpoch,
                ContextDocumentId = m.ContextDocumentId,
                ContextDocumentVersionId = m.ContextDocumentVersionId,
                MessageKind = m.MessageKind,
                Citations = m.Citations.Select(c => new ChatCitationDto
                {
                    CitationId = c.CitationId,
                    MessageId = c.MessageId,
                    DocumentId = c.DocumentId,
                    DocumentVersionId = c.DocumentVersionId,
                    ChunkId = c.ChunkId,
                    DocumentTitle = c.DocumentTitleSnapshot,
                    VersionNumber = c.VersionNumberSnapshot,
                    FileExtension = c.FileExtensionSnapshot,
                    PageNumber = c.PageNumberSnapshot,
                    StartOffset = c.StartOffsetSnapshot,
                    EndOffset = c.EndOffsetSnapshot,
                    HeadingPath = c.HeadingPathSnapshot,
                    Snippet = c.Snippet,
                    CreatedAt = c.CreatedAt
                }).ToList()
            })
            .ToListAsync();
    }

    public async Task<ChatAnswerDto> ProcessUserMessageAsync(int userId, int sessionId, AskQuestionDto dto, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId, cancellationToken);
        if (session == null)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập phiên chat này.");
        }

        if (session.AttachedDocumentId.HasValue && !session.AttachedDocumentVersionId.HasValue)
        {
            var legacyDocument = await _dbContext.Documents.FirstOrDefaultAsync(d =>
                d.DocumentId == session.AttachedDocumentId.Value && !d.IsDeleted, cancellationToken);
            if (legacyDocument != null)
            {
                session.AttachedDocumentVersionId = await ResolveOrCreateDocumentVersionAsync(legacyDocument, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        ChatMessage? retryMessage = null;
        if (dto.RetryMessageId.HasValue)
        {
            retryMessage = await _dbContext.ChatMessages.FirstOrDefaultAsync(m =>
                m.MessageId == dto.RetryMessageId.Value &&
                m.SessionId == sessionId &&
                m.Sender == "USER" &&
                m.Display == true &&
                m.MessageKind == "USER_MESSAGE" &&
                m.AttachmentEpoch == session.CurrentAttachmentEpoch,
                cancellationToken);
            if (retryMessage == null)
                throw new ArgumentException("Không tìm thấy câu hỏi lỗi hợp lệ để thử lại.");
            dto.MessageContent = retryMessage.MessageContent;
        }

        if (session.AttachedDocumentId.HasValue && dto.DocumentId.HasValue &&
            (dto.DocumentId.Value != session.AttachedDocumentId.Value ||
             (dto.DocumentVersionId.HasValue && dto.DocumentVersionId != session.AttachedDocumentVersionId)))
        {
            throw new ArgumentException("Phiên chat đã khóa vào một tài liệu khác. Hãy tạo phiên chat mới hoặc đổi tài liệu bằng bộ chọn đính kèm trước khi hỏi.");
        }

        if (EstimateTokens(dto.MessageContent) > DefaultMaxSingleMessageTokens)
        {
            throw new ArgumentException($"Câu hỏi quá dài (ước tính > {DefaultMaxSingleMessageTokens} token). Vui lòng rút gọn nội dung.");
        }

        // 1. Rate Limit Verification
        var user = await _dbContext.Users.Include(u => u.Tier).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
            throw new ArgumentException("Người dùng không tồn tại.");

        int limitPerDay = user.Tier?.AiPromptLimitPerDay ?? 10;
        int currentPrompts = user.AiPromptsToday ?? 0;
        DateTime lastReset = user.LastPromptReset ?? DateTime.Now;
        DateTime now = DateTime.Now;

        if ((now - lastReset).TotalHours >= 24)
        {
            currentPrompts = 0;
            lastReset = now;
        }

        if (currentPrompts >= limitPerDay)
        {
            var nextReset = lastReset.AddDays(1);
            var cooldown = nextReset - now;
            throw new InvalidOperationException($"Hết lượt câu hỏi! Gói của bạn tối đa {limitPerDay} câu/ngày. Thời gian hồi lượt tiếp theo còn: {cooldown.Hours} giờ {cooldown.Minutes} phút.");
        }

        int promptCountBeforeThisRequest = currentPrompts;
        DateTime lastResetBeforeThisRequest = lastReset;

        // Increment prompt count
        user.AiPromptsToday = currentPrompts + 1;
        user.LastPromptReset = lastReset;
        var remainingPrompts = limitPerDay - user.AiPromptsToday.Value;
        if (remainingPrompts is 1 or 2 && !await _dbContext.ModerationNotices.AnyAsync(n =>
            n.UserId == userId && n.Type == "AI_PROMPT_LOW" && n.CreatedAt.Date == now.Date && n.Message.Contains($"{remainingPrompts} lượt")))
        {
            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = userId,
                Type = "AI_PROMPT_LOW",
                Title = "Bạn sắp hết lượt chat AI",
                Message = $"Bạn còn {remainingPrompts} lượt hỏi AI trong chu kỳ hiện tại.",
                ActionUrl = "/premium",
                IsRead = false,
                CreatedAt = now
            });
        }
        await _dbContext.SaveChangesAsync();

        // 2. Save User Message (if present). A retry reuses the persisted failed question.
        if (retryMessage == null && !string.IsNullOrWhiteSpace(dto.MessageContent))
        {
            var userMsg = new ChatMessage
            {
                SessionId = sessionId,
                Sender = "USER",
                MessageContent = dto.MessageContent,
                Display = true,
                AttachmentEpoch = session.CurrentAttachmentEpoch,
                ContextDocumentId = session.AttachedDocumentId,
                ContextDocumentVersionId = session.AttachedDocumentVersionId,
                MessageKind = "USER_MESSAGE",
                CreatedAt = DateTime.Now
            };
            _dbContext.ChatMessages.Add(userMsg);
            await _dbContext.SaveChangesAsync();
        }

        // 3. Handle Attachment
        if (!session.AttachedDocumentId.HasValue && dto.DocumentId.HasValue)
        {
            await SetAttachedDocumentAsync(userId, sessionId, dto.DocumentId.Value, dto.DocumentVersionId);
            session = await _dbContext.ChatSessions.FirstAsync(s => s.SessionId == sessionId);
        }

        var attachedDocumentId = session.AttachedDocumentId;
        var attachedDocumentVersionId = session.AttachedDocumentVersionId;
        var currentEpoch = session.CurrentAttachmentEpoch;

        // 4. Agent Loop
        int loopCount = 0;
        string? finalResponse = null;
        List<int> citedChunkIds = [];
        HashSet<int> allowedCitationChunkIds = [];
        bool searchedDocuments = false;
        bool utilityCommandHandled = false;
        bool documentInventoryRequest = IsDocumentInventoryRequest(dto.MessageContent ?? string.Empty);
        bool ownedDocumentInventoryRequest = IsOwnedDocumentInventoryRequest(dto.MessageContent ?? string.Empty);

        // Inventory questions are answered deterministically from the permission-filtered database.
        // They do not need document content or an available AI provider.
        if (documentInventoryRequest && !attachedDocumentId.HasValue)
            finalResponse = await BuildChatScopeDocumentInventoryResponseAsync(userId, ownedDocumentInventoryRequest);
        else if (documentInventoryRequest && attachedDocumentId.HasValue)
        {
            var attachedTitle = await _dbContext.Documents.AsNoTracking()
                .Where(d => d.DocumentId == attachedDocumentId.Value && !d.IsDeleted)
                .Select(d => d.Title)
                .FirstOrDefaultAsync(cancellationToken);
            finalResponse = attachedTitle == null
                ? "Phiên chat đang gắn với một tài liệu không còn khả dụng."
                : $"Phiên chat này đang khóa vào duy nhất tài liệu “{attachedTitle}”. Tôi không đọc tài liệu nào khác trong phiên này.";
        }

        try
        {
        while (finalResponse == null && loopCount < MaxAiLoop)
        {
            loopCount++;

            var history = await GetBoundedHistoryAsync(sessionId, currentEpoch, cancellationToken);
            history = TrimToTokenBudget(history, MaxInputTokensPerRequest);

            var geminiResult = await _geminiService.GetGeminiResponseAsync(history, "CHAT", cancellationToken);
            string aiResponse = geminiResult.Content;

            // Record AI observability metrics (privacy-safe: no raw prompt/response text)
            _dbContext.AiUsages.Add(new AiUsage
            {
                UserId = session.UserId,
                Provider = geminiResult.Provider,
                Model = geminiResult.Model,
                Operation = geminiResult.Operation,
                PromptTokens = geminiResult.PromptTokens,
                CompletionTokens = geminiResult.CompletionTokens,
                CachedTokens = geminiResult.CachedTokens,
                TotalTokens = geminiResult.TotalTokens,
                LatencyMs = geminiResult.LatencyMs,
                Status = geminiResult.Status,
                ErrorCode = geminiResult.ErrorCode,
                EstimatedCost = geminiResult.EstimatedCost,
                Currency = geminiResult.Currency,
                PricingVersion = geminiResult.PricingVersion,
                RequestId = geminiResult.RequestId,
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();

            string trimmedResponse = aiResponse.Trim();

            var isToolCommand = trimmedResponse.StartsWith("SEARCH", StringComparison.OrdinalIgnoreCase) ||
                                trimmedResponse.StartsWith("VIEW/", StringComparison.OrdinalIgnoreCase) ||
                                trimmedResponse.StartsWith("VIEW /", StringComparison.OrdinalIgnoreCase) ||
                                trimmedResponse.StartsWith("TODAY", StringComparison.OrdinalIgnoreCase) ||
                                trimmedResponse.StartsWith("GETLINK/", StringComparison.OrdinalIgnoreCase) ||
                                trimmedResponse.StartsWith("GETLINK /", StringComparison.OrdinalIgnoreCase);
            if (attachedDocumentId.HasValue && allowedCitationChunkIds.Count == 0 && !isToolCommand)
            {
                _dbContext.ChatMessages.Add(new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = $"Từ chối câu trả lời vừa rồi vì chưa đọc tài liệu được ghim. Chỉ dùng VIEW/{attachedDocumentId.Value}; không SEARCH, không đọc hoặc liệt kê tài liệu khác, không dùng kiến thức bên ngoài.",
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "SYSTEM_POLICY",
                    CreatedAt = DateTime.Now
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (TryParseGroundedResponse(trimmedResponse, allowedCitationChunkIds, out var groundedResponse, out var groundedCitedChunkIds))
            {
                finalResponse = groundedResponse;
                citedChunkIds = groundedCitedChunkIds;
                break;
            }

            if (trimmedResponse.StartsWith("RESPONSE:", StringComparison.OrdinalIgnoreCase))
            {
                if (attachedDocumentId.HasValue && allowedCitationChunkIds.Count == 0)
                {
                    _dbContext.ChatMessages.Add(new ChatMessage
                    {
                        SessionId = sessionId,
                        Sender = "USER",
                        MessageContent = $"Phiên này chỉ được trả lời từ tài liệu đính kèm. Hãy đọc VIEW/{attachedDocumentId.Value} trước và không dùng kiến thức hay tài liệu khác.",
                        Display = false,
                        AttachmentEpoch = currentEpoch,
                        ContextDocumentId = attachedDocumentId,
                        ContextDocumentVersionId = attachedDocumentVersionId,
                        MessageKind = "TOOL_COMMAND",
                        CreatedAt = DateTime.Now
                    });
                    await _dbContext.SaveChangesAsync();
                    continue;
                }
                if (!attachedDocumentId.HasValue && allowedCitationChunkIds.Count == 0 && !utilityCommandHandled)
                {
                    if (searchedDocuments && documentInventoryRequest)
                    {
                        var responsePayload = trimmedResponse.Substring("RESPONSE:".Length).Trim();
                        finalResponse = NormalizeModelAnswer(responsePayload);
                        break;
                    }
                    if (!searchedDocuments)
                    {
                        _dbContext.ChatMessages.Add(new ChatMessage
                        {
                            SessionId = sessionId,
                            Sender = "USER",
                            MessageContent = "Không được trả lời bằng kiến thức bên ngoài. Hãy dùng SEARCH rồi VIEW tài liệu phù hợp trước khi trả lời.",
                            Display = false,
                            AttachmentEpoch = currentEpoch,
                            MessageKind = "TOOL_COMMAND",
                            CreatedAt = DateTime.Now
                        });
                        await _dbContext.SaveChangesAsync();
                        continue;
                    }
                    finalResponse = "Không tìm thấy tài liệu liên quan đến câu hỏi của bạn trong các tài liệu đã tải lên.";
                    break;
                }
                finalResponse = trimmedResponse.Substring("RESPONSE:".Length).Trim();
                break;
            }
            else if (trimmedResponse.StartsWith("SEARCH", StringComparison.OrdinalIgnoreCase))
            {
                if (searchedDocuments && documentInventoryRequest)
                {
                    finalResponse = await BuildChatScopeDocumentInventoryResponseAsync(userId, ownedDocumentInventoryRequest);
                    break;
                }
                searchedDocuments = true;
                var botCmdMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "BOT",
                    MessageContent = trimmedResponse,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(botCmdMsg);

                string treeMsg;
                if (attachedDocumentId.HasValue)
                    treeMsg = $"Phiên chat đang khóa vào tài liệu ID {attachedDocumentId.Value}. Không được tìm kiếm hay đọc tài liệu khác. Hãy dùng VIEW/{attachedDocumentId.Value}.";
                else
                {
                    string folderTree = await BuildFolderTreeTextAsync(userId);
                    treeMsg = $"Đây là cấu trúc cây tài liệu hiện tại của sinh viên:\n{folderTree}\n\n"
                        + "Nếu người dùng đang hỏi danh sách, số lượng hoặc tên các tài liệu, hãy trả lời ngay từ cây này bằng RESPONSE và không SEARCH lại. "
                        + "Chỉ dùng VIEW khi người dùng hỏi về nội dung bên trong một tài liệu.";
                }

                var sysMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = treeMsg,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(sysMsg);
                await _dbContext.SaveChangesAsync();
            }
            else if (trimmedResponse.StartsWith("VIEW/", StringComparison.OrdinalIgnoreCase) ||
                     trimmedResponse.StartsWith("VIEW /", StringComparison.OrdinalIgnoreCase))
            {
                var botCmdMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "BOT",
                    MessageContent = trimmedResponse,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(botCmdMsg);

                string docIdStr = trimmedResponse.Substring(trimmedResponse.IndexOf('/') + 1).Trim();
                string systemResponseText = "";

                if (int.TryParse(docIdStr, out int docId))
                {
                    if (attachedDocumentId.HasValue && docId != attachedDocumentId.Value)
                    {
                        systemResponseText = $"Phiên chat chỉ được phép đọc tài liệu đang đính kèm ID {attachedDocumentId.Value}. Không được truy cập tài liệu ID {docId}.";
                    }
                    else
                    {
                        var doc = await _dbContext.Documents.FirstOrDefaultAsync(d =>
                            d.DocumentId == docId && !d.IsDeleted);
                        if (doc == null || !await _permissionService.CanViewDocumentAsync(docId, userId))
                        {
                            systemResponseText = $"Hệ thống không tìm thấy tài liệu có id \"{docId}\" trong kho lưu trữ của sinh viên hoặc sinh viên không có quyền truy cập. Hãy thông báo cho sinh viên biết.";
                        }
                    else
                    {
                        string parsingStatus = doc.AiParsingStatus ?? "PENDING";
                        string ext = doc.FileExtension?.ToLower() ?? "";
                        var supportedExtensions = new[] { "pdf", "docx", "txt", "xlsx", "pptx", "md" };

                        if (!supportedExtensions.Contains(ext))
                        {
                            systemResponseText = $"Tài liệu \"{doc.Title}\" có định dạng (.{ext}) không được hệ thống hỗ trợ đọc chữ. Hãy xin lỗi và thông báo cho sinh viên biết AI hiện tại chỉ hỗ trợ: PDF, DOCX, TXT, XLSX, PPTX.";
                        }
                        else if ("FAILED".Equals(parsingStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            systemResponseText = $"Tài liệu \"{doc.Title}\" đã bị lỗi trong quá trình trích xuất nội dung (Trạng thái: FAILED). Hãy xin lỗi sinh viên và khuyên họ tải lên file chuẩn khác.";
                        }
                        else if (!"READY".Equals(parsingStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            systemResponseText = $"Tài liệu \"{doc.Title}\" đang trong quá trình trích xuất văn bản (trạng thái hiện tại: {parsingStatus}). Hãy báo cho sinh viên vui lòng đợi vài giây và gửi lại yêu cầu để kiểm tra lại nội dung.";
                        }
                        else
                        {
                            var targetVerId = (docId == attachedDocumentId) ? attachedDocumentVersionId : doc.CurrentVersionId;
                            bool hasVersionedChunks = await _dbContext.DocumentChunks.AnyAsync(c => c.DocumentId == docId && c.DocumentVersionId != null);

                            var chunkCheckQuery = _dbContext.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == docId);
                            if (targetVerId.HasValue) chunkCheckQuery = chunkCheckQuery.Where(c => c.DocumentVersionId == targetVerId.Value);
                            else if (!hasVersionedChunks) chunkCheckQuery = chunkCheckQuery.Where(c => c.DocumentVersionId == null);

                            if (!await chunkCheckQuery.AnyAsync())
                            {
                                systemResponseText = $"Tài liệu \"{doc.Title}\" được tìm thấy nhưng nội dung trống rỗng hoặc hệ thống không thể quét được chữ. Hãy thông báo cho sinh viên biết.";
                            }
                            else
                            {
                                var question = dto.MessageContent ?? string.Empty;
                                RetrievedContext selected;
                                if (IsWholeDocumentRequest(question))
                                {
                                    var allChunksQuery = _dbContext.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == docId);
                                    if (targetVerId.HasValue) allChunksQuery = allChunksQuery.Where(c => c.DocumentVersionId == targetVerId.Value);
                                    else if (!hasVersionedChunks) allChunksQuery = allChunksQuery.Where(c => c.DocumentVersionId == null);

                                    var allChunks = await allChunksQuery.OrderBy(c => c.ChunkIndex).ToListAsync();
                                    selected = await SummarizeWholeDocumentAsync(allChunks, question, cancellationToken);
                                }
                                else
                                {
                                    selected = await RetrieveContextAsync(docId, targetVerId, question);
                                }
                                allowedCitationChunkIds = selected.ChunkIds.ToHashSet();
                                if (!selected.HasRelevantMatch)
                                {
                                    finalResponse = "Không tìm thấy tài liệu liên quan đến câu hỏi của bạn trong các tài liệu đã tải lên.";
                                    systemResponseText = "Không tìm thấy chunk nào liên quan đến câu hỏi. Không được dùng kiến thức bên ngoài để trả lời.";
                                }
                                else systemResponseText = $"Document: \"{doc.Title}\". Only use the context below. "
                                    + "Return strict JSON with this schema: {\"answer\":\"...\",\"citations\":[{\"chunkId\":1,\"page\":1}],\"insufficientContext\":false}. "
                                    + "Citations may only reference chunk IDs present in the context. Set insufficientContext=true when the answer is not supported.\n\n"
                                    + selected.Context;
                            }
                        }
                    }
                    }
                }
                else
                {
                    systemResponseText = "Lỗi định dạng lệnh: ID tài liệu phải là số. Ví dụ: VIEW/123";
                }

                var sysMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = systemResponseText,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "DOCUMENT_CONTEXT",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(sysMsg);
                await _dbContext.SaveChangesAsync();
                if (finalResponse != null)
                    break;
            }
            else if (trimmedResponse.StartsWith("TODAY", StringComparison.OrdinalIgnoreCase))
            {
                utilityCommandHandled = true;
                var botCmdMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "BOT",
                    MessageContent = trimmedResponse,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(botCmdMsg);

                string timeMsg = $"Đây là thời gian của hiện tại:\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                var sysMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = timeMsg,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(sysMsg);
                await _dbContext.SaveChangesAsync();
            }
            else if (trimmedResponse.StartsWith("GETLINK/", StringComparison.OrdinalIgnoreCase) ||
                     trimmedResponse.StartsWith("GETLINK /", StringComparison.OrdinalIgnoreCase))
            {
                utilityCommandHandled = true;
                var botCmdMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "BOT",
                    MessageContent = trimmedResponse,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(botCmdMsg);

                string folderIdStr = trimmedResponse.Substring(trimmedResponse.IndexOf('/') + 1).Trim();
                string systemResponseText = "";

                if (int.TryParse(folderIdStr, out int fId))
                {
                    var f = await _dbContext.Folders.FirstOrDefaultAsync(folder =>
                        folder.FolderId == fId && folder.UserId == userId);
                    if (f != null)
                    {
                        systemResponseText = $"Thư mục có id \"{fId}\" đã được tìm thấy. Gửi đường link này cho sinh viên: /explore?folderId={fId}";
                    }
                    else
                    {
                        systemResponseText = $"Hệ thống không tìm thấy thư mục có id \"{fId}\" trong kho của sinh viên. Hãy kiểm tra lại cấu trúc folder tree.";
                    }
                }
                else
                {
                    systemResponseText = "Lỗi định dạng lệnh: ID folder phải là số. Ví dụ: GETLINK/123";
                }

                var sysMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = systemResponseText,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    ContextDocumentId = attachedDocumentId,
                    ContextDocumentVersionId = attachedDocumentVersionId,
                    MessageKind = "TOOL_COMMAND",
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(sysMsg);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                finalResponse = TryExtractAnswerFromJson(trimmedResponse, out var jsonAnswer)
                    ? jsonAnswer
                    : aiResponse;
                break;
            }
        }
        }
        catch (Exception) when (finalResponse == null)
        {
            user.AiPromptsToday = promptCountBeforeThisRequest;
            user.LastPromptReset = lastResetBeforeThisRequest;
            await _dbContext.SaveChangesAsync();
            throw;
        }

        if (finalResponse == null)
        {
            finalResponse = documentInventoryRequest && !attachedDocumentId.HasValue
                ? await BuildChatScopeDocumentInventoryResponseAsync(userId, ownedDocumentInventoryRequest)
                : "Xin lỗi, hệ thống AI đang gặp sự cố xử lý. Vui lòng thử lại sau.";
        }

        finalResponse = NormalizeModelAnswer(finalResponse);

        if (citedChunkIds.Count == 0 && allowedCitationChunkIds.Count > 0)
        {
            citedChunkIds = ExtractChunkIdsFromText(finalResponse, allowedCitationChunkIds);
        }

        var finalBotMsg = new ChatMessage
        {
            SessionId = sessionId,
            Sender = "BOT",
            MessageContent = finalResponse,
            Display = true,
            AttachmentEpoch = currentEpoch,
            ContextDocumentId = attachedDocumentId,
            ContextDocumentVersionId = attachedDocumentVersionId,
            MessageKind = "ASSISTANT_ANSWER",
            CreatedAt = DateTime.Now
        };
        _dbContext.ChatMessages.Add(finalBotMsg);

        // Atomically prepare and insert citations attached to finalBotMsg
        var citations = await PersistCitationsForMessageAsync(finalBotMsg, citedChunkIds, allowedCitationChunkIds, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var citationDtos = citations.Select(c => new ChatCitationDto
        {
            CitationId = c.CitationId,
            MessageId = c.MessageId,
            DocumentId = c.DocumentId,
            DocumentVersionId = c.DocumentVersionId,
            ChunkId = c.ChunkId,
            DocumentTitle = c.DocumentTitleSnapshot,
            VersionNumber = c.VersionNumberSnapshot,
            FileExtension = c.FileExtensionSnapshot,
            PageNumber = c.PageNumberSnapshot,
            StartOffset = c.StartOffsetSnapshot,
            EndOffset = c.EndOffsetSnapshot,
            HeadingPath = c.HeadingPathSnapshot,
            Snippet = c.Snippet,
            CreatedAt = c.CreatedAt
        }).ToList();

        return new ChatAnswerDto { Response = finalResponse, Citations = citationDtos };
    }

    public static List<int> ExtractChunkIdsFromText(string text, HashSet<int> allowedIds)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(text) || allowedIds == null || allowedIds.Count == 0)
            return result;

        var matches = Regex.Matches(text, @"\[CHUNK(?::|\s+id=|\s+)?\s*(\d+)\]", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out int chunkId) && allowedIds.Contains(chunkId))
            {
                result.Add(chunkId);
            }
        }

        return result.Distinct().ToList();
    }

    public async Task<List<ChatMessageCitation>> PersistCitationsForMessageAsync(
        ChatMessage message,
        List<int> citedChunkIds,
        HashSet<int> allowedCandidateChunkIds,
        CancellationToken cancellationToken = default)
    {
        if (citedChunkIds == null || citedChunkIds.Count == 0 || allowedCandidateChunkIds == null || allowedCandidateChunkIds.Count == 0)
            return [];

        // 1. Strict Whitelist verification against prompt context candidate set
        var validChunkIds = citedChunkIds.Where(id => allowedCandidateChunkIds.Contains(id)).Distinct().ToList();
        if (validChunkIds.Count == 0)
            return [];

        // 2. Fetch chunk and document/version details
        var chunks = await _dbContext.DocumentChunks
            .Include(c => c.Document)
                .ThenInclude(d => d.DocumentVersions)
            .Include(c => c.DocumentVersion)
            .Where(c => validChunkIds.Contains(c.ChunkId) && !c.Document.IsDeleted)
            .ToListAsync(cancellationToken);

        var citationsToInsert = new List<ChatMessageCitation>();
        var seenChunkIds = new HashSet<int>();

        foreach (var chunk in chunks)
        {
            if (!seenChunkIds.Add(chunk.ChunkId)) continue; // Deduplicate

            var doc = chunk.Document;
            int versionId = 0;
            int versionNumber = 1;

            // Direct version pinning from chunk metadata:
            if (chunk.DocumentVersionId.HasValue && chunk.DocumentVersionId.Value > 0)
            {
                versionId = chunk.DocumentVersionId.Value;
                versionNumber = chunk.DocumentVersion?.VersionNumber ??
                    doc.DocumentVersions.FirstOrDefault(v => v.VersionId == versionId)?.VersionNumber ?? 1;
            }
            else if (chunk.DocumentVersion != null)
            {
                versionId = chunk.DocumentVersion.VersionId;
                versionNumber = chunk.DocumentVersion.VersionNumber;
            }
            else
            {
                // Fallback for legacy un-versioned chunks:
                var currentVersion = doc.DocumentVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
                versionId = currentVersion?.VersionId ?? doc.CurrentVersionId ?? 0;
                versionNumber = currentVersion?.VersionNumber ?? 1;

                if (versionId == 0)
                {
                    var fallbackVersion = await _dbContext.DocumentVersions.FirstOrDefaultAsync(v => v.DocumentId == doc.DocumentId, cancellationToken);
                    if (fallbackVersion != null)
                    {
                        versionId = fallbackVersion.VersionId;
                        versionNumber = fallbackVersion.VersionNumber;
                    }
                }
            }

            string rawSnippet = chunk.Text ?? string.Empty;
            string safeSnippet = rawSnippet.Length > 2000 ? rawSnippet.Substring(0, 2000) : rawSnippet;

            var citation = new ChatMessageCitation
            {
                Message = message,
                DocumentId = doc.DocumentId,
                DocumentVersionId = versionId,
                ChunkId = chunk.ChunkId,
                DocumentTitleSnapshot = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled Document" : (doc.Title.Length > 255 ? doc.Title.Substring(0, 255) : doc.Title),
                VersionNumberSnapshot = versionNumber,
                FileExtensionSnapshot = string.IsNullOrWhiteSpace(doc.FileExtension) ? "txt" : (doc.FileExtension.Length > 20 ? doc.FileExtension.Substring(0, 20) : doc.FileExtension),
                PageNumberSnapshot = chunk.PageNumber,
                StartOffsetSnapshot = chunk.StartOffset,
                EndOffsetSnapshot = chunk.EndOffset,
                HeadingPathSnapshot = string.IsNullOrWhiteSpace(chunk.HeadingPath) ? null : (chunk.HeadingPath.Length > 500 ? chunk.HeadingPath.Substring(0, 500) : chunk.HeadingPath),
                Snippet = safeSnippet,
                CreatedAt = DateTime.UtcNow
            };

            citationsToInsert.Add(citation);
            _dbContext.ChatMessageCitations.Add(citation);
        }

        return citationsToInsert;
    }

    private async Task<int> ResolveOrCreateDocumentVersionAsync(Document document, CancellationToken cancellationToken)
    {
        var existingVersionId = await _dbContext.DocumentVersions
            .Where(v => v.DocumentId == document.DocumentId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => (int?)v.VersionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingVersionId.HasValue)
            return existingVersionId.Value;

        var baselineVersion = new DocumentVersion
        {
            DocumentId = document.DocumentId,
            VersionNumber = 1,
            CloudStorageUrl = document.CloudStorageUrl,
            FileExtension = document.FileExtension,
            FileSizeMb = document.FileSizeMb,
            ChangeSummary = "Baseline tự động cho tài liệu legacy",
            CreatedByUserId = document.UserId,
            CreatedAt = document.CreatedAt ?? DateTime.UtcNow
        };
        _dbContext.DocumentVersions.Add(baselineVersion);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var trackedDocument = await _dbContext.Documents.FirstAsync(d => d.DocumentId == document.DocumentId, cancellationToken);
        trackedDocument.CurrentVersionId = baselineVersion.VersionId;
        var legacyChunks = await _dbContext.DocumentChunks
            .Where(c => c.DocumentId == document.DocumentId && c.DocumentVersionId == null)
            .ToListAsync(cancellationToken);
        foreach (var chunk in legacyChunks)
            chunk.DocumentVersionId = baselineVersion.VersionId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return baselineVersion.VersionId;
    }

    private async Task<List<ChatMessageDto>> GetBoundedHistoryAsync(int sessionId, int currentEpoch, CancellationToken cancellationToken)
    {
        var all = await _dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId && (m.AttachmentEpoch == currentEpoch || m.MessageKind == "SYSTEM_POLICY"))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageDto
            {
                Sender = m.Sender,
                MessageContent = m.MessageContent
            })
            .ToListAsync(cancellationToken);

        if (all.Count <= HistoryKeepRecentCount + 1 || EstimateTokens(all) <= MaxHistoryTokens)
            return all;

        var summaryIndex = all.FindLastIndex(m => m.MessageContent.StartsWith(HistorySummaryMarker, StringComparison.Ordinal));
        var systemMessage = all[0];
        var afterSummary = (summaryIndex >= 0 ? all.Skip(summaryIndex + 1) : all.Skip(1)).ToList();

        if (summaryIndex < 0 && afterSummary.Count > HistoryKeepRecentCount)
        {
            var toSummarize = afterSummary.Take(afterSummary.Count - HistoryKeepRecentCount).ToList();
            if (toSummarize.Count > 0)
            {
                var summaryResult = await _geminiService.GetGeminiResponseAsync([
                    new ChatMessageDto
                    {
                        Sender = "USER",
                        MessageContent = "Summarize the following conversation history concisely, preserving key facts, decisions and open questions.\n\n" +
                            string.Join("\n", toSummarize.Select(m => $"{m.Sender}: {m.MessageContent}"))
                    }
                ], "HISTORY_SUMMARY", cancellationToken);
                string summaryText = summaryResult.Content;

                var summaryContent = HistorySummaryMarker + "\n" + summaryText;
                _dbContext.ChatMessages.Add(new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = summaryContent,
                    Display = false,
                    AttachmentEpoch = currentEpoch,
                    MessageKind = "HISTORY_SUMMARY",
                    CreatedAt = DateTime.Now
                });
                await _dbContext.SaveChangesAsync(cancellationToken);

                var remaining = afterSummary.Skip(toSummarize.Count).ToList();
                var rebuilt = new List<ChatMessageDto> { systemMessage, new ChatMessageDto { Sender = "USER", MessageContent = summaryContent } };
                rebuilt.AddRange(remaining);
                return TrimToTokenBudget(rebuilt, MaxHistoryTokens);
            }
        }

        var bounded = new List<ChatMessageDto> { systemMessage };
        if (summaryIndex >= 0)
            bounded.Add(all[summaryIndex]);
        bounded.AddRange(afterSummary);
        return TrimToTokenBudget(bounded, MaxHistoryTokens);
    }

    private static List<ChatMessageDto> TrimToTokenBudget(List<ChatMessageDto> messages, int maxTokens)
    {
        if (messages.Count == 0 || EstimateTokens(messages) <= maxTokens)
            return messages;

        int tokens = EstimateTokens(messages[0].MessageContent);
        var kept = new List<ChatMessageDto>();
        for (int i = messages.Count - 1; i >= 1; i--)
        {
            int t = EstimateTokens(messages[i].MessageContent);
            if (tokens + t > maxTokens)
                break;
            tokens += t;
            kept.Insert(0, messages[i]);
        }

        var result = new List<ChatMessageDto> { messages[0] };
        result.AddRange(kept);
        return result;
    }

    private async Task<RetrievedContext> RetrieveContextAsync(int documentId, int? targetVersionId, string question)
    {
        bool hasVersionedChunks = await _dbContext.DocumentChunks.AnyAsync(c => c.DocumentId == documentId && c.DocumentVersionId != null);

        var query = _dbContext.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId);

        if (targetVersionId.HasValue)
        {
            query = query.Where(c => c.DocumentVersionId == targetVersionId.Value);
        }
        else if (hasVersionedChunks)
        {
            var maxVerId = await _dbContext.DocumentVersions
                .Where(v => v.DocumentId == documentId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => (int?)v.VersionId)
                .FirstOrDefaultAsync();

            if (maxVerId.HasValue)
                query = query.Where(c => c.DocumentVersionId == maxVerId.Value);
        }
        else
        {
            query = query.Where(c => c.DocumentVersionId == null);
        }

        var allChunks = await query.OrderBy(c => c.ChunkIndex).ToListAsync();

        if (allChunks.Count == 0)
            return new RetrievedContext(string.Empty, [], false);

        if (allChunks.Count <= 8)
        {
            var boundedAll = BuildBoundedChunks(allChunks, allChunks.Select(c => c.ChunkId).ToHashSet(), MaxContextTokens);
            return new RetrievedContext(FormatChunks(boundedAll), boundedAll.Select(c => c.ChunkId).ToList(), true);
        }

        var queryTerms = Tokenize(question).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        var termSet = queryTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scored = allChunks.Select(chunk =>
        {
            var chunkTokens = Tokenize($"{chunk.HeadingPath} {chunk.Text}").ToList();
            double score = chunkTokens
                .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
                .Sum(group => termSet.Contains(group.Key) ? 1.0 + Math.Log(1 + group.Count()) : 0);
            return new { Chunk = chunk, Score = score };
        })
        .Where(item => item.Score > 0)
        .OrderByDescending(item => item.Score)
        .ThenBy(item => item.Chunk.ChunkIndex)
        .Take(6)
        .Select(item => item.Chunk)
        .ToList();

        if (scored.Count == 0)
        {
            var fallback = allChunks.Take(8).ToList();
            var boundedFallback = BuildBoundedChunks(fallback, fallback.Select(c => c.ChunkId).ToHashSet(), MaxContextTokens);
            return new RetrievedContext(FormatChunks(boundedFallback), boundedFallback.Select(c => c.ChunkId).ToList(), true);
        }

        var indexes = scored.Select(c => c.ChunkIndex).ToHashSet();
        foreach (var index in scored.Select(c => c.ChunkIndex).ToList())
        {
            if (index > 0)
                indexes.Add(index - 1);
            indexes.Add(index + 1);
        }

        var selected = allChunks
            .Where(c => indexes.Contains(c.ChunkIndex))
            .OrderBy(c => c.ChunkIndex)
            .Take(8)
            .ToList();

        var bounded = BuildBoundedChunks(selected, scored.Select(c => c.ChunkId).ToHashSet(), MaxContextTokens);
        return new RetrievedContext(FormatChunks(bounded), bounded.Select(c => c.ChunkId).ToList(), true);
    }

    private static List<DocumentChunk> BuildBoundedChunks(List<DocumentChunk> chunks, HashSet<int> priorityChunkIds, int maxTokens)
    {
        var ordered = chunks.Where(c => priorityChunkIds.Contains(c.ChunkId))
            .Concat(chunks.Where(c => !priorityChunkIds.Contains(c.ChunkId)))
            .ToList();

        var result = new List<DocumentChunk>();
        int tokens = 0;
        foreach (var chunk in ordered)
        {
            int t = EstimateTokens(FormatChunks([chunk]));
            if (result.Count > 0 && tokens + t > maxTokens)
                continue;
            tokens += t;
            result.Add(chunk);
        }
        return result.OrderBy(c => c.ChunkIndex).ToList();
    }

    private async Task<RetrievedContext> SummarizeWholeDocumentAsync(List<DocumentChunk> chunks, string question, CancellationToken cancellationToken)
    {
        var groups = chunks.Chunk(5).Take(MaxMapReduceGroups).ToList();
        bool truncated = chunks.Count > groups.Count * 5;

        var summaries = new List<string>();
        foreach (var group in groups)
        {
            var res = await _geminiService.GetGeminiResponseAsync([
                new ChatMessageDto
                {
                    Sender = "USER",
                    MessageContent = "Summarize this document section faithfully for a later whole-document answer. Preserve key facts and cite chunk IDs in square brackets.\n\n" + FormatChunks(group)
                }
            ], "DOCUMENT_SUMMARY", cancellationToken);
            summaries.Add(res.Content);
        }

        int contextBudget = MaxContextTokens * 2;
        var keptSummaries = new List<string>();
        int summaryTokens = 0;
        foreach (var s in summaries)
        {
            int t = EstimateTokens(s);
            if (keptSummaries.Count > 0 && summaryTokens + t > contextBudget)
                break;
            summaryTokens += t;
            keptSummaries.Add(s);
        }

        var coveredChunkIds = groups.Take(keptSummaries.Count).SelectMany(g => g).Select(c => c.ChunkId).ToList();
        var note = truncated || keptSummaries.Count < summaries.Count
            ? $"\n\n[LƯU Ý: tài liệu có {chunks.Count} chunk, hệ thống chỉ tóm tắt được {coveredChunkIds.Count} chunk đầu do giới hạn ngân sách xử lý.]"
            : "";

        return new RetrievedContext(
            "Map summaries for the whole-document request: " + question + "\n\n" + string.Join("\n\n", keptSummaries) + note,
            coveredChunkIds, coveredChunkIds.Count > 0);
    }

    private static string FormatChunks(IEnumerable<DocumentChunk> chunks) => string.Join("\n\n", chunks.Select(c =>
        $"[CHUNK id={c.ChunkId} index={c.ChunkIndex} page={c.PageNumber?.ToString() ?? "null"} headings={c.HeadingPath ?? "[]"}]\n{c.Text}"));

    private static IEnumerable<string> Tokenize(string value) => Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}]{2,}")
        .Select(match => match.Value).Where(term => !StopWords.Contains(term));

    private static bool IsWholeDocumentRequest(string question) => Regex.IsMatch(question,
        @"(toàn bộ|toan bo|cả tài liệu|ca tai lieu|whole document|entire document|tóm tắt tài liệu|tom tat tai lieu)", RegexOptions.IgnoreCase);

    private static bool IsDocumentInventoryRequest(string question) => Regex.IsMatch(question,
        @"(danh sách|danh sach|liệt kê|liet ke|bao nhiêu tài liệu|bao nhieu tai lieu|có những tài liệu|co nhung tai lieu|tài liệu nào|tai lieu nao|tất cả tài liệu|tat ca tai lieu|list (the )?documents?|how many documents?|what documents?)",
        RegexOptions.IgnoreCase);

    private static bool IsOwnedDocumentInventoryRequest(string question) => Regex.IsMatch(question,
        @"(của tôi|cua toi|tôi (đã )?tải|toi (da )?tai|do tôi tải|do toi tai|my uploaded|I uploaded)",
        RegexOptions.IgnoreCase);

    private static bool TryParseGroundedResponse(string value, HashSet<int> allowedIds, out string response, out List<int> citedChunkIds)
    {
        response = string.Empty;
        citedChunkIds = [];
        if (allowedIds.Count == 0)
            return false;
        try
        {
            if (!TryGetJsonEnvelope(value, out var json))
                return false;
            var parsed = JsonSerializer.Deserialize<GroundedAiResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Answer))
                return false;
            var valid = parsed.Citations.Where(c => allowedIds.Contains(c.ChunkId)).DistinctBy(c => c.ChunkId).ToList();
            if (parsed.InsufficientContext)
            {
                response = "Không tìm thấy tài liệu liên quan đến câu hỏi của bạn trong các tài liệu đã tải lên.";
                return true;
            }
            response = parsed.Answer.Trim();
            if (valid.Count > 0)
            {
                response += "\n\nNguồn: " + string.Join(", ", valid.Select(c => c.Page.HasValue ? $"chunk {c.ChunkId} (trang {c.Page})" : $"chunk {c.ChunkId}"));
                citedChunkIds = valid.Select(c => c.ChunkId).ToList();
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryExtractAnswerFromJson(string value, out string answer)
    {
        answer = string.Empty;
        try
        {
            if (!TryGetJsonEnvelope(value, out var json))
                return false;
            var parsed = JsonSerializer.Deserialize<GroundedAiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Answer))
                return false;
            answer = parsed.InsufficientContext
                ? "Không tìm thấy tài liệu liên quan đến câu hỏi của bạn trong các tài liệu đã tải lên."
                : parsed.Answer.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeModelAnswer(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("RESPONSE:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("RESPONSE:".Length).Trim();

        return TryExtractAnswerFromJson(normalized, out var answer) ? answer : normalized;
    }

    private static bool TryGetJsonEnvelope(string value, out string json)
    {
        json = value.Trim();
        if (json.StartsWith("RESPONSE:", StringComparison.OrdinalIgnoreCase))
            json = json.Substring("RESPONSE:".Length).Trim();
        if (json.StartsWith("```"))
            json = Regex.Replace(json, @"^```(?:json)?\s*|\s*```$", "", RegexOptions.IgnoreCase).Trim();

        var firstBrace = json.IndexOf('{');
        var lastBrace = json.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
            return false;
        json = json.Substring(firstBrace, lastBrace - firstBrace + 1);
        return true;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "và", "của", "cho", "trong", "là", "the", "and", "for", "with", "this", "that", "from" };
    private sealed record RetrievedContext(string Context, List<int> ChunkIds, bool HasRelevantMatch);
    private sealed class GroundedAiResponse
    {
        public string Answer { get; set; } = string.Empty;
        public List<GroundedCitation> Citations { get; set; } = [];
        public bool InsufficientContext
        {
            get; set;
        }
    }
    private sealed class GroundedCitation
    {
        public int ChunkId
        {
            get; set;
        }
        public int? Page
        {
            get; set;
        }
    }

    private async Task<string> CreateInitPromptAsync(int userId, Document? attachedDocument = null)
    {
        string folderTree = attachedDocument == null
            ? await BuildFolderTreeTextAsync(userId)
            : $"[LOCKED DOCUMENT] {attachedDocument.Title}";
        return "System: You are an intelligent virtual personal assistant for a student inside 'AI Study Hub'. ALWAYS answer the user in Vietnamese, even when the question or retrieved content is in another language.\n\n"
               + "--- CURRENT CONTEXT ---\n"
               + $"Today's Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
               + "Folder Tree (Format: [ID] Name (Date)):\n" + folderTree + "\n"
               + "-----------------------\n\n"
               + "--- CORE OBJECTIVES ---\n"
               + "1. Answer the student's queries accurately.\n"
               + "2. Help the student locate specific folders and documents.\n"
               + "3. Analyze or summarize document contents when requested.\n\n"
               + "--- STRICT RULES ---\n"
               + "0. LANGUAGE: Every user-facing answer must be in natural Vietnamese. Do not answer in English unless the user explicitly asks for English.\n"
               + (attachedDocument == null
                   ? "0A. KNOWLEDGE SCOPE: For every substantive question, only answer from the user's accessible uploaded documents. First use SEARCH and then VIEW the most relevant document. Never use outside knowledge. If no document is relevant, reply exactly: 'RESPONSE: Không tìm thấy tài liệu liên quan đến câu hỏi của bạn trong các tài liệu đã tải lên.'\n"
                   : $"0A. LOCKED KNOWLEDGE SCOPE: This session is locked exclusively to document ID {attachedDocument.DocumentId} ({attachedDocument.Title}). You MUST use VIEW/{attachedDocument.DocumentId}. Never SEARCH, VIEW another ID, mention other documents, or use outside knowledge. If this document does not support the answer, report insufficient context.\n")
               + "0B. CITATIONS: When citing facts or concepts from viewed chunks, append the exact chunk marker [CHUNK:{chunkId}] (e.g. [CHUNK:42]) where that fact was retrieved from.\n"
               + "1. DATA PRIVACY: NEVER expose raw Folder IDs, Document IDs, or raw Creation Dates to the user in your normal responses. If asked to show the folder tree, format it into a friendly, clean list (e.g., using bullet points or emojis) without the system IDs or timestamps.\n"
               + "2. NORMAL RESPONSE FORMAT: Every response that is directed to the user MUST start exactly with the prefix 'RESPONSE: '.\n"
               + "3. TIMEOUT HANDLING: If you request a file's content and the system returns the status 'PENDING' for 3 consecutive times, you must inform the user: 'RESPONSE: The file text extraction has failed.'\n\n"
               + "--- SYSTEM COMMANDS ---\n"
               + "To fetch missing information, you can output a system command. \n"
               + "CRITICAL: If you decide to use a command, your ENTIRE output must ONLY be the command string. Do NOT include the 'RESPONSE:' prefix, do NOT include descriptions, and do NOT add conversational text.\n\n"
               + "Available Commands:\n"
               + "- SEARCH : Fetch the latest folder tree.\n"
               + "- VIEW/[document_id] : Read the content of a specific document (e.g., VIEW/15).\n"
               + "- TODAY : Check the current time and date.\n"
               + "- GETLINK/[folder_id] : Get the href link to navigate the user to a specific folder.\n\n"
               + "--- EXAMPLES ---\n"
               + "Example 1 (Normal Chat):\n"
               + "RESPONSE: Here is the summary of your document... You can view the folder containing it here: [Link]\n\n"
               + "Example 2 (Executing a Command):\n"
               + "VIEW/42\n\n"
               + "========================\n"
               + "USER INTERACTION BEGINS NOW:\n";
    }

    private async Task<string> BuildFolderTreeTextAsync(int userId)
    {
        var allFolders = await _dbContext.Folders.Where(f => f.UserId == userId && !f.IsDeleted).ToListAsync();
        var allDocuments = await _dbContext.Documents.Where(d => d.UserId == userId && !d.IsDeleted).ToListAsync();
        var sharedDocIds = await _permissionService.GetSharedDocumentIdsAsync(userId);
        var sharedDocs = await _dbContext.Documents.AsNoTracking().Include(d => d.User)
            .Where(d => sharedDocIds.Contains(d.DocumentId) && !d.IsDeleted)
            .ToListAsync();

        var tree = new StringBuilder();
        var rootFolders = allFolders.Where(f => f.ParentFolderId == null).ToList();

        foreach (var rootFolder in rootFolders)
        {
            BuildFolderTreeRecursive(tree, rootFolder, allFolders, allDocuments, 1);
        }

        foreach (var doc in allDocuments.Where(d => d.FolderId == null))
        {
            tree.AppendLine($"- [Doc ID: {doc.DocumentId}] {doc.Title} ({doc.CreatedAt:yyyy:MM:dd HH:mm:ss})");
        }

        if (sharedDocs.Count > 0)
        {
            tree.AppendLine("\n--- TÀI LIỆU ĐƯỢC BẠN BÈ CHIA SẺ ---");
            foreach (var sDoc in sharedDocs)
            {
                tree.AppendLine($"- [Doc ID: {sDoc.DocumentId}] {sDoc.Title} (Chia sẻ bởi: {sDoc.User?.Username ?? "Bạn bè"})");
            }
        }

        if (tree.Length == 0)
        {
            tree.Append("(Trống — Sinh viên chưa có thư mục hoặc tài liệu nào)");
        }

        return tree.ToString().Trim();
    }

    private async Task<string> BuildChatScopeDocumentInventoryResponseAsync(int userId, bool ownedOnly)
    {
        var documentIds = await _dbContext.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && !d.IsDeleted)
            .Select(d => d.DocumentId)
            .ToListAsync();

        if (!ownedOnly)
        {
            var sharedDocumentIds = await _permissionService.GetSharedDocumentIdsAsync(userId);
            documentIds = documentIds.Concat(sharedDocumentIds).Distinct().ToList();
        }

        var titles = await _dbContext.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.DocumentId) && !d.IsDeleted)
            .OrderBy(d => d.Title)
            .Select(d => d.Title)
            .ToListAsync();

        if (titles.Count == 0)
            return ownedOnly
                ? "Hiện tại bạn chưa tải lên tài liệu nào."
                : "Hiện tại tôi chưa thấy tài liệu nào thuộc kho của bạn hoặc được chia sẻ với bạn.";

        var scope = ownedOnly ? "bạn đã tải lên" : "thuộc kho của bạn hoặc được chia sẻ với bạn";
        return $"Hiện tại tôi có thể xem {titles.Count} tài liệu {scope}:\n"
            + string.Join("\n", titles.Select(title => $"- {title}"));
    }

    private void BuildFolderTreeRecursive(StringBuilder tree, Folder currentFolder, List<Folder> allFolders, List<Document> allDocuments, int depth)
    {
        var prefix = new string('-', depth);
        tree.AppendLine($"{prefix} [ID: {currentFolder.FolderId}] {currentFolder.FolderName} ({currentFolder.CreatedAt:yyyy:MM:dd HH:mm:ss})");

        foreach (var doc in allDocuments.Where(d => d.FolderId == currentFolder.FolderId))
        {
            tree.AppendLine($"{prefix}- [Doc ID: {doc.DocumentId}] {doc.Title} ({doc.CreatedAt:yyyy:MM:dd HH:mm:ss})");
        }

        foreach (var child in allFolders.Where(f => f.ParentFolderId == currentFolder.FolderId))
        {
            BuildFolderTreeRecursive(tree, child, allFolders, allDocuments, depth + 1);
        }
    }
}
