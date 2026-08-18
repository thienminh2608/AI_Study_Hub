import React, { useState, useEffect } from 'react';
import { api } from '../services/api';
import { useUiFeedback } from '../context/UiFeedbackContext';
import {
  Search,
  UserCheck,
  UserMinus,
  UserX,
  UserPlus,
  Loader,
  Mail,
  ShieldAlert,
  Users,
} from 'lucide-react';

import { Pagination } from '../components/Pagination';

interface Friend {
  userId: number;
  username: string;
  email: string;
  role: string;
  tierId: number;
  status: string; // Relationship status, e.g., "ACCEPTED"
}

interface PendingRequest {
  userId: number;
  username: string;
  email: string;
  status: string; // e.g., "PENDING"
  isRequester: boolean; // Tells if the current user sent the request or received it
}

interface FriendSearchResult extends Friend {}

export const Friends: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const [activeTab, setActiveTab] = useState<'friends' | 'pending' | 'blocked'>('friends');

  // Lists & Pagination
  const [friendsList, setFriendsList] = useState<Friend[]>([]);
  const [friendsPage, setFriendsPage] = useState(1);
  const [friendsTotalPages, setFriendsTotalPages] = useState(1);
  const [friendsTotalCount, setFriendsTotalCount] = useState(0);

  const [pendingList, setPendingRequestList] = useState<PendingRequest[]>([]);
  const [pendingPage, setPendingPage] = useState(1);
  const [pendingTotalPages, setPendingTotalPages] = useState(1);
  const [pendingTotalCount, setPendingTotalCount] = useState(0);

  const [blockedList, setBlockedList] = useState<Friend[]>([]);
  const [blockedPage, setBlockedPage] = useState(1);
  const [blockedTotalPages, setBlockedTotalPages] = useState(1);
  const [blockedTotalCount, setBlockedTotalCount] = useState(0);

  // Search
  const [searchEmail, setSearchEmail] = useState('');
  const [searchResult, setSearchResult] = useState<FriendSearchResult | null>(null);
  const [searchError, setSearchError] = useState('');

  // Loading
  const [loading, setLoading] = useState(true);
  const [searching, setSearching] = useState(false);

  const loadAllData = async (fPage = 1, pPage = 1, bPage = 1) => {
    setLoading(true);
    try {
      const friendsRes = await api.friendshipExtra.getFriendsPaged(fPage, 10);
      setFriendsList(friendsRes.items);
      setFriendsPage(friendsRes.pageNumber);
      setFriendsTotalPages(friendsRes.totalPages);
      setFriendsTotalCount(friendsRes.totalCount);

      const pendingRes = await api.friendshipExtra.getPendingRequestsPaged(pPage, 10);
      setPendingRequestList(pendingRes.items);
      setPendingPage(pendingRes.pageNumber);
      setPendingTotalPages(pendingRes.totalPages);
      setPendingTotalCount(pendingRes.totalCount);

      const blockedRes = await api.friendshipExtra.getBlockedUsersPaged(bPage, 10);
      setBlockedList(blockedRes.items);
      setBlockedPage(blockedRes.pageNumber);
      setBlockedTotalPages(blockedRes.totalPages);
      setBlockedTotalCount(blockedRes.totalCount);
    } catch (err: any) {
      console.error('Error loading social data:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadAllData();
  }, []);

  // Search User Handler
  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    setSearchError('');
    setSearchResult(null);
    if (!searchEmail.trim()) return;

    setSearching(true);
    try {
      const result = await api.friendship.find(searchEmail.trim());
      setSearchResult(result);
    } catch (err: any) {
      setSearchError(err.message || 'Không tìm thấy sinh viên này.');
    } finally {
      setSearching(false);
    }
  };

  // Friendship Actions
  const handleSendRequest = async (targetId: number) => {
    try {
      await api.friendship.sendRequest(targetId);
      notify('Đã gửi lời mời kết bạn.', 'success');
      loadAllData();
      if (searchResult && searchResult.userId === targetId) {
        setSearchResult({ ...searchResult, status: 'PENDING_SENT' });
      }
    } catch (err: any) {
      notify(err.message || 'Không thể kết bạn.', 'error');
    }
  };

  const handleRespondRequest = async (targetId: number, status: string) => {
    try {
      await api.friendship.respond(targetId, status);
      loadAllData();
      if (searchResult && searchResult.userId === targetId) {
        setSearchResult({ ...searchResult, status: status === 'ACCEPTED' ? 'ACCEPTED' : 'NONE' });
      }
    } catch (err: any) {
      notify(err.message || 'Thao tác thất bại.', 'error');
    }
  };

  const handleDeleteFriendship = async (targetId: number) => {
    if (
      !(await confirm({
        title: 'Xác nhận quan hệ bạn bè',
        message: 'Bạn chắc chắn muốn thực hiện thao tác này?',
        confirmLabel: 'Xác nhận',
        danger: true,
      }))
    )
      return;
    try {
      await api.friendship.delete(targetId);
      loadAllData();
      if (searchResult && searchResult.userId === targetId) {
        setSearchResult({ ...searchResult, status: 'NONE' });
      }
      notify('Thao tác đã hoàn tất.', 'success');
    } catch (err: any) {
      notify(err.message || 'Thao tác thất bại.', 'error');
    }
  };

  return (
    <div className="social-container">
      <div className="social-grid">
        {/* Left Side: Friends tabs & list view */}
        <div className="friends-pane glass-panel">
          {/* Tab Selector */}
          <div className="tab-selector">
            <button
              onClick={() => setActiveTab('friends')}
              className={`tab-btn ${activeTab === 'friends' ? 'active' : ''}`}
            >
              Bạn bè ({friendsList.length})
            </button>
            <button
              onClick={() => setActiveTab('pending')}
              className={`tab-btn ${activeTab === 'pending' ? 'active' : ''}`}
            >
              Lời mời đang chờ ({pendingList.length})
            </button>
            <button
              onClick={() => setActiveTab('blocked')}
              className={`tab-btn ${activeTab === 'blocked' ? 'active' : ''}`}
            >
              Đã chặn ({blockedList.length})
            </button>
          </div>

          {/* List display */}
          <div className="list-content">
            {loading ? (
              <div className="loading-state">
                <Loader className="spin" size={32} />
                <p>Đang tải dữ liệu bạn bè...</p>
              </div>
            ) : activeTab === 'friends' ? (
              friendsList.length === 0 ? (
                <div className="empty-social">
                  <Users size={48} className="empty-icon" />
                  <p>Bạn chưa kết bạn với ai. Sử dụng thanh tìm kiếm để kết nối nhé!</p>
                </div>
              ) : (
                <div>
                  <div className="social-list">
                    {friendsList.map((f) => (
                      <div key={f.userId} className="social-card glass-card">
                        <div className="user-details">
                          <span className="username">{f.username}</span>
                          <span className="email">{f.email}</span>
                        </div>
                        <div className="card-actions">
                          <button
                            onClick={() => handleRespondRequest(f.userId, 'BLOCKED')}
                            className="btn-secondary block-btn"
                            title="Chặn người này"
                          >
                            <UserX size={16} />
                            <span>Chặn</span>
                          </button>
                          <button
                            onClick={() => handleDeleteFriendship(f.userId)}
                            className="btn-secondary delete-btn"
                            title="Hủy kết bạn"
                          >
                            <UserMinus size={16} />
                            <span>Xóa bạn</span>
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                  <Pagination
                    currentPage={friendsPage}
                    totalPages={friendsTotalPages}
                    totalCount={friendsTotalCount}
                    onPageChange={(p) => loadAllData(p, pendingPage, blockedPage)}
                  />
                </div>
              )
            ) : activeTab === 'pending' ? (
              pendingList.length === 0 ? (
                <div className="empty-social">
                  <Mail size={48} className="empty-icon" />
                  <p>Không có lời mời kết bạn nào đang chờ duyệt.</p>
                </div>
              ) : (
                <div>
                  <div className="social-list">
                    {pendingList.map((p) => (
                      <div key={p.userId} className="social-card glass-card">
                        <div className="user-details">
                          <span className="username">{p.username}</span>
                          <span className="email">{p.email}</span>
                          <span className="pending-badge">
                            {p.isRequester ? 'Lời mời đã gửi' : 'Yêu cầu đang chờ bạn duyệt'}
                          </span>
                        </div>
                        <div className="card-actions">
                          {p.isRequester ? (
                            <button
                              onClick={() => handleDeleteFriendship(p.userId)}
                              className="btn-secondary delete-btn"
                            >
                              Hủy lời mời
                            </button>
                          ) : (
                            <>
                              <button
                                onClick={() => handleRespondRequest(p.userId, 'ACCEPTED')}
                                className="btn-primary"
                              >
                                <UserCheck size={16} />
                                <span>Đồng ý</span>
                              </button>
                              <button
                                onClick={() => handleDeleteFriendship(p.userId)}
                                className="btn-secondary delete-btn"
                              >
                                Từ chối
                              </button>
                            </>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                  <Pagination
                    currentPage={pendingPage}
                    totalPages={pendingTotalPages}
                    totalCount={pendingTotalCount}
                    onPageChange={(p) => loadAllData(friendsPage, p, blockedPage)}
                  />
                </div>
              )
            ) : blockedList.length === 0 ? (
              <div className="empty-social">
                <ShieldAlert size={48} className="empty-icon" />
                <p>Không có người dùng nào bị chặn.</p>
              </div>
            ) : (
              <div>
                <div className="social-list">
                  {blockedList.map((b) => (
                    <div key={b.userId} className="social-card glass-card">
                      <div className="user-details">
                        <span className="username">{b.username}</span>
                        <span className="email">{b.email}</span>
                      </div>
                      <div className="card-actions">
                        <button
                          onClick={() => handleDeleteFriendship(b.userId)}
                          className="btn-primary"
                        >
                          Hủy chặn
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
                <Pagination
                  currentPage={blockedPage}
                  totalPages={blockedTotalPages}
                  totalCount={blockedTotalCount}
                  onPageChange={(p) => loadAllData(friendsPage, pendingPage, p)}
                />
              </div>
            )}
          </div>
        </div>

        {/* Right Side: Find Friends Panel */}
        <div className="find-pane glass-panel">
          <h3>Tìm kiếm sinh viên</h3>
          <p className="subtitle">Điền email để tìm kiếm người học khác trên hệ thống.</p>

          <form onSubmit={handleSearch} className="search-form">
            <div className="input-icon-wrapper">
              <Search size={18} className="input-icon" />
              <input
                type="email"
                placeholder="search@email.com..."
                value={searchEmail}
                onChange={(e) => setSearchEmail(e.target.value)}
                className="input-control"
              />
            </div>
            <button type="submit" className="btn-primary" disabled={searching}>
              {searching ? <Loader className="spin" size={16} /> : 'Tìm kiếm'}
            </button>
          </form>

          {searchError && (
            <div className="search-error glass-card">
              <span>{searchError}</span>
            </div>
          )}

          {searchResult && (
            <div className="search-result-card glass-card animate-slide-up">
              <h4>{searchResult.username}</h4>
              <p className="result-email">{searchResult.email}</p>
              <div className="status-indicator">
                Quan hệ: <strong>{searchResult.status || 'Chưa kết nối'}</strong>
              </div>

              <div className="result-actions">
                {searchResult.status === 'ACCEPTED' && (
                  <button
                    onClick={() => handleDeleteFriendship(searchResult.userId)}
                    className="btn-secondary delete-btn"
                  >
                    <UserMinus size={16} />
                    <span>Hủy kết bạn</span>
                  </button>
                )}

                {searchResult.status === 'PENDING_RECEIVED' && (
                  <>
                    <button
                      onClick={() => handleRespondRequest(searchResult.userId, 'ACCEPTED')}
                      className="btn-primary"
                    >
                      Xác nhận kết bạn
                    </button>
                    <button
                      onClick={() => handleDeleteFriendship(searchResult.userId)}
                      className="btn-secondary"
                    >
                      Hủy bỏ
                    </button>
                  </>
                )}

                {searchResult.status === 'PENDING_SENT' && (
                  <button
                    onClick={() => handleDeleteFriendship(searchResult.userId)}
                    className="btn-secondary"
                  >
                    Hủy yêu cầu kết bạn
                  </button>
                )}

                {searchResult.status === 'BLOCKED_BY_ME' && (
                  <button
                    onClick={() => handleDeleteFriendship(searchResult.userId)}
                    className="btn-primary"
                  >
                    Hủy chặn
                  </button>
                )}

                {(!searchResult.status || searchResult.status === 'NONE') && (
                  <button
                    onClick={() => handleSendRequest(searchResult.userId)}
                    className="btn-primary"
                  >
                    <UserPlus size={16} />
                    <span>Kết bạn</span>
                  </button>
                )}
              </div>
            </div>
          )}
        </div>
      </div>

      <style>{`
        .social-container {
          min-height: 80vh;
        }

        .social-grid {
          display: grid;
          grid-template-columns: 1.5fr 1fr;
          gap: 1.5rem;
          height: calc(100vh - 6rem);
        }

        .friends-pane {
          display: flex;
          flex-direction: column;
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .tab-selector {
          display: flex;
          gap: 0.5rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          padding-bottom: 0.75rem;
          margin-bottom: 1.25rem;
        }

        .tab-btn {
          background: transparent;
          border: none;
          color: var(--text-secondary);
          cursor: pointer;
          font-weight: 600;
          font-size: 0.95rem;
          padding: 0.5rem 1rem;
          border-radius: var(--radius-sm);
          transition: var(--transition-fast);
        }

        .tab-btn:hover {
          color: var(--text-primary);
          background: rgba(255, 255, 255, 0.03);
        }

        .tab-btn.active {
          color: var(--accent-purple);
          background: rgba(157, 78, 221, 0.08);
        }

        .list-content {
          flex: 1;
          overflow-y: auto;
        }

        .loading-state, .empty-social {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 250px;
          color: var(--text-muted);
          text-align: center;
          gap: 0.75rem;
        }

        .empty-icon {
          color: rgba(255, 255, 255, 0.02);
        }

        .social-list {
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }

        .social-card {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 1rem 1.25rem;
        }

        .user-details {
          display: flex;
          flex-direction: column;
          gap: 0.15rem;
        }

        .user-details .username {
          font-weight: 600;
          font-size: 1rem;
          color: var(--text-primary);
        }

        .user-details .email {
          font-size: 0.85rem;
          color: var(--text-muted);
        }

        .pending-badge {
          font-size: 0.75rem;
          color: var(--accent-blue);
          font-weight: 500;
          margin-top: 0.25rem;
        }

        .card-actions {
          display: flex;
          gap: 0.5rem;
        }

        .block-btn:hover {
          border-color: var(--danger) !important;
          color: var(--danger) !important;
        }

        .delete-btn:hover {
          border-color: var(--danger) !important;
          color: var(--danger) !important;
        }

        .find-pane {
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .find-pane h3 {
          margin-bottom: 0.25rem;
        }

        .find-pane .subtitle {
          color: var(--text-secondary);
          font-size: 0.9rem;
          margin-bottom: 1.5rem;
        }

        .search-form {
          display: flex;
          gap: 0.5rem;
          margin-bottom: 1.5rem;
        }

        .search-form .input-icon-wrapper {
          flex: 1;
        }

        .search-error {
          background: rgba(239, 68, 68, 0.08);
          border: 1px solid rgba(239, 68, 68, 0.15);
          color: var(--danger);
          padding: 0.75rem 1rem;
          font-size: 0.9rem;
          border-radius: var(--radius-sm);
        }

        .search-result-card {
          margin-top: 1rem;
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }

        .search-result-card h4 {
          font-size: 1.1rem;
          color: var(--text-primary);
        }

        .result-email {
          font-size: 0.85rem;
          color: var(--text-muted);
        }

        .status-indicator {
          font-size: 0.9rem;
          color: var(--text-secondary);
          border-top: 1px solid rgba(255, 255, 255, 0.05);
          padding-top: 0.5rem;
        }

        .result-actions {
          display: flex;
          gap: 0.5rem;
          margin-top: 0.5rem;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        @media (max-width: 768px) {
          .social-grid {
            grid-template-columns: 1fr;
            height: auto;
          }
          .friends-pane, .find-pane {
            height: auto;
          }
        }
      `}</style>
    </div>
  );
};
