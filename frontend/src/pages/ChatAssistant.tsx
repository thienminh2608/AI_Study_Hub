import React, { useState, useEffect, useRef } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../services/api';
import { useUiFeedback } from '../context/UiFeedbackContext';
import {
  Bot,
  Send,
  Trash2,
  Pin,
  Plus,
  Loader,
  MessageSquare,
  Search,
  BookOpen,
  Calendar,
  Link as LinkIcon,
  X,
} from 'lucide-react';

interface ChatSession {
  sessionId: number;
  sessionName: string;
  isPinned: boolean;
  createdAt: string;
  attachedDocumentId?: number | null;
  attachedDocumentTitle?: string | null;
}

interface ChatMessage {
  messageId: number;
  sender: string; // "USER" | "BOT"
  messageContent: string;
  display: boolean;
  createdAt: string;
}

interface ChatDocument {
  documentId: number;
  title: string;
  fileExtension: string;
}

interface ChatCitation {
  chunkId: number;
  documentId: number;
  page?: number | null;
  startOffset: number;
  endOffset: number;
}

export const ChatAssistant: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [citationsByMessageId, setCitationsByMessageId] = useState<Record<number, ChatCitation[]>>({});
  const [sessions, setSessions] = useState<ChatSession[]>([]);
  const [currentSessionId, setCurrentSessionId] = useState<number | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);

  // Forms & inputs
  const [newSessionTitle, setNewSessionTitle] = useState('');
  const [showCreateSession, setShowCreateSession] = useState(false);
  const [createSessionError, setCreateSessionError] = useState('');
  const [inputMessage, setInputMessage] = useState('');
  const [documents, setDocuments] = useState<ChatDocument[]>([]);
  const [selectedDocumentId, setSelectedDocumentId] = useState<number | null>(null);

  // States
  const [loadingSessions, setLoadingSessions] = useState(true);
  const [loadingMessages, setLoadingMessages] = useState(false);
  const [sending, setSending] = useState(false);
  const [agentAction, setAgentAction] = useState<string | null>(null); // Displays AI Agent running loops

  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Load Sessions
  const loadSessions = async () => {
    try {
      const data = await api.chat.getSessions();
      setSessions(data);
      if (data.length > 0) setCurrentSessionId((current) => current ?? data[0].sessionId);
    } catch (err: any) {
      console.error('Error loading chat sessions:', err);
    } finally {
      setLoadingSessions(false);
    }
  };

  // Load Messages for current session
  const loadMessages = async (sessionId: number) => {
    setLoadingMessages(true);
    try {
      const data = await api.chat.getMessages(sessionId);
      setMessages(data);
      return data as ChatMessage[];
    } catch (err: any) {
      console.error('Error loading messages:', err);
      return [];
    } finally {
      setLoadingMessages(false);
    }
  };

  useEffect(() => {
    const requestedSessionId = Number(searchParams.get('sessionId'));
    const requestedDocumentId = Number(searchParams.get('documentId'));
    if (requestedSessionId > 0) setCurrentSessionId(requestedSessionId);
    if (requestedDocumentId > 0) setSelectedDocumentId(requestedDocumentId);
    loadSessions();
    Promise.all([
      api.document.getUserDocuments(),
      api.document.getSharedWithMe(),
      requestedDocumentId > 0
        ? api.document.getById(requestedDocumentId).catch(() => null)
        : Promise.resolve(null),
    ])
      .then(([data, sharedDocuments, requestedDocument]) => {
        const next = [...data, ...sharedDocuments].filter(
          (document, index, all) =>
            all.findIndex((item) => item.documentId === document.documentId) === index,
        ) as ChatDocument[];
        if (
          requestedDocument &&
          !next.some((document) => document.documentId === requestedDocument.documentId)
        )
          next.push(requestedDocument as ChatDocument);
        setDocuments(next);
        if (requestedSessionId > 0 || requestedDocumentId > 0)
          setSearchParams({}, { replace: true });
      })
      .catch((err) => console.error('Error loading documents for chat:', err));
  }, []);

  useEffect(() => {
    if (currentSessionId) {
      loadMessages(currentSessionId);
      const session = sessions.find((item) => item.sessionId === currentSessionId);
      if (session) setSelectedDocumentId(session.attachedDocumentId ?? null);
    } else {
      setMessages([]);
    }
  }, [currentSessionId]);

  useEffect(() => {
    // Scroll to bottom on new messages
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, agentAction]);

  // Session Handlers
  const handleCreateSession = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newSessionTitle.trim()) {
      setCreateSessionError('Vui lòng nhập tên phiên trò chuyện.');
      return;
    }

    try {
      const newSession = await api.chat.createSession({ sessionName: newSessionTitle.trim() });
      setNewSessionTitle('');
      setCreateSessionError('');
      setShowCreateSession(false);
      await loadSessions();
      setCurrentSessionId(newSession.sessionId);
    } catch (err: any) {
      notify(err.message || 'Không thể tạo phiên hội thoại.', 'error');
    }
  };

  const handlePinSession = async (sessionId: number, currentPin: boolean) => {
    try {
      await api.chat.pinSession(sessionId, !currentPin);
      loadSessions();
    } catch (err: any) {
      console.error('Error pinning session:', err);
    }
  };

  const handleDeleteSession = async (sessionId: number) => {
    if (
      !(await confirm({
        title: 'Xóa phiên trò chuyện',
        message: 'Xóa phiên trò chuyện này cùng tất cả tin nhắn?',
        confirmLabel: 'Xóa phiên',
        danger: true,
      }))
    )
      return;
    try {
      await api.chat.deleteSession(sessionId);
      if (currentSessionId === sessionId) {
        setCurrentSessionId(null);
      }
      loadSessions();
      notify('Đã xóa phiên trò chuyện.', 'success');
    } catch (err: any) {
      notify(err.message || 'Không thể xóa phiên hội thoại.', 'error');
    }
  };

  // Send Message & Simulation of Agent Loop
  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputMessage.trim() || !currentSessionId || sending) return;

    const userMsgText = inputMessage.trim();
    setInputMessage('');
    setSending(true);

    // 1. Instantly append user message to local state
    const tempUserMsg: ChatMessage = {
      messageId: Date.now(),
      sender: 'USER',
      messageContent: userMsgText,
      display: true,
      createdAt: new Date().toISOString(),
    };
    setMessages((prev) => [...prev, tempUserMsg]);

    // 2. Loop indicator simulator
    // In our backend, Gemini agent runs a loop. We can simulate or show status updates based on typical agent flows
    setAgentAction('Trợ lý đang phân tích câu hỏi của bạn...');

    try {
      // Send question and get final response
      const result = await api.chat.askQuestion(currentSessionId, {
        messageContent: userMsgText,
        documentId: selectedDocumentId ?? undefined,
      });
      await loadSessions();

      // Clear action and refresh message list
      setAgentAction(null);
      const refreshed = await loadMessages(currentSessionId);
      if (result?.citations?.length) {
        const lastBotMessage = [...refreshed].reverse().find((m) => m.sender === 'BOT');
        if (lastBotMessage) {
          setCitationsByMessageId((prev) => ({
            ...prev,
            [lastBotMessage.messageId]: result.citations,
          }));
        }
      }
    } catch (err: any) {
      setAgentAction(null);
      notify(err.message || 'Gặp lỗi trong quá trình giao tiếp với AI.', 'error');
      // Refresh to make sure UI matches backend state
      await loadMessages(currentSessionId);
    } finally {
      setSending(false);
    }
  };

  const stripSourceSuffix = (text: string) => text.replace(/\n\nNguồn:.*$/s, '');

  // Simple clean markdown paragraph converter
  const renderMessageContent = (text: string) => {
    // Basic replacements for bold (**text**), linebreaks, code blocks
    const lines = text.split('\n');
    return lines.map((line, idx) => {
      let formatted = line;
      // Bold
      formatted = formatted.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
      // Code tags
      formatted = formatted.replace(/`(.*?)`/g, '<code class="inline-code">$1</code>');

      if (line.startsWith('* ') || line.startsWith('- ')) {
        return (
          <li
            key={idx}
            dangerouslySetInnerHTML={{ __html: formatted.substring(2) }}
            style={{ marginLeft: '1.5rem', marginBottom: '0.25rem' }}
          ></li>
        );
      }
      return (
        <p
          key={idx}
          dangerouslySetInnerHTML={{ __html: formatted }}
          style={{ marginBottom: '0.5rem', minHeight: line ? 'auto' : '0.5rem' }}
        ></p>
      );
    });
  };

  // Detect and format Agent Action Cards in Chat Flow
  const renderAgentStatusCard = (content: string) => {
    if (content.startsWith('SEARCH')) {
      return (
        <div className="agent-status-card">
          <Search size={16} className="status-icon active-glow" />
          <span>AI đang quét cấu trúc thư mục tài liệu của sinh viên...</span>
        </div>
      );
    }
    if (content.startsWith('VIEW/')) {
      const docId = content.substring(5).trim();
      return (
        <div className="agent-status-card">
          <BookOpen size={16} className="status-icon active-glow" />
          <span>AI đang đọc nội dung tệp tin ID #{docId}...</span>
        </div>
      );
    }
    if (content.startsWith('TODAY')) {
      return (
        <div className="agent-status-card">
          <Calendar size={16} className="status-icon active-glow" />
          <span>AI đang truy vấn mốc thời gian hiện tại...</span>
        </div>
      );
    }
    if (content.startsWith('GETLINK/')) {
      return (
        <div className="agent-status-card">
          <LinkIcon size={16} className="status-icon active-glow" />
          <span>AI đang sinh liên kết điều hướng thư mục...</span>
        </div>
      );
    }
    return null;
  };

  return (
    <div className="chat-container">
      <div className="chat-layout">
        {/* Left Sessions Sidebar */}
        <aside className="chat-sessions glass-panel">
          <div className="sessions-header">
            <h3>Phiên trò chuyện</h3>
            <button
              onClick={() => setShowCreateSession(true)}
              className="add-session-btn"
              title="Tạo phiên mới"
            >
              <Plus size={18} />
            </button>
          </div>

          <div className="sessions-scroll">
            {loadingSessions ? (
              <div className="sessions-loader">
                <Loader className="spin" />
              </div>
            ) : sessions.length === 0 ? (
              <div className="empty-sessions">
                <MessageSquare size={32} className="empty-icon" />
                <p>Chưa có phiên chat nào. Bấm "+" để tạo mới!</p>
              </div>
            ) : (
              <div className="sessions-list">
                {sessions.map((s) => (
                  <div
                    key={s.sessionId}
                    className={`session-item ${currentSessionId === s.sessionId ? 'active' : ''}`}
                  >
                    <div onClick={() => setCurrentSessionId(s.sessionId)} className="session-title">
                      <MessageSquare size={16} />
                      <span>{s.sessionName}</span>
                    </div>
                    <div className="session-actions">
                      <button
                        onClick={() => handlePinSession(s.sessionId, s.isPinned)}
                        className={`pin-btn ${s.isPinned ? 'pinned' : ''}`}
                        title={s.isPinned ? 'Bỏ ghim' : 'Ghim phiên chat'}
                      >
                        <Pin size={14} />
                      </button>
                      <button
                        onClick={() => handleDeleteSession(s.sessionId)}
                        className="delete-btn"
                        title="Xóa phiên"
                      >
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </aside>

        {/* Right Active Chat Body */}
        <div className="chat-body glass-panel">
          {currentSessionId ? (
            <>
              {/* Chat Header */}
              <div className="chat-header-bar">
                <Bot size={22} className="bot-avatar-icon" />
                <h4>
                  {sessions.find((s) => s.sessionId === currentSessionId)?.sessionName ||
                    'Trợ lý học tập AI'}
                </h4>
              </div>

              {/* Messages Panel */}
              <div className="messages-pane">
                {loadingMessages ? (
                  <div className="messages-loader">
                    <Loader className="spin" size={32} />
                    <p>Đang tải nội dung tin nhắn...</p>
                  </div>
                ) : (
                  <>
                    {messages.map((m) => {
                      // Filter command logs
                      const isStatusCard =
                        !m.display &&
                        m.sender === 'BOT' &&
                        (m.messageContent.startsWith('SEARCH') ||
                          m.messageContent.startsWith('VIEW/') ||
                          m.messageContent.startsWith('TODAY') ||
                          m.messageContent.startsWith('GETLINK/'));

                      if (isStatusCard) {
                        return (
                          <React.Fragment key={m.messageId}>
                            {renderAgentStatusCard(m.messageContent)}
                          </React.Fragment>
                        );
                      }

                      // If standard message is not displayed in frontend chat flow, hide it
                      if (!m.display) return null;

                      const isBot = m.sender === 'BOT';
                      return (
                        <div
                          key={m.messageId}
                          className={`message-bubble-wrapper ${isBot ? 'bot' : 'user'}`}
                        >
                          {isBot && <Bot size={28} className="chat-avatar bot" />}
                          <div className={`message-bubble glass-card ${isBot ? 'bot' : 'user'}`}>
                            {renderMessageContent(
                              citationsByMessageId[m.messageId]?.length
                                ? stripSourceSuffix(m.messageContent)
                                : m.messageContent,
                            )}
                            {citationsByMessageId[m.messageId]?.length ? (
                              <div className="message-citations">
                                {citationsByMessageId[m.messageId].map((citation) => (
                                  <button
                                    key={citation.chunkId}
                                    type="button"
                                    className="citation-chip"
                                    onClick={() =>
                                      navigate(
                                        `/document/${citation.documentId}?chunk=${citation.chunkId}&start=${citation.startOffset}&end=${citation.endOffset}` +
                                          (citation.page ? `&page=${citation.page}` : ''),
                                      )
                                    }
                                  >
                                    <LinkIcon size={12} />
                                    {citation.page ? `Trang ${citation.page}` : `Chunk ${citation.chunkId}`}
                                  </button>
                                ))}
                              </div>
                            ) : null}
                          </div>
                        </div>
                      );
                    })}

                    {/* Agent active action status */}
                    {agentAction && (
                      <div className="message-bubble-wrapper bot">
                        <Bot size={28} className="chat-avatar bot" />
                        <div className="message-bubble glass-card bot typing">
                          <Loader size={14} className="spin status-icon" />
                          <span>{agentAction}</span>
                        </div>
                      </div>
                    )}

                    <div ref={messagesEndRef} />
                  </>
                )}
              </div>

              {/* Message Input Form */}
              <form onSubmit={handleSendMessage} className="chat-input-form">
                <select
                  value={selectedDocumentId ?? ''}
                  onChange={async (e) => {
                    const documentId = e.target.value ? Number(e.target.value) : null;
                    setSelectedDocumentId(documentId);
                    if (!currentSessionId) return;
                    try {
                      await api.chat.setDocument(currentSessionId, documentId);
                      await loadSessions();
                      if (!documentId) notify('Đã gỡ tài liệu khỏi phiên chat.', 'success');
                    } catch (err: any) {
                      notify(err.message || 'Không thể cập nhật tài liệu đính kèm.', 'error');
                    }
                  }}
                  className="input-control"
                  disabled={sending}
                  aria-label="Tài liệu đính kèm"
                >
                  <option value="">Không đính kèm tài liệu</option>
                  {documents.map((document) => (
                    <option key={document.documentId} value={document.documentId}>
                      {document.title}.{document.fileExtension}
                    </option>
                  ))}
                </select>
                <input
                  type="text"
                  placeholder="Hỏi trợ lý về các bài tập hoặc tài liệu của bạn (ví dụ: Tóm tắt file 123)..."
                  value={inputMessage}
                  onChange={(e) => setInputMessage(e.target.value)}
                  className="input-control chat-input"
                  disabled={sending}
                />
                <button
                  type="submit"
                  className="btn-primary send-btn"
                  disabled={sending || !inputMessage.trim()}
                >
                  {sending ? <Loader className="spin" size={18} /> : <Send size={18} />}
                </button>
              </form>
            </>
          ) : (
            <div className="empty-chat-state">
              <Bot size={64} className="empty-icon" />
              <h3>Chào mừng đến với Trợ lý AI</h3>
              <p>
                Chọn một phiên trò chuyện hiện có hoặc nhấn nút "+" ở cột bên để bắt đầu học tập
                cùng AI.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* Modal: Create Chat Session */}
      {showCreateSession && (
        <div className="modal-overlay" onMouseDown={() => setShowCreateSession(false)}>
          <div
            className="modal-box create-session-modal glass-panel animate-slide-up"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <button
              className="create-session-close"
              onClick={() => setShowCreateSession(false)}
              aria-label="Đóng"
            >
              <X size={20} />
            </button>
            <span className="create-session-icon">
              <MessageSquare size={22} />
            </span>
            <small>PHIÊN TRÒ CHUYỆN MỚI</small>
            <h3>Bạn muốn học về chủ đề gì?</h3>
            <p>Đặt một tên ngắn gọn để dễ tìm lại cuộc trò chuyện.</p>
            <form onSubmit={handleCreateSession}>
              <label htmlFor="session-name">Tên phiên trò chuyện</label>
              <input
                id="session-name"
                type="text"
                placeholder="Nhập chủ đề cuộc hội thoại..."
                value={newSessionTitle}
                onChange={(e) => {
                  setNewSessionTitle(e.target.value);
                  if (e.target.value.trim()) setCreateSessionError('');
                }}
                className="input-control"
                autoFocus
              />
              {createSessionError && (
                <span className="create-session-error">{createSessionError}</span>
              )}
              <div className="modal-actions">
                <button
                  type="button"
                  onClick={() => setShowCreateSession(false)}
                  className="btn-secondary"
                >
                  Hủy
                </button>
                <button type="submit" className="btn-primary">
                  Tạo mới
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .chat-container {
          height: calc(100vh - 6rem);
        }

        .modal-overlay{position:fixed;inset:0;z-index:3000;display:grid;place-items:center;padding:1rem;background:rgba(3,7,18,.82);backdrop-filter:blur(8px)}
        .create-session-modal{position:relative;width:min(500px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:1.7rem;border:1px solid rgba(255,255,255,.1);border-radius:var(--radius-md);box-shadow:0 24px 80px rgba(0,0,0,.45);background:rgba(17,17,26,.98)}.create-session-close{position:absolute;right:1rem;top:1rem;display:grid;place-items:center;width:34px;height:34px;border:0;border-radius:8px;background:rgba(255,255,255,.05);color:var(--text-secondary);cursor:pointer}.create-session-close:hover{color:var(--text-primary);background:rgba(255,255,255,.1)}.create-session-icon{width:46px;height:46px;display:grid;place-items:center;margin-bottom:.8rem;border-radius:13px;background:rgba(0,180,216,.12);color:var(--accent-blue)}.create-session-modal h3{margin:.3rem 0;padding-right:2rem}.create-session-modal>p{margin-bottom:1.2rem;color:var(--text-secondary)}.create-session-modal form{display:grid;gap:.7rem}.create-session-modal form>label{font-weight:700}.create-session-modal .input-control{width:100%;padding:.78rem .9rem;border:1px solid rgba(255,255,255,.12);border-radius:9px;outline:0;background:rgba(255,255,255,.04);color:var(--text-primary)}.create-session-modal .input-control:focus{border-color:var(--accent-blue);box-shadow:0 0 0 3px rgba(0,180,216,.12)}.create-session-error{color:var(--danger);font-size:.82rem}.create-session-modal .modal-actions{display:flex;justify-content:flex-end;gap:.65rem;margin-top:.75rem}.create-session-modal .modal-actions button{min-width:105px}@media(max-width:520px){.create-session-modal{padding:1.25rem}.create-session-modal .modal-actions{display:grid;grid-template-columns:1fr 1fr}.create-session-modal .modal-actions button{width:100%;min-width:0}}

        .chat-layout {
          display: flex;
          gap: 1.5rem;
          height: 100%;
        }

        .chat-sessions {
          width: var(--sidebar-width);
          display: flex;
          flex-direction: column;
          padding: 1.25rem;
          border-radius: var(--radius-md);
        }

        .sessions-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 1rem;
        }

        .sessions-header h3 {
          font-size: 1rem;
          color: var(--text-secondary);
          text-transform: uppercase;
          letter-spacing: 0.05em;
        }

        .add-session-btn {
          background: rgba(255, 255, 255, 0.05);
          border: 1px solid rgba(255, 255, 255, 0.1);
          color: var(--text-primary);
          cursor: pointer;
          width: 32px;
          height: 32px;
          border-radius: 50%;
          display: flex;
          justify-content: center;
          align-items: center;
          transition: var(--transition-fast);
        }

        .add-session-btn:hover {
          background: var(--accent-glow);
          border-color: transparent;
          box-shadow: var(--shadow-neon);
        }

        .sessions-scroll {
          flex: 1;
          overflow-y: auto;
        }

        .sessions-loader, .empty-sessions {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 150px;
          color: var(--text-muted);
          text-align: center;
          gap: 0.5rem;
        }

        .empty-icon {
          color: rgba(255, 255, 255, 0.02);
        }

        .sessions-list {
          display: flex;
          flex-direction: column;
          gap: 0.4rem;
        }

        .session-item {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 0.6rem 0.8rem;
          border-radius: var(--radius-sm);
          transition: var(--transition-fast);
          cursor: pointer;
          border: 1px solid transparent;
        }

        .session-item:hover {
          background: rgba(255, 255, 255, 0.03);
        }

        .session-item.active {
          background: rgba(0, 180, 216, 0.08);
          border-color: rgba(0, 180, 216, 0.15);
        }

        .session-title {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          flex: 1;
          overflow: hidden;
          color: var(--text-secondary);
        }

        .session-item.active .session-title {
          color: var(--text-primary);
          font-weight: 600;
        }

        .session-title span {
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          font-size: 0.9rem;
        }

        .session-actions {
          display: flex;
          gap: 0.15rem;
          opacity: 0.3;
          transition: var(--transition-fast);
        }

        .session-item:hover .session-actions, .session-item.active .session-actions {
          opacity: 1;
        }

        .pin-btn, .delete-btn {
          background: transparent;
          border: none;
          color: var(--text-muted);
          cursor: pointer;
          padding: 0.25rem;
          border-radius: 4px;
          transition: var(--transition-fast);
        }

        .pin-btn:hover {
          color: var(--accent-blue);
        }

        .pin-btn.pinned {
          color: var(--accent-blue);
          transform: rotate(45deg);
        }

        .delete-btn:hover {
          color: var(--danger);
        }

        .chat-body {
          flex: 1;
          display: flex;
          flex-direction: column;
          border-radius: var(--radius-md);
          overflow: hidden;
        }

        .chat-header-bar {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          padding: 1rem 1.5rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          background: rgba(255, 255, 255, 0.01);
        }

        .bot-avatar-icon {
          color: var(--accent-blue);
        }

        .messages-pane {
          flex: 1;
          padding: 1.5rem;
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .messages-loader {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 100%;
          color: var(--text-muted);
          gap: 0.75rem;
        }

        .message-bubble-wrapper {
          display: flex;
          gap: 0.75rem;
          max-width: 85%;
        }

        .message-bubble-wrapper.user {
          align-self: flex-end;
          flex-direction: row-reverse;
        }

        .message-bubble-wrapper.bot {
          align-self: flex-start;
        }

        .chat-avatar {
          border-radius: 50%;
          background: rgba(255, 255, 255, 0.03);
          padding: 0.25rem;
          flex-shrink: 0;
        }

        .chat-avatar.bot {
          color: var(--accent-blue);
          border: 1px solid rgba(0, 180, 216, 0.2);
        }

        .message-bubble {
          padding: 1rem 1.25rem;
          font-size: 0.95rem;
          line-height: 1.5;
        }

        .message-bubble.user {
          background: var(--bg-tertiary);
          border-color: rgba(255, 255, 255, 0.08);
          border-bottom-right-radius: 0;
        }

        .message-bubble.bot {
          border-bottom-left-radius: 0;
        }

        .message-bubble.bot.typing {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          color: var(--text-secondary);
          font-style: italic;
          padding: 0.75rem 1.25rem;
        }

        .message-citations {
          display: flex;
          flex-wrap: wrap;
          gap: 0.4rem;
          margin-top: 0.6rem;
          padding-top: 0.6rem;
          border-top: 1px solid rgba(255, 255, 255, 0.08);
        }

        .citation-chip {
          display: inline-flex;
          align-items: center;
          gap: 0.3rem;
          padding: 0.25rem 0.6rem;
          border-radius: 999px;
          border: 1px solid rgba(0, 180, 216, 0.25);
          background: rgba(0, 180, 216, 0.08);
          color: var(--accent-blue);
          font-size: 0.75rem;
          cursor: pointer;
        }

        .citation-chip:hover {
          background: rgba(0, 180, 216, 0.16);
        }

        .agent-status-card {
          align-self: center;
          display: inline-flex;
          align-items: center;
          gap: 0.5rem;
          background: rgba(0, 180, 216, 0.04);
          border: 1px solid rgba(0, 180, 216, 0.1);
          border-radius: var(--radius-sm);
          padding: 0.5rem 1rem;
          font-size: 0.8rem;
          color: var(--text-secondary);
          margin: 0.25rem 0;
        }

        .status-icon {
          color: var(--accent-blue);
        }

        .inline-code {
          background: rgba(255, 255, 255, 0.06);
          padding: 0.1rem 0.3rem;
          border-radius: 4px;
          font-family: monospace;
          color: var(--accent-blue);
        }

        .chat-input-form {
          display: flex;
          gap: 0.75rem;
          padding: 1.25rem 1.5rem;
          border-top: 1px solid rgba(255, 255, 255, 0.05);
          background: rgba(0, 0, 0, 0.15);
        }

        .chat-input {
          height: 48px;
          border-radius: var(--radius-md);
        }

        .send-btn {
          width: 48px;
          height: 48px;
          border-radius: var(--radius-md);
          display: flex;
          justify-content: center;
          align-items: center;
          flex-shrink: 0;
        }

        .empty-chat-state {
          display: flex;
          flex-direction: column;
          justify-content: center;
          align-items: center;
          height: 100%;
          color: var(--text-muted);
          text-align: center;
          gap: 1rem;
        }

        .empty-chat-state h3 {
          color: var(--text-primary);
        }

        .empty-chat-state p {
          max-width: 320px;
          font-size: 0.9rem;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }
      `}</style>
    </div>
  );
};
