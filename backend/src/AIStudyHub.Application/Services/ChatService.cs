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
    private readonly IConfiguration _configuration;
    private const int MaxAiLoop = 5;

    private const int DefaultMaxInputTokensPerRequest = 30_000;
    private const int DefaultMaxSingleMessageTokens = 8_000;
    private const int DefaultMaxHistoryTokens = 20_000;
    private const int HistoryKeepRecentCount = 10;
    private const int DefaultMaxContextTokens = 8_000;
    private const int DefaultMaxMapReduceGroups = 20;

    private const string HistorySummaryMarker = "[TÓM TẮT LỊCH SỬ CŨ]";

    public ChatService(IStudyHubDbContext dbContext, IGeminiService geminiService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _geminiService = geminiService;
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
            AttachedDocumentTitle = s.AttachedDocumentId.HasValue && documentTitles.TryGetValue(s.AttachedDocumentId.Value, out var title) ? title : null
        }).ToList();
    }

    public async Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionDto dto)
    {
        var session = new ChatSession
        {
            UserId = userId,
            SessionName = dto.SessionName,
            IsPinned = false,
            CreatedAt = DateTime.Now
        };

        _dbContext.ChatSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        string initPrompt = await CreateInitPromptAsync(userId);
        var initMsg = new ChatMessage
        {
            SessionId = session.SessionId,
            Sender = "USER",
            MessageContent = initPrompt,
            Display = false,
            CreatedAt = DateTime.Now
        };
        _dbContext.ChatMessages.Add(initMsg);
        await _dbContext.SaveChangesAsync();

        return new ChatSessionDto
        {
            SessionId = session.SessionId,
            SessionName = session.SessionName,
            UserId = session.UserId,
            IsPinned = session.IsPinned,
            CreatedAt = session.CreatedAt
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

    public async Task<ChatSessionDto?> SetAttachedDocumentAsync(int userId, int sessionId, int? documentId)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);
        if (session == null)
            return null;
        Document? document = null;
        if (documentId.HasValue)
        {
            document = await _dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId.Value &&
                d.AiParsingStatus == "READY" && (d.UserId == userId || (d.SharingPermission == "PUBLIC" && d.IsFlagged != true) ||
                    _dbContext.DocumentShares.Any(s => s.DocumentId == d.DocumentId && s.SharedWithUserId == userId)));
            if (document == null)
                throw new ArgumentException("Tài liệu không tồn tại, chưa sẵn sàng hoặc bạn không có quyền truy cập.");
        }
        session.AttachedDocumentId = documentId;
        await _dbContext.SaveChangesAsync();
        return new ChatSessionDto { SessionId = session.SessionId, SessionName = session.SessionName, UserId = session.UserId,
            IsPinned = session.IsPinned, CreatedAt = session.CreatedAt, AttachedDocumentId = documentId, AttachedDocumentTitle = document?.Title };
    }

    public async Task<List<ChatMessageDto>> GetSessionMessagesAsync(int userId, int sessionId)
    {
        var session = await _dbContext.ChatSessions.AnyAsync(s => s.SessionId == sessionId && s.UserId == userId);
        if (!session)
            return new List<ChatMessageDto>();

        var messages = await _dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId && m.Display == true)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return messages.Select(m => new ChatMessageDto
        {
            MessageId = m.MessageId,
            SessionId = m.SessionId,
            Sender = m.Sender,
            MessageContent = m.MessageContent,
            Display = m.Display,
            CreatedAt = m.CreatedAt
        }).ToList();
    }

    public async Task<ChatAnswerDto> ProcessUserMessageAsync(int userId, int sessionId, AskQuestionDto dto, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId, cancellationToken);
        if (session == null)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập phiên chat này.");
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

        // 2. Save User Message (if present)
        if (!string.IsNullOrWhiteSpace(dto.MessageContent))
        {
            var userMsg = new ChatMessage
            {
                SessionId = sessionId,
                Sender = "USER",
                MessageContent = dto.MessageContent,
                Display = true,
                CreatedAt = DateTime.Now
            };
            _dbContext.ChatMessages.Add(userMsg);
            await _dbContext.SaveChangesAsync();
        }

        // 3. Handle Attachment
        if (dto.DocumentId.HasValue)
        {
            var doc = await _dbContext.Documents.FirstOrDefaultAsync(d =>
                d.DocumentId == dto.DocumentId.Value &&
                (d.UserId == userId || (d.SharingPermission == "PUBLIC" && d.IsFlagged != true)));
            if (doc != null)
            {
                session.AttachedDocumentId = doc.DocumentId;
                string attachmentText;
                if (string.IsNullOrWhiteSpace(dto.MessageContent))
                {
                    attachmentText = $"[HỆ THỐNG: Người dùng vừa đính kèm tài liệu mới ID là '{doc.DocumentId}'. BẠN PHẢI DÙNG LỆNH: VIEW/{doc.DocumentId} để đọc nó.]";
                }
                else
                {
                    attachmentText = $"[HỆ THỐNG: Người dùng vừa đính kèm tài liệu mới ID là '{doc.DocumentId}'. BẠN PHẢI DÙNG LỆNH: VIEW/{doc.DocumentId} để đọc nội dung tài liệu này TRƯỚC KHI trả lời câu hỏi bên dưới.]\n\nCâu hỏi của người dùng: {dto.MessageContent}";
                }

                var sysAttachmentMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = attachmentText,
                    Display = false,
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(sysAttachmentMsg);
                await _dbContext.SaveChangesAsync();
            }
        }

        var attachedDocumentId = session.AttachedDocumentId;

        // 4. Agent Loop
        int loopCount = 0;
        string? finalResponse = null;
        List<int> citedChunkIds = [];
        HashSet<int> allowedCitationChunkIds = [];
        bool searchedDocuments = false;
        bool utilityCommandHandled = false;

        try
        {
        while (loopCount < MaxAiLoop)
        {
            loopCount++;

            var history = await GetBoundedHistoryAsync(sessionId, cancellationToken);
            history = TrimToTokenBudget(history, MaxInputTokensPerRequest);

            string aiResponse = await _geminiService.GetGeminiResponseAsync(history, cancellationToken);
            string trimmedResponse = aiResponse.Trim();

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
                        CreatedAt = DateTime.Now
                    });
                    await _dbContext.SaveChangesAsync();
                    continue;
                }
                if (!attachedDocumentId.HasValue && allowedCitationChunkIds.Count == 0 && !utilityCommandHandled)
                {
                    if (!searchedDocuments)
                    {
                        _dbContext.ChatMessages.Add(new ChatMessage
                        {
                            SessionId = sessionId,
                            Sender = "USER",
                            MessageContent = "Không được trả lời bằng kiến thức bên ngoài. Hãy dùng SEARCH rồi VIEW tài liệu phù hợp trước khi trả lời.",
                            Display = false,
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
                searchedDocuments = true;
                var botCmdMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "BOT",
                    MessageContent = trimmedResponse,
                    Display = false,
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(botCmdMsg);

                string treeMsg;
                if (attachedDocumentId.HasValue)
                    treeMsg = $"Phiên chat đang khóa vào tài liệu ID {attachedDocumentId.Value}. Không được tìm kiếm hay đọc tài liệu khác. Hãy dùng VIEW/{attachedDocumentId.Value}.";
                else
                {
                    string folderTree = await BuildFolderTreeTextAsync(userId);
                    treeMsg = $"Đây là cấu trúc cây tài liệu hiện tại của sinh viên:\n{folderTree}";
                }

                var sysMsg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = treeMsg,
                    Display = false,
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
                        d.DocumentId == docId &&
                        (d.UserId == userId || (d.SharingPermission == "PUBLIC" && d.IsFlagged != true) ||
                         _dbContext.DocumentShares.Any(s => s.DocumentId == d.DocumentId && s.SharedWithUserId == userId)));
                    if (doc == null)
                    {
                        systemResponseText = $"Hệ thống không tìm thấy tài liệu có id \"{docId}\" trong kho lưu trữ của sinh viên. Hãy thông báo cho sinh viên biết và hỏi lại tên chính xác.";
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
                            if (!await _dbContext.DocumentChunks.AnyAsync(c => c.DocumentId == docId))
                            {
                                systemResponseText = $"Tài liệu \"{doc.Title}\" được tìm thấy nhưng nội dung trống rỗng hoặc hệ thống không thể quét được chữ. Hãy thông báo cho sinh viên biết.";
                            }
                            else
                            {
                                var question = dto.MessageContent ?? string.Empty;
                                RetrievedContext selected;
                                if (IsWholeDocumentRequest(question))
                                {
                                    var allChunks = await _dbContext.DocumentChunks.AsNoTracking()
                                        .Where(c => c.DocumentId == docId).OrderBy(c => c.ChunkIndex).ToListAsync();
                                    selected = await SummarizeWholeDocumentAsync(allChunks, question, cancellationToken);
                                }
                                else
                                {
                                    selected = await RetrieveContextAsync(docId, question);
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
                    CreatedAt = DateTime.Now
                };
                _dbContext.ChatMessages.Add(sysMsg);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                finalResponse = aiResponse;
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
            finalResponse = "Xin lỗi, hệ thống AI đang gặp sự cố xử lý. Vui lòng thử lại sau.";
        }

        var finalBotMsg = new ChatMessage
        {
            SessionId = sessionId,
            Sender = "BOT",
            MessageContent = finalResponse,
            Display = true,
            CreatedAt = DateTime.Now
        };
        _dbContext.ChatMessages.Add(finalBotMsg);
        await _dbContext.SaveChangesAsync();

        var citations = await BuildCitationsAsync(citedChunkIds);
        return new ChatAnswerDto { Response = finalResponse, Citations = citations };
    }

    private async Task<List<ChatCitationDto>> BuildCitationsAsync(List<int> citedChunkIds)
    {
        if (citedChunkIds.Count == 0)
            return [];

        var chunkInfos = await _dbContext.DocumentChunks.AsNoTracking()
            .Where(c => citedChunkIds.Contains(c.ChunkId))
            .Select(c => new { c.ChunkId, c.DocumentId, c.PageNumber, c.StartOffset, c.EndOffset })
            .ToListAsync();

        return citedChunkIds
            .Select(id => chunkInfos.FirstOrDefault(c => c.ChunkId == id))
            .Where(c => c != null)
            .Select(c => new ChatCitationDto
            {
                ChunkId = c!.ChunkId,
                DocumentId = c.DocumentId,
                Page = c.PageNumber,
                StartOffset = c.StartOffset,
                EndOffset = c.EndOffset
            }).ToList();
    }

    private async Task<List<ChatMessageDto>> GetBoundedHistoryAsync(int sessionId, CancellationToken cancellationToken)
    {
        var all = await _dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId)
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
                string summaryText = await _geminiService.GetGeminiResponseAsync([
                    new ChatMessageDto
                    {
                        Sender = "USER",
                        MessageContent = "Summarize the following conversation history concisely, preserving key facts, decisions and open questions.\n\n" +
                            string.Join("\n", toSummarize.Select(m => $"{m.Sender}: {m.MessageContent}"))
                    }
                ], cancellationToken);

                var summaryContent = HistorySummaryMarker + "\n" + summaryText;
                _dbContext.ChatMessages.Add(new ChatMessage
                {
                    SessionId = sessionId,
                    Sender = "USER",
                    MessageContent = summaryContent,
                    Display = false,
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

    private async Task<RetrievedContext> RetrieveContextAsync(int documentId, string question)
    {
        var queryTerms = Tokenize(question).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        var query = _dbContext.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId);
        if (queryTerms.Count > 0)
        {
            var term0 = queryTerms[0];
            query = query.Where(c => c.Text.Contains(term0) || (c.HeadingPath != null && c.HeadingPath.Contains(term0)));
        }
        var candidates = await query.OrderBy(c => c.ChunkIndex).Take(24).ToListAsync();
        if (candidates.Count == 0)
            return new RetrievedContext(string.Empty, [], false);
        var termSet = queryTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scored = candidates.Select(chunk => new
        {
            Chunk = chunk,
            Score = Tokenize($"{chunk.HeadingPath} {chunk.Text}")
                    .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
                    .Sum(group => termSet.Contains(group.Key) ? 1.0 + Math.Log(1 + group.Count()) : 0)
        })
            .OrderByDescending(item => item.Score).ThenBy(item => item.Chunk.ChunkIndex)
            .Take(6).Select(item => item.Chunk).ToList();

        if (scored.Count == 0 || scored.All(c => !Tokenize($"{c.HeadingPath} {c.Text}").Any(termSet.Contains)))
            return new RetrievedContext(string.Empty, [], false);
        var indexes = scored.Select(c => c.ChunkIndex).ToHashSet();
        foreach (var index in scored.Select(c => c.ChunkIndex).ToList())
        {
            if (index > 0)
                indexes.Add(index - 1);
            indexes.Add(index + 1);
        }
        var selected = await _dbContext.DocumentChunks.AsNoTracking()
            .Where(c => c.DocumentId == documentId && indexes.Contains(c.ChunkIndex))
            .OrderBy(c => c.ChunkIndex).Take(8).ToListAsync();
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
            summaries.Add(await _geminiService.GetGeminiResponseAsync([
                new ChatMessageDto
                {
                    Sender = "USER",
                    MessageContent = "Summarize this document section faithfully for a later whole-document answer. Preserve key facts and cite chunk IDs in square brackets.\n\n" + FormatChunks(group)
                }
            ], cancellationToken));
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

    private static bool TryParseGroundedResponse(string value, HashSet<int> allowedIds, out string response, out List<int> citedChunkIds)
    {
        response = string.Empty;
        citedChunkIds = [];
        if (allowedIds.Count == 0)
            return false;
        try
        {
            var json = value.Trim();
            if (json.StartsWith("```"))
                json = Regex.Replace(json, @"^```(?:json)?\s*|\s*```$", "", RegexOptions.IgnoreCase);
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

    private async Task<string> CreateInitPromptAsync(int userId)
    {
        string folderTree = await BuildFolderTreeTextAsync(userId);
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
               + "0A. KNOWLEDGE SCOPE: For every substantive question, only answer from the user's accessible uploaded documents. First use SEARCH and then VIEW the most relevant document. Never use outside knowledge. If no document is relevant, reply exactly: 'RESPONSE: Không tìm thấy tài liệu liên quan đến câu hỏi của bạn trong các tài liệu đã tải lên.'\n"
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
        var allFolders = await _dbContext.Folders.Where(f => f.UserId == userId).ToListAsync();
        var allDocuments = await _dbContext.Documents.Where(d => d.UserId == userId).ToListAsync();

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

        if (tree.Length == 0)
        {
            tree.Append("(Trống — Sinh viên chưa có thư mục hoặc tài liệu nào)");
        }

        return tree.ToString().Trim();
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
