import React, { useEffect, useState, useCallback, useRef } from 'react';
import { api, type DocumentVersion } from '../services/api';
import { useUiFeedback } from '../context/UiFeedbackContext';
import { X, Upload, History, RotateCcw, FileText, Loader, Trash2 } from 'lucide-react';
import { formatDateTime } from '../utils/dateTime';

interface DocumentVersionHistoryModalProps {
  documentId: number;
  isOpen: boolean;
  onClose: () => void;
  onVersionChanged?: () => void;
}

export const DocumentVersionHistoryModal: React.FC<DocumentVersionHistoryModalProps> = ({
  documentId,
  isOpen,
  onClose,
  onVersionChanged,
}) => {
  const { confirm, notify } = useUiFeedback();
  const [versions, setVersions] = useState<DocumentVersion[]>([]);
  const [loading, setLoading] = useState(false);
  const [fileInput, setFileInput] = useState<File | null>(null);
  const [summaryInput, setSummaryInput] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploadPercent, setUploadPercent] = useState(0);
  const abortControllerRef = useRef<AbortController | null>(null);

  const fetchHistory = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.versions.getVersionHistory(documentId);
      setVersions(data);
    } catch (err: any) {
      notify(err.message || 'Không thể tải lịch sử phiên bản.', 'error');
    } finally {
      setLoading(false);
    }
  }, [documentId, notify]);

  useEffect(() => {
    if (isOpen) {
      fetchHistory();
    }
  }, [isOpen, fetchHistory]);

  if (!isOpen) return null;

  const handleUploadNewVersion = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!fileInput) return;

    setUploading(true);
    setUploadPercent(0);
    const controller = new AbortController();
    abortControllerRef.current = controller;

    try {
      await api.versions.uploadNewVersion(
        documentId,
        fileInput,
        summaryInput,
        (pct) => setUploadPercent(pct),
        controller.signal,
      );
      notify('Tải lên phiên bản mới thành công!', 'success');
      setFileInput(null);
      setSummaryInput('');
      await fetchHistory();
      if (onVersionChanged) onVersionChanged();
    } catch (err: any) {
      if (controller.signal.aborted) {
        notify('Đã hủy tải lên phiên bản mới.', 'info');
      } else {
        notify(err.message || 'Lỗi tải phiên bản mới.', 'error');
      }
    } finally {
      setUploading(false);
      abortControllerRef.current = null;
    }
  };

  const handleRestoreVersion = async (versionId: number) => {
    if (
      !(await confirm({
        title: 'Khôi phục phiên bản',
        message: 'Bạn có chắc chắn muốn khôi phục về phiên bản này không?',
        confirmLabel: 'Khôi phục',
      }))
    )
      return;

    try {
      await api.versions.restoreVersion(documentId, versionId);
      notify('Đã khôi phục phiên bản thành công!', 'success');
      await fetchHistory();
      if (onVersionChanged) onVersionChanged();
    } catch (err: any) {
      notify(err.message || 'Lỗi khôi phục phiên bản.', 'error');
    }
  };

  const handleDeleteVersion = async (versionId: number, versionNumber: number) => {
    if (
      !(await confirm({
        title: `Xóa phiên bản v${versionNumber}`,
        message: `Bạn có chắc chắn muốn xóa vĩnh viễn phiên bản v${versionNumber} này không?`,
        confirmLabel: 'Xóa phiên bản',
      }))
    )
      return;

    try {
      await api.versions.deleteVersion(documentId, versionId);
      notify(`Đã xóa phiên bản v${versionNumber} thành công!`, 'success');
      await fetchHistory();
      if (onVersionChanged) onVersionChanged();
    } catch (err: any) {
      notify(err.message || 'Lỗi xóa phiên bản.', 'error');
    }
  };

  return (
    <div className="version-modal-overlay" onClick={onClose}>
      <div className="version-modal-card glass-panel animate-slide-up" onClick={(e) => e.stopPropagation()}>
        <div className="version-modal-header">
          <div className="title-box">
            <History size={20} className="header-icon" />
            <h2>Lịch sử phiên bản (Versioning)</h2>
          </div>
          <button onClick={onClose} className="close-btn">
            <X size={18} />
          </button>
        </div>

        <div className="version-modal-body">
          {/* Upload new version form */}
          <form onSubmit={handleUploadNewVersion} className="upload-version-card">
            <h3 className="card-heading">
              <Upload size={16} />
              <span>Tải đè phiên bản mới</span>
            </h3>
            <input
              type="file"
              onChange={(e) => setFileInput(e.target.files?.[0] || null)}
              className="version-file-input"
            />
            <input
              type="text"
              placeholder="Ghi chú thay đổi (ví dụ: Sửa nội dung chương 2...)"
              value={summaryInput}
              onChange={(e) => setSummaryInput(e.target.value)}
              className="version-text-input"
            />
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
              <button
                type="submit"
                disabled={!fileInput || uploading}
                className="submit-version-btn"
                style={{ flex: 1 }}
              >
                {uploading ? `Đang tải lên (${uploadPercent}%)...` : 'Tải lên phiên bản mới'}
              </button>
              {uploading && uploadPercent < 100 && (
                <button
                  type="button"
                  onClick={() => abortControllerRef.current?.abort()}
                  style={{
                    padding: '8px 12px',
                    borderRadius: '6px',
                    border: '1px solid rgba(239, 68, 68, 0.4)',
                    background: 'rgba(239, 68, 68, 0.15)',
                    color: '#fca5a5',
                    fontSize: '12px',
                    cursor: 'pointer',
                  }}
                >
                  Hủy
                </button>
              )}
            </div>
          </form>

          {/* History timeline list */}
          <div className="history-section">
            <h3 className="section-label">Các phiên bản trước đó</h3>
            {loading ? (
              <div className="version-loading">
                <Loader className="spin" size={24} />
                <span>Đang tải lịch sử...</span>
              </div>
            ) : (
              <div className="versions-list custom-scroll">
                {versions.map((v) => (
                  <div
                    key={v.versionId}
                    className={`version-item ${v.isCurrent ? 'current' : ''}`}
                  >
                    <div className="version-info">
                      <FileText size={20} className="file-icon" />
                      <div className="version-meta">
                        <div className="version-title-row">
                          <span className="version-num">v{v.versionNumber}</span>
                          {v.isCurrent && <span className="current-badge">Hiện tại</span>}
                        </div>
                        <p className="version-summary">{v.changeSummary || 'Không có ghi chú'}</p>
                        <p className="version-details">
                          Tạo bởi {v.createdByName} • {formatDateTime(v.createdAt)} • {v.fileSizeMb} MB
                        </p>
                      </div>
                    </div>

                    {!v.isCurrent && (
                      <div className="version-item-actions">
                        <button
                          onClick={() => handleRestoreVersion(v.versionId)}
                          className="restore-version-btn"
                          title="Khôi phục phiên bản này"
                        >
                          <RotateCcw size={13} />
                          <span>Khôi phục</span>
                        </button>
                        <button
                          onClick={() => handleDeleteVersion(v.versionId, v.versionNumber)}
                          className="delete-version-btn"
                          title="Xóa phiên bản này"
                        >
                          <Trash2 size={13} />
                          <span>Xóa</span>
                        </button>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="version-modal-footer">
          <button onClick={onClose} className="close-footer-btn">
            Đóng
          </button>
        </div>
      </div>

      <style>{`
        .version-modal-overlay {
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

        .version-modal-card {
          width: 100%;
          max-width: 540px;
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

        .version-modal-header {
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

        .version-modal-header h2 {
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
        }

        .close-btn:hover {
          color: var(--text-primary);
          background: rgba(255, 255, 255, 0.1);
        }

        .version-modal-body {
          padding: 1.25rem 1.5rem;
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .upload-version-card {
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
          background: rgba(157, 78, 221, 0.08);
          border: 1px solid rgba(157, 78, 221, 0.2);
          padding: 1rem;
          border-radius: var(--radius-md);
        }

        .card-heading {
          display: flex;
          align-items: center;
          gap: 0.4rem;
          font-size: 0.88rem;
          font-weight: 700;
          color: var(--accent-purple);
        }

        .version-file-input {
          font-size: 0.8rem;
          color: var(--text-muted);
        }

        .version-text-input {
          background: rgba(0, 0, 0, 0.2);
          border: 1px solid rgba(255, 255, 255, 0.1);
          border-radius: var(--radius-sm);
          padding: 0.5rem 0.75rem;
          color: var(--text-primary);
          font-size: 0.82rem;
          outline: none;
        }

        .submit-version-btn {
          background: var(--accent-purple);
          color: #fff;
          border: none;
          border-radius: var(--radius-sm);
          padding: 0.5rem;
          font-size: 0.85rem;
          font-weight: 600;
          cursor: pointer;
        }

        .submit-version-btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .history-section {
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

        .version-loading {
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 0.5rem;
          padding: 2rem;
          color: var(--text-muted);
        }

        .versions-list {
          max-height: 220px;
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
        }

        .version-item {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 0.75rem;
          border-radius: var(--radius-md);
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid rgba(255, 255, 255, 0.06);
        }

        .version-item.current {
          background: rgba(157, 78, 221, 0.12);
          border-color: rgba(157, 78, 221, 0.3);
        }

        .version-info {
          display: flex;
          align-items: flex-start;
          gap: 0.65rem;
        }

        .file-icon {
          color: var(--accent-purple);
          margin-top: 0.1rem;
        }

        .version-meta {
          display: flex;
          flex-direction: column;
          gap: 0.15rem;
        }

        .version-title-row {
          display: flex;
          align-items: center;
          gap: 0.5rem;
        }

        .version-num {
          font-weight: 700;
          font-size: 0.9rem;
          color: var(--text-primary);
        }

        .current-badge {
          font-size: 0.7rem;
          font-weight: 700;
          background: rgba(157, 78, 221, 0.25);
          color: var(--accent-purple);
          padding: 0.1rem 0.45rem;
          border-radius: 4px;
        }

        .version-summary {
          font-size: 0.8rem;
          color: var(--text-secondary);
        }

        .version-details {
          font-size: 0.72rem;
          color: var(--text-muted);
        }

        .version-item-actions {
          display: flex;
          align-items: center;
          gap: 0.4rem;
        }

        .restore-version-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.3rem;
          background: rgba(255, 255, 255, 0.08);
          border: 1px solid rgba(255, 255, 255, 0.12);
          color: var(--text-primary);
          padding: 0.35rem 0.65rem;
          border-radius: 6px;
          font-size: 0.78rem;
          font-weight: 600;
          cursor: pointer;
          transition: all 0.2s;
        }

        .restore-version-btn:hover {
          background: rgba(255, 255, 255, 0.15);
          color: #fff;
        }

        .delete-version-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.3rem;
          background: rgba(239, 68, 68, 0.12);
          border: 1px solid rgba(239, 68, 68, 0.3);
          color: #fca5a5;
          padding: 0.35rem 0.65rem;
          border-radius: 6px;
          font-size: 0.78rem;
          font-weight: 600;
          cursor: pointer;
          transition: all 0.2s;
        }

        .delete-version-btn:hover {
          background: rgba(239, 68, 68, 0.25);
          color: #fff;
          border-color: #ef4444;
        }

        .version-modal-footer {
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
