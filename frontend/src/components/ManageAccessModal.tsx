import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../services/api';
import { useUiFeedback } from '../context/UiFeedbackContext';
import {
  X,
  Lock,
  Link as LinkIcon,
  Globe,
  Copy,
  RefreshCw,
  UserPlus,
  Trash2,
  Shield,
  Loader,
  CheckCircle2,
  Clock,
  AlertCircle,
  Send,
  RotateCcw,
  Slash,
  Users,
} from 'lucide-react';

interface ManageAccessModalProps {
  itemType: 'document' | 'folder';
  itemId: number;
  isOpen: boolean;
  onClose: () => void;
}

export const ManageAccessModal: React.FC<ManageAccessModalProps> = ({
  itemType,
  itemId,
  isOpen,
  onClose,
}) => {
  const { notify } = useUiFeedback();
  const [access, setAccess] = useState<any>(null);
  const [friends, setFriends] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [updatingAccess, setUpdatingAccess] = useState(false);
  const [emailInput, setEmailInput] = useState('');
  const [roleInput, setRoleInput] = useState<'VIEWER' | 'EDITOR'>('VIEWER');
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchAccessSettings = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.access.getAccessSettings(itemType, itemId);
      setAccess(data);
    } catch (err: any) {
      setError(err.message || 'Không thể tải cài đặt quyền truy cập.');
    } finally {
      setLoading(false);
    }
  }, [itemType, itemId]);

  const loadFriends = useCallback(async () => {
    try {
      const list = await api.friendship.getFriends();
      setFriends(list || []);
    } catch {
      // ignore
    }
  }, []);

  useEffect(() => {
    if (isOpen) {
      fetchAccessSettings();
      loadFriends();
    }
  }, [isOpen, fetchAccessSettings, loadFriends]);

  if (!isOpen) return null;

  const handleUpdateGeneralAccess = async (newGeneralAccess: string) => {
    setUpdatingAccess(true);
    try {
      await api.access.updateGeneralAccess(itemType, itemId, newGeneralAccess);
      if (newGeneralAccess === 'PUBLIC') {
        notify('Đã gửi yêu cầu xét duyệt công khai lên Moderator.', 'success');
      } else {
        notify('Đã cập nhật quyền truy cập.', 'success');
      }
      await fetchAccessSettings();
    } catch (err: any) {
      notify(err.message || 'Không thể cập nhật quyền truy cập.', 'error');
    } finally {
      setUpdatingAccess(false);
    }
  };

  const handleAddShare = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!emailInput.trim()) return;
    try {
      await api.access.addUserShare(itemType, itemId, emailInput.trim(), roleInput);
      notify(`Đã thêm quyền cho ${emailInput.trim()}`, 'success');
      setEmailInput('');
      await fetchAccessSettings();
    } catch (err: any) {
      notify(err.message || 'Không thể thêm quyền người dùng.', 'error');
    }
  };

  const handleShareWithFriend = async (friend: any, role: 'VIEWER' | 'EDITOR') => {
    try {
      const emailOrUsername = friend.email || friend.username;
      await api.access.addUserShare(itemType, itemId, emailOrUsername, role);
      notify(`Đã chia sẻ quyền ${role === 'EDITOR' ? 'Chỉnh sửa' : 'Xem'} cho ${friend.username}`, 'success');
      await fetchAccessSettings();
    } catch (err: any) {
      notify(err.message || 'Không thể chia sẻ cho bạn bè.', 'error');
    }
  };

  const handleRemoveShare = async (userId: number) => {
    try {
      await api.access.removeUserShare(itemType, itemId, userId);
      notify('Đã xóa quyền người dùng.', 'success');
      await fetchAccessSettings();
    } catch (err: any) {
      notify(err.message || 'Không thể xóa quyền.', 'error');
    }
  };

  const handleRotateLink = async () => {
    if (itemType !== 'document') return;
    try {
      await api.access.rotateShareLink(itemId);
      notify('Đã xoay Token liên kết mới.', 'success');
      await fetchAccessSettings();
    } catch (err: any) {
      notify(err.message || 'Không thể đổi token link.', 'error');
    }
  };

  const handleRevokeLink = async () => {
    if (itemType !== 'document') return;
    try {
      await api.access.revokeShareLink(itemId);
      notify('Đã thu hồi liên kết chia sẻ.', 'success');
      await fetchAccessSettings();
    } catch (err: any) {
      notify(err.message || 'Không thể thu hồi link.', 'error');
    }
  };

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    notify('Đã sao chép liên kết vào bộ nhớ tạm!', 'success');
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="access-modal-overlay" onClick={onClose}>
      <div className="access-modal-card glass-panel animate-slide-up" onClick={(e) => e.stopPropagation()}>
        <div className="access-modal-header">
          <div className="title-box">
            <Shield size={20} className="header-icon" />
            <h2>Quản lý quyền truy cập ({itemType === 'document' ? 'Tài liệu' : 'Thư mục'})</h2>
          </div>
          <button onClick={onClose} className="close-btn">
            <X size={18} />
          </button>
        </div>

        {loading ? (
          <div className="access-loading">
            <Loader className="spin" size={26} />
            <span>Đang tải cấu hình quyền...</span>
          </div>
        ) : error ? (
          <div className="access-error">{error}</div>
        ) : access ? (
          <div className="access-modal-body">
            {/* Target Item Summary */}
            <div className="item-summary-box">
              <span className="summary-title">{access.title || 'Mục không tên'}</span>
              <span className="summary-owner">Chủ sở hữu: <strong>{access.ownerName || 'N/A'}</strong></span>
            </div>

            {/* Add user share section */}
            <div className="access-section">
              <label className="section-label">Thêm người dùng được chia sẻ</label>
              <form onSubmit={handleAddShare} className="add-share-form">
                <div className="input-group">
                  <UserPlus size={16} className="input-icon" />
                  <input
                    type="text"
                    placeholder="Nhập email hoặc username người nhận..."
                    value={emailInput}
                    onChange={(e) => setEmailInput(e.target.value)}
                    className="access-input"
                  />
                </div>
                <select
                  value={roleInput}
                  onChange={(e) => setRoleInput(e.target.value as any)}
                  className="access-select"
                >
                  <option value="VIEWER">Xem (Viewer)</option>
                  <option value="EDITOR">Chỉnh sửa (Editor)</option>
                </select>
                <button type="submit" className="add-btn">
                  Thêm
                </button>
              </form>
            </div>

            {/* Quick Share for Friends */}
            {friends && friends.length > 0 && (
              <div className="access-section friends-quick-section">
                <div className="friends-header-row">
                  <div className="friends-label-box">
                    <Users size={14} className="friends-header-icon" />
                    <label className="section-label">Chia sẻ nhanh cho bạn bè</label>
                  </div>
                  <span className="friends-count-pill">{friends.length} bạn bè</span>
                </div>
                <div className="friends-chips-list custom-scroll">
                  {friends.map((friend) => {
                    const existingShare = access.shares?.find((s: any) => s.userId === friend.userId);
                    return (
                      <div key={friend.userId} className={`friend-chip ${existingShare ? 'shared' : ''}`}>
                        <div className="friend-chip-avatar">
                          {friend.username.charAt(0).toUpperCase()}
                        </div>
                        <div className="friend-chip-name" title={friend.username}>
                          {friend.username}
                        </div>
                        {existingShare ? (
                          <div className="friend-shared-status">
                            <span className="friend-role-badge">{existingShare.role}</span>
                            <button
                              type="button"
                              onClick={() => handleRemoveShare(friend.userId)}
                              className="friend-remove-btn"
                              title="Gỡ quyền"
                            >
                              <X size={12} />
                            </button>
                          </div>
                        ) : (
                          <div className="friend-action-btns">
                            <button
                              type="button"
                              onClick={() => handleShareWithFriend(friend, 'VIEWER')}
                              className="friend-btn view"
                              title="Cấp quyền Xem"
                            >
                              + Xem
                            </button>
                            <button
                              type="button"
                              onClick={() => handleShareWithFriend(friend, 'EDITOR')}
                              className="friend-btn edit"
                              title="Cấp quyền Chỉnh sửa"
                            >
                              + Sửa
                            </button>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* User shares list */}
            <div className="access-section">
              <label className="section-label">Danh sách người có quyền truy cập</label>
              <div className="shares-list custom-scroll">
                <div className="share-item owner-item">
                  <div>
                    <span className="user-name">{access.ownerName} (Bạn)</span>
                    <span className="user-email">Chủ sở hữu</span>
                  </div>
                  <span className="role-tag owner-tag">OWNER</span>
                </div>
                {access.shares?.map((share: any) => (
                  <div key={share.userId} className="share-item">
                    <div>
                      <span className="user-name">{share.username}</span>
                      <span className="user-email">{share.email}</span>
                    </div>
                    <div className="share-actions">
                      <span className="role-tag">{share.role}</span>
                      <button
                        onClick={() => handleRemoveShare(share.userId)}
                        className="remove-share-btn"
                        title="Gỡ quyền"
                      >
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* General Access Radio */}
            <div className="access-section">
              <label className="section-label">Quyền truy cập chung (General Access)</label>
              <div className="general-options">
                {[
                  {
                    value: 'RESTRICTED',
                    label: 'Bị hạn chế (Restricted)',
                    desc: 'Chỉ những người được thêm đích danh trong danh sách mới có thể mở.',
                    Icon: Lock,
                  },
                  {
                    value: 'LINK',
                    label: 'Bất kỳ ai có liên kết (Anyone with link)',
                    desc: 'Bất kỳ ai có liên kết chia sẻ đều có thể xem ngay (Không cần duyệt).',
                    Icon: LinkIcon,
                  },
                  ...(itemType === 'document'
                    ? [
                        {
                          value: 'PUBLIC',
                          label: 'Công khai Thư viện cộng đồng (Public catalog)',
                          desc: 'Hiển thị trên Thư viện tài liệu cộng đồng để tất cả sinh viên tra cứu và học tập.',
                          Icon: Globe,
                          requiresReview: true,
                        },
                      ]
                    : []),
                ].map(({ value, label, desc, Icon, requiresReview }) => {
                  const isChecked = (access.generalAccess || 'RESTRICTED') === value;
                  const isModerationPending = access.moderationStatus === 'PENDING_REVIEW';
                  const isApproved = access.moderationStatus === 'APPROVED';
                  const isRejected = access.moderationStatus === 'REJECTED';

                  return (
                    <div
                      key={value}
                      className={`radio-card ${isChecked ? 'active' : ''} ${value === 'PUBLIC' ? 'public-option-card' : ''}`}
                      onClick={() => !updatingAccess && handleUpdateGeneralAccess(value)}
                    >
                      <input
                        type="radio"
                        name="generalAccess"
                        value={value}
                        checked={isChecked}
                        disabled={updatingAccess}
                        onChange={() => {}}
                      />
                      <Icon size={18} className="radio-icon" />
                      <div className="radio-info">
                        <div className="radio-title-row">
                          <span className="radio-title">{label}</span>
                          {requiresReview && isApproved && isChecked && (
                            <span className="moderation-badge approved">
                              <CheckCircle2 size={12} /> Đã phê duyệt
                            </span>
                          )}
                          {requiresReview && isModerationPending && (
                            <span className="moderation-badge pending">
                              <Clock size={12} /> Chờ duyệt
                            </span>
                          )}
                          {requiresReview && isRejected && (
                            <span className="moderation-badge rejected">
                              <AlertCircle size={12} /> Bị từ chối
                            </span>
                          )}
                        </div>
                        <span className="radio-desc">{desc}</span>

                        {/* Moderation Status Banner when Public is selected */}
                        {value === 'PUBLIC' && isChecked && (
                          <div className="moderation-callout" onClick={(e) => e.stopPropagation()}>
                            {isApproved ? (
                              <div className="mod-status-box approved">
                                <CheckCircle2 size={15} className="mod-status-icon" />
                                <div className="mod-status-text">
                                  <strong>Tài liệu đã được Moderator phê duyệt công khai.</strong>
                                  <p>Hiện đang xuất hiện trên Thư viện cộng đồng cho toàn bộ người dùng.</p>
                                </div>
                              </div>
                            ) : isModerationPending ? (
                              <div className="mod-status-box pending">
                                <Clock size={15} className="mod-status-icon" />
                                <div className="mod-status-text">
                                  <strong>Đang chờ Moderator xét duyệt nội dung.</strong>
                                  <p>
                                    Tài liệu tạm thời được bảo mật riêng tư cho đến khi được duyệt. Bạn vẫn có thể chia sẻ trực tiếp qua liên kết.
                                  </p>
                                  <button
                                    type="button"
                                    onClick={() => handleUpdateGeneralAccess('RESTRICTED')}
                                    className="mod-action-btn cancel"
                                    disabled={updatingAccess}
                                  >
                                    <RotateCcw size={12} /> Hủy yêu cầu duyệt
                                  </button>
                                </div>
                              </div>
                            ) : isRejected ? (
                              <div className="mod-status-box rejected">
                                <AlertCircle size={15} className="mod-status-icon" />
                                <div className="mod-status-text">
                                  <strong>Yêu cầu công khai bị từ chối.</strong>
                                  <p>Lý do: {access.moderationNote || 'Nội dung chưa phù hợp tiêu chuẩn cộng đồng.'}</p>
                                  <button
                                    type="button"
                                    onClick={() => handleUpdateGeneralAccess('PUBLIC')}
                                    className="mod-action-btn re-request"
                                    disabled={updatingAccess}
                                  >
                                    <Send size={12} /> Gửi duyệt lại
                                  </button>
                                </div>
                              </div>
                            ) : (
                              <div className="mod-status-box not-requested">
                                <AlertCircle size={15} className="mod-status-icon" />
                                <div className="mod-status-text">
                                  <strong>Yêu cầu cần qua kiểm duyệt.</strong>
                                  <p>Khi chọn chế độ này, yêu cầu xét duyệt sẽ được gửi tới Ban kiểm duyệt trước khi hiển thị công khai.</p>
                                </div>
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Share Link Management for documents */}
            {itemType === 'document' && access.shareLink && (
              <div className="access-section link-section">
                <label className="section-label">Liên kết chia sẻ (Share link)</label>
                <div className="link-copy-row">
                  <input
                    type="text"
                    readOnly
                    value={window.location.origin + (access.shareLink.shareUrl || `/d/${access.shareLink.token || ''}`)}
                    className="link-input"
                  />
                  <button
                    onClick={() =>
                      copyToClipboard(
                        window.location.origin + (access.shareLink.shareUrl || `/d/${access.shareLink.token || ''}`)
                      )
                    }
                    className="copy-btn"
                  >
                    <Copy size={14} />
                    <span>{copied ? 'Đã chép' : 'Sao chép'}</span>
                  </button>
                </div>
                <div className="link-tools-row">
                  <button onClick={handleRotateLink} className="tool-btn rotate-btn">
                    <RefreshCw size={13} />
                    <span>Đổi Token (Rotate)</span>
                  </button>
                  <button onClick={handleRevokeLink} className="tool-btn revoke-btn">
                    <Slash size={13} />
                    <span>Thu hồi Link (Revoke)</span>
                  </button>
                </div>
              </div>
            )}
          </div>
        ) : null}

        <div className="access-modal-footer">
          <button onClick={onClose} className="close-footer-btn">
            Đóng
          </button>
        </div>
      </div>

      <style>{`
        .access-modal-overlay {
          position: fixed;
          inset: 0;
          z-index: 99999;
          background: rgba(0, 0, 0, 0.75);
          backdrop-filter: blur(8px);
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 1rem;
        }

        .access-modal-card {
          width: 100%;
          max-width: 520px;
          max-height: 90vh;
          display: flex;
          flex-direction: column;
          background: rgba(18, 18, 26, 0.95);
          border: 1px solid rgba(255, 255, 255, 0.12);
          border-radius: var(--radius-lg);
          box-shadow: 0 20px 50px rgba(0, 0, 0, 0.6);
          color: var(--text-primary);
          overflow: hidden;
        }

        .access-modal-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 1.25rem 1.5rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.08);
        }

        .title-box {
          display: flex;
          align-items: center;
          gap: 0.6rem;
        }

        .header-icon {
          color: var(--accent-purple);
        }

        .access-modal-header h2 {
          font-size: 1.15rem;
          font-weight: 700;
          color: var(--text-primary);
        }

        .close-btn {
          background: transparent;
          border: none;
          color: var(--text-muted);
          cursor: pointer;
          padding: 0.35rem;
          border-radius: 6px;
          transition: var(--transition-fast);
        }

        .close-btn:hover {
          color: var(--text-primary);
          background: rgba(255, 255, 255, 0.1);
        }

        .access-loading {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          padding: 3rem 1.5rem;
          gap: 0.75rem;
          color: var(--text-muted);
        }

        .access-error {
          padding: 2rem;
          text-align: center;
          color: var(--danger);
        }

        .access-modal-body {
          padding: 1.25rem 1.5rem;
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .item-summary-box {
          display: flex;
          flex-direction: column;
          gap: 0.2rem;
          background: rgba(255, 255, 255, 0.04);
          border: 1px solid rgba(255, 255, 255, 0.06);
          padding: 0.75rem 1rem;
          border-radius: var(--radius-md);
        }

        .summary-title {
          font-weight: 700;
          font-size: 0.95rem;
          color: var(--text-primary);
        }

        .summary-owner {
          font-size: 0.8rem;
          color: var(--text-muted);
        }

        .access-section {
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
        }

        .section-label {
          font-size: 0.78rem;
          font-weight: 700;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--text-muted);
        }

        .add-share-form {
          display: flex;
          gap: 0.5rem;
          align-items: center;
        }

        .input-group {
          position: relative;
          flex: 1;
        }

        .input-icon {
          position: absolute;
          left: 0.75rem;
          top: 50%;
          transform: translateY(-50%);
          color: var(--text-muted);
        }

        .access-input {
          width: 100%;
          background: rgba(255, 255, 255, 0.05);
          border: 1px solid rgba(255, 255, 255, 0.1);
          border-radius: var(--radius-sm);
          padding: 0.5rem 0.75rem 0.5rem 2.25rem;
          color: var(--text-primary);
          font-size: 0.85rem;
          outline: none;
        }

        .access-input:focus {
          border-color: var(--accent-purple);
        }

        .access-select {
          background: rgba(255, 255, 255, 0.08);
          border: 1px solid rgba(255, 255, 255, 0.1);
          border-radius: var(--radius-sm);
          padding: 0.5rem 0.6rem;
          color: var(--text-primary);
          font-size: 0.82rem;
          outline: none;
        }

        .access-select option {
          background: #181824;
          color: #fff;
        }

        .add-btn {
          background: var(--accent-purple);
          color: #fff;
          border: none;
          border-radius: var(--radius-sm);
          padding: 0.5rem 1rem;
          font-size: 0.85rem;
          font-weight: 600;
          cursor: pointer;
          transition: var(--transition-fast);
        }

        .add-btn:hover {
          opacity: 0.9;
        }

        .friends-quick-section {
          background: rgba(255, 255, 255, 0.02);
          border: 1px solid rgba(255, 255, 255, 0.06);
          border-radius: var(--radius-md);
          padding: 0.65rem 0.85rem;
        }

        .friends-header-row {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 0.45rem;
        }

        .friends-label-box {
          display: flex;
          align-items: center;
          gap: 0.35rem;
        }

        .friends-header-icon {
          color: var(--accent-purple);
        }

        .friends-count-pill {
          font-size: 0.7rem;
          color: var(--text-muted);
          background: rgba(255, 255, 255, 0.06);
          padding: 0.1rem 0.45rem;
          border-radius: 999px;
        }

        .friends-chips-list {
          display: flex;
          gap: 0.45rem;
          overflow-x: auto;
          padding-bottom: 0.3rem;
        }

        .friend-chip {
          display: flex;
          align-items: center;
          gap: 0.45rem;
          padding: 0.3rem 0.55rem;
          border-radius: 20px;
          background: rgba(255, 255, 255, 0.04);
          border: 1px solid rgba(255, 255, 255, 0.08);
          flex-shrink: 0;
          transition: var(--transition-fast);
        }

        .friend-chip.shared {
          background: rgba(157, 78, 221, 0.12);
          border-color: rgba(157, 78, 221, 0.3);
        }

        .friend-chip-avatar {
          width: 22px;
          height: 22px;
          border-radius: 50%;
          background: linear-gradient(135deg, var(--accent-purple), #7928ca);
          color: #fff;
          font-size: 0.65rem;
          font-weight: 700;
          display: flex;
          align-items: center;
          justify-content: center;
        }

        .friend-chip-name {
          font-size: 0.78rem;
          font-weight: 600;
          color: var(--text-primary);
          max-width: 90px;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .friend-action-btns {
          display: flex;
          gap: 0.25rem;
        }

        .friend-btn {
          padding: 0.15rem 0.4rem;
          border-radius: 12px;
          font-size: 0.68rem;
          font-weight: 600;
          cursor: pointer;
          border: none;
          transition: var(--transition-fast);
        }
        .friend-btn.view {
          background: rgba(255, 255, 255, 0.1);
          color: #cbd5e1;
        }
        .friend-btn.view:hover {
          background: rgba(255, 255, 255, 0.2);
          color: #fff;
        }
        .friend-btn.edit {
          background: rgba(157, 78, 221, 0.25);
          color: #d8b4fe;
        }
        .friend-btn.edit:hover {
          background: var(--accent-purple);
          color: #fff;
        }

        .friend-shared-status {
          display: flex;
          align-items: center;
          gap: 0.25rem;
        }

        .friend-role-badge {
          font-size: 0.65rem;
          font-weight: 700;
          padding: 0.1rem 0.35rem;
          border-radius: 10px;
          background: rgba(16, 185, 129, 0.2);
          color: #34d399;
        }

        .friend-remove-btn {
          background: transparent;
          border: none;
          color: #f87171;
          cursor: pointer;
          display: flex;
          align-items: center;
          padding: 0.1rem;
        }
        .friend-remove-btn:hover {
          color: #ef4444;
        }

        .shares-list {
          max-height: 140px;
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          gap: 0.35rem;
          background: rgba(0, 0, 0, 0.2);
          border: 1px solid rgba(255, 255, 255, 0.06);
          border-radius: var(--radius-md);
          padding: 0.5rem;
        }

        .share-item {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 0.45rem 0.65rem;
          border-radius: 6px;
          background: rgba(255, 255, 255, 0.02);
        }

        .user-name {
          display: block;
          font-size: 0.85rem;
          font-weight: 600;
          color: var(--text-primary);
        }

        .user-email {
          display: block;
          font-size: 0.75rem;
          color: var(--text-muted);
        }

        .share-actions {
          display: flex;
          align-items: center;
          gap: 0.5rem;
        }

        .role-tag {
          font-size: 0.72rem;
          font-weight: 700;
          padding: 0.15rem 0.5rem;
          border-radius: 4px;
          background: rgba(157, 78, 221, 0.15);
          color: var(--accent-purple);
        }

        .owner-tag {
          background: rgba(255, 255, 255, 0.1);
          color: var(--text-secondary);
        }

        .remove-share-btn {
          background: transparent;
          border: none;
          color: #f87171;
          cursor: pointer;
          padding: 0.2rem;
        }

        .general-options {
          display: flex;
          flex-direction: column;
          gap: 0.45rem;
        }

        .radio-card {
          display: flex;
          align-items: flex-start;
          gap: 0.75rem;
          padding: 0.65rem 0.85rem;
          border-radius: var(--radius-md);
          background: rgba(255, 255, 255, 0.02);
          border: 1px solid rgba(255, 255, 255, 0.06);
          cursor: pointer;
          transition: var(--transition-fast);
        }

        .radio-card:hover {
          background: rgba(255, 255, 255, 0.04);
        }

        .radio-card.active {
          border-color: var(--accent-purple);
          background: rgba(157, 78, 221, 0.1);
        }

        .radio-card input {
          margin-top: 0.25rem;
          accent-color: var(--accent-purple);
        }

        .radio-icon {
          color: var(--accent-purple);
          flex-shrink: 0;
          margin-top: 0.15rem;
        }

        .radio-info {
          display: flex;
          flex-direction: column;
          flex: 1;
        }

        .radio-title-row {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          flex-wrap: wrap;
        }

        .radio-title {
          font-size: 0.85rem;
          font-weight: 600;
          color: var(--text-primary);
        }

        .moderation-badge {
          display: inline-flex;
          align-items: center;
          gap: 0.25rem;
          font-size: 0.7rem;
          font-weight: 700;
          padding: 0.12rem 0.45rem;
          border-radius: 4px;
        }
        .moderation-badge.approved {
          background: rgba(16, 185, 129, 0.2);
          color: #34d399;
          border: 1px solid rgba(16, 185, 129, 0.35);
        }
        .moderation-badge.pending {
          background: rgba(245, 158, 11, 0.2);
          color: #fbbf24;
          border: 1px solid rgba(245, 158, 11, 0.35);
        }
        .moderation-badge.rejected {
          background: rgba(239, 68, 68, 0.2);
          color: #f87171;
          border: 1px solid rgba(239, 68, 68, 0.35);
        }

        .moderation-callout {
          margin-top: 0.55rem;
          width: 100%;
        }

        .mod-status-box {
          display: flex;
          align-items: flex-start;
          gap: 0.55rem;
          padding: 0.6rem 0.75rem;
          border-radius: 6px;
          font-size: 0.78rem;
          line-height: 1.35;
        }

        .mod-status-box.approved {
          background: rgba(16, 185, 129, 0.08);
          border: 1px solid rgba(16, 185, 129, 0.25);
          color: #a7f3d0;
        }
        .mod-status-box.approved .mod-status-icon {
          color: #34d399;
          flex-shrink: 0;
          margin-top: 0.1rem;
        }

        .mod-status-box.pending {
          background: rgba(245, 158, 11, 0.08);
          border: 1px solid rgba(245, 158, 11, 0.25);
          color: #fde68a;
        }
        .mod-status-box.pending .mod-status-icon {
          color: #fbbf24;
          flex-shrink: 0;
          margin-top: 0.1rem;
        }

        .mod-status-box.rejected {
          background: rgba(239, 68, 68, 0.08);
          border: 1px solid rgba(239, 68, 68, 0.25);
          color: #fca5a5;
        }
        .mod-status-box.rejected .mod-status-icon {
          color: #f87171;
          flex-shrink: 0;
          margin-top: 0.1rem;
        }

        .mod-status-box.not-requested {
          background: rgba(99, 102, 241, 0.08);
          border: 1px solid rgba(99, 102, 241, 0.25);
          color: #c7d2fe;
        }
        .mod-status-box.not-requested .mod-status-icon {
          color: #818cf8;
          flex-shrink: 0;
          margin-top: 0.1rem;
        }

        .mod-status-text {
          flex: 1;
        }
        .mod-status-text strong {
          display: block;
          font-size: 0.8rem;
          margin-bottom: 0.15rem;
        }
        .mod-status-text p {
          margin: 0 0 0.35rem 0;
          opacity: 0.9;
        }

        .mod-action-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          padding: 0.3rem 0.6rem;
          border-radius: 5px;
          font-size: 0.72rem;
          font-weight: 600;
          cursor: pointer;
          border: none;
          transition: var(--transition-fast);
          margin-top: 0.25rem;
        }
        .mod-action-btn.cancel {
          background: rgba(255, 255, 255, 0.1);
          color: #e2e8f0;
        }
        .mod-action-btn.cancel:hover {
          background: rgba(255, 255, 255, 0.2);
        }
        .mod-action-btn.re-request {
          background: var(--accent-purple);
          color: #fff;
        }
        .mod-action-btn.re-request:hover {
          opacity: 0.9;
        }

        .radio-desc {
          font-size: 0.75rem;
          color: var(--text-muted);
        }

        .link-copy-row {
          display: flex;
          gap: 0.5rem;
        }

        .link-input {
          flex: 1;
          background: rgba(255, 255, 255, 0.04);
          border: 1px solid rgba(255, 255, 255, 0.08);
          border-radius: var(--radius-sm);
          padding: 0.45rem 0.65rem;
          font-size: 0.78rem;
          color: var(--text-secondary);
          outline: none;
        }

        .copy-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.3rem;
          background: rgba(255, 255, 255, 0.08);
          border: 1px solid rgba(255, 255, 255, 0.12);
          color: #fff;
          padding: 0.45rem 0.85rem;
          border-radius: var(--radius-sm);
          font-size: 0.78rem;
          font-weight: 600;
          cursor: pointer;
        }

        .link-tools-row {
          display: flex;
          gap: 0.5rem;
          margin-top: 0.4rem;
        }

        .tool-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.3rem;
          padding: 0.35rem 0.65rem;
          border-radius: 6px;
          font-size: 0.75rem;
          font-weight: 600;
          cursor: pointer;
        }

        .rotate-btn {
          background: rgba(99, 102, 241, 0.12);
          color: #818cf8;
          border: 1px solid rgba(99, 102, 241, 0.25);
        }

        .revoke-btn {
          background: rgba(239, 68, 68, 0.12);
          color: #f87171;
          border: 1px solid rgba(239, 68, 68, 0.25);
        }

        .access-modal-footer {
          padding: 0.85rem 1.5rem;
          border-top: 1px solid rgba(255, 255, 255, 0.08);
          text-align: right;
          background: rgba(0, 0, 0, 0.15);
        }

        .close-footer-btn {
          background: rgba(255, 255, 255, 0.1);
          border: 1px solid rgba(255, 255, 255, 0.15);
          color: var(--text-primary);
          padding: 0.45rem 1.25rem;
          border-radius: var(--radius-sm);
          font-size: 0.85rem;
          font-weight: 600;
          cursor: pointer;
        }
      `}</style>
    </div>
  );
};
