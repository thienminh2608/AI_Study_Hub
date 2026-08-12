import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { api } from '../services/api';
import { 
  FileText, 
  ArrowLeft, 
  Trash2, 
  Share2, 
  AlertOctagon, 
  Loader, 
  Lock,
  Download
} from 'lucide-react';

interface DocumentDetails {
  documentId: number;
  userId: number;
  uploaderName: string;
  folderId?: number;
  title: string;
  fileExtension: string;
  cloudStorageUrl: string;
  fileSizeMb: number;
  aiParsingStatus: string;
  sharingPermission: string;
  shareLinkToken?: string;
  createdAt?: string;
}

export const DocumentViewer: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const documentId = parseInt(id || '0');

  const [doc, setDoc] = useState<DocumentDetails | null>(null);
  const [extractedText, setExtractedText] = useState('');
  const [loading, setLoading] = useState(true);
  
  // Edit & Share
  const [sharingPermission, setSharingPermission] = useState('PRIVATE');
  const [updatingPermission, setUpdatingPermission] = useState(false);

  // Report Modal
  const [showReportModal, setShowReportModal] = useState(false);
  const [reportReason, setReportReason] = useState('INAPPROPRIATE');
  const [reportDetails, setReportDetails] = useState('');
  const [reporting, setReporting] = useState(false);

  const loadDocumentDetails = async () => {
    if (!documentId) return;
    setLoading(true);
    try {
      // 1. Get metadata
      const details = await api.document.getById(documentId);
      setDoc(details);
      setSharingPermission(details.sharingPermission);

      // 2. Get text
      const textData = await api.document.getText(documentId);
      setExtractedText(textData.extractedText);
    } catch (err: any) {
      alert(err.message || 'Lỗi khi tải chi tiết tài liệu.');
      navigate('/');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadDocumentDetails();
  }, [documentId]);

  const handleToggleSharing = async () => {
    if (!doc) return;
    const nextPermission = sharingPermission === 'PUBLIC' ? 'PRIVATE' : 'PUBLIC';
    
    setUpdatingPermission(true);
    try {
      await api.document.confirm(doc.documentId, doc.title, nextPermission, doc.folderId);
      setSharingPermission(nextPermission);
    } catch (err: any) {
      alert(err.message || 'Cập nhật quyền chia sẻ thất bại.');
    } finally {
      setUpdatingPermission(false);
    }
  };

  const handleDelete = async () => {
    if (!doc || !window.confirm('Bạn có chắc chắn muốn xóa tài liệu này vĩnh viễn?')) return;

    try {
      await api.document.delete(doc.documentId);
      navigate('/');
    } catch (err: any) {
      alert(err.message || 'Không thể xóa tài liệu.');
    }
  };

  const handleReport = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!doc || reporting) return;

    setReporting(true);
    try {
      await api.document.report({
        documentId: doc.documentId,
        reasonCode: reportReason,
        additionalDetails: reportDetails.trim()
      });
      alert('Đã gửi báo cáo vi phạm tài liệu thành công. Cảm ơn phản hồi của bạn!');
      setShowReportModal(false);
      setReportDetails('');
    } catch (err: any) {
      alert(err.message || 'Gửi báo cáo thất bại.');
    } finally {
      setReporting(false);
    }
  };

  // Build full download URL
  const downloadUrl = doc ? `http://localhost:5065${doc.cloudStorageUrl}` : '#';

  return (
    <div className="viewer-container">
      
      {/* Header operations row */}
      <div className="viewer-header">
        <button onClick={() => navigate(-1)} className="btn-secondary back-btn">
          <ArrowLeft size={16} />
          <span>Quay lại</span>
        </button>

        {doc && (
          <div className="operations-group">
            {/* Share toggle */}
            <button 
              onClick={handleToggleSharing} 
              className={`btn-secondary ${sharingPermission === 'PUBLIC' ? 'shared-active' : ''}`}
              disabled={updatingPermission}
            >
              {updatingPermission ? <Loader className="spin" size={16} /> : (
                sharingPermission === 'PUBLIC' ? <Share2 size={16} /> : <Lock size={16} />
              )}
              <span>{sharingPermission === 'PUBLIC' ? 'Đang chia sẻ công khai' : 'Riêng tư'}</span>
            </button>

            {/* Download file */}
            <a href={downloadUrl} download target="_blank" rel="noopener noreferrer" className="btn-secondary download-link">
              <Download size={16} />
              <span>Tải file gốc</span>
            </a>

            {/* Report */}
            <button onClick={() => setShowReportModal(true)} className="btn-secondary report-btn" title="Báo cáo vi phạm">
              <AlertOctagon size={16} />
              <span>Báo cáo</span>
            </button>

            {/* Delete */}
            <button onClick={handleDelete} className="btn-secondary delete-btn" title="Xóa tài liệu">
              <Trash2 size={16} />
              <span>Xóa</span>
            </button>
          </div>
        )}
      </div>

      {loading ? (
        <div className="viewer-loader">
          <Loader className="spin" size={32} />
          <p>Đang tải nội dung tài liệu...</p>
        </div>
      ) : doc ? (
        <div className="document-sheet-layout animate-slide-up">
          
          {/* Metadata Sidebar card */}
          <div className="doc-metadata-panel glass-card">
            <FileText size={48} className="doc-large-icon" />
            <h3>{doc.title}.{doc.fileExtension}</h3>
            
            <div className="metadata-list">
              <div className="meta-row">
                <span className="label">Định dạng:</span>
                <span className="val">{doc.fileExtension.toUpperCase()}</span>
              </div>
              <div className="meta-row">
                <span className="label">Kích thước:</span>
                <span className="val">{doc.fileSizeMb.toFixed(2)} MB</span>
              </div>
              <div className="meta-row">
                <span className="label">Trạng thái AI:</span>
                <span className={`val ai-badge ${doc.aiParsingStatus}`}>
                  {doc.aiParsingStatus}
                </span>
              </div>
              <div className="meta-row">
                <span className="label">Người tải lên:</span>
                <span className="val">{doc.uploaderName}</span>
              </div>
              <div className="meta-row">
                <span className="label">Ngày đăng:</span>
                <span className="val">
                  {doc.createdAt ? new Date(doc.createdAt).toLocaleDateString() : 'N/A'}
                </span>
              </div>
            </div>

            {sharingPermission === 'PUBLIC' && doc.shareLinkToken && (
              <div className="public-link-box">
                <label>Link công khai:</label>
                <input 
                  type="text" 
                  readOnly 
                  value={`${window.location.origin}/document/public/${doc.documentId}`}
                  onClick={(e) => (e.target as HTMLInputElement).select()}
                  className="input-control copy-input"
                />
              </div>
            )}
          </div>

          {/* Main text content sheet panel */}
          <div className="text-viewer-sheet glass-panel">
            <h4>Nội dung văn bản trích xuất (AI Text)</h4>
            <div className="text-scroll-area">
              {extractedText ? (
                <pre className="extracted-text-pre">{extractedText}</pre>
              ) : doc.aiParsingStatus === 'PENDING' ? (
                <div className="empty-text-state">
                  <Loader className="spin" size={24} />
                  <p>Hệ thống AI đang tiến hành phân tích và trích xuất chữ từ tệp tin gốc. Quá trình này có thể mất vài giây...</p>
                </div>
              ) : (
                <div className="empty-text-state">
                  <AlertOctagon size={32} />
                  <p>Không có văn bản nào được trích xuất (Tệp tin trống hoặc định dạng hình ảnh/lỗi quét).</p>
                </div>
              )}
            </div>
          </div>

        </div>
      ) : null}

      {/* Modal: Report Document */}
      {showReportModal && (
        <div className="modal-overlay">
          <div className="modal-box glass-panel animate-slide-up">
            <h3>Báo cáo tài liệu vi phạm</h3>
            <form onSubmit={handleReport} className="report-form">
              <div className="form-group">
                <label>Lý do báo cáo</label>
                <select 
                  value={reportReason} 
                  onChange={(e) => setReportReason(e.target.value)}
                  className="input-control"
                >
                  <option value="INAPPROPRIATE">Nội dung không lành mạnh / Độc hại</option>
                  <option value="COPYRIGHT">Vi phạm bản quyền / Quyền tác giả</option>
                  <option value="SPAM">Quảng cáo rác / Sai chủ đề học tập</option>
                  <option value="OTHER">Lý do khác</option>
                </select>
              </div>

              <div className="form-group">
                <label>Chi tiết thêm</label>
                <textarea
                  placeholder="Mô tả cụ thể về nội dung vi phạm..."
                  value={reportDetails}
                  onChange={(e) => setReportDetails(e.target.value)}
                  className="input-control text-area"
                  rows={4}
                  required
                />
              </div>

              <div className="modal-actions">
                <button type="button" onClick={() => setShowReportModal(false)} className="btn-secondary">Hủy</button>
                <button type="submit" className="btn-primary" disabled={reporting}>
                  {reporting ? <Loader className="spin" size={16} /> : 'Gửi báo cáo'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .viewer-container {
          min-height: 80vh;
        }

        .viewer-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 1.5rem;
        }

        .operations-group {
          display: flex;
          gap: 0.5rem;
        }

        .shared-active {
          color: var(--success) !important;
          border-color: rgba(16, 185, 129, 0.3) !important;
          background: rgba(16, 185, 129, 0.08) !important;
        }

        .download-link {
          text-decoration: none;
        }

        .report-btn:hover {
          color: var(--warning) !important;
          border-color: rgba(245, 158, 11, 0.3) !important;
          background: rgba(245, 158, 11, 0.08) !important;
        }

        .delete-btn:hover {
          color: var(--danger) !important;
          border-color: rgba(239, 68, 68, 0.3) !important;
          background: rgba(239, 68, 68, 0.08) !important;
        }

        .viewer-loader {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 350px;
          color: var(--text-muted);
          gap: 0.75rem;
        }

        .document-sheet-layout {
          display: grid;
          grid-template-columns: 280px 1fr;
          gap: 1.5rem;
          height: calc(100vh - 10rem);
        }

        .doc-metadata-panel {
          padding: 1.5rem;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 1rem;
          height: fit-content;
        }

        .doc-large-icon {
          color: var(--accent-blue);
          filter: drop-shadow(0 0 10px rgba(0, 180, 216, 0.2));
        }

        .doc-metadata-panel h3 {
          font-size: 1.1rem;
          text-align: center;
          word-break: break-all;
        }

        .metadata-list {
          width: 100%;
          border-top: 1px solid rgba(255, 255, 255, 0.05);
          padding-top: 1rem;
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }

        .meta-row {
          display: flex;
          justify-content: space-between;
          font-size: 0.85rem;
        }

        .meta-row .label {
          color: var(--text-muted);
          font-weight: 500;
        }

        .meta-row .val {
          color: var(--text-primary);
          font-weight: 600;
        }

        .val.ai-badge {
          font-size: 0.7rem;
          padding: 0.1rem 0.4rem;
          border-radius: 4px;
          font-weight: 700;
        }

        .val.ai-badge.READY {
          background: rgba(16, 185, 129, 0.15);
          color: var(--success);
        }

        .val.ai-badge.PENDING {
          background: rgba(245, 158, 11, 0.15);
          color: var(--warning);
        }

        .val.ai-badge.FAILED {
          background: rgba(239, 68, 68, 0.15);
          color: var(--danger);
        }

        .public-link-box {
          width: 100%;
          border-top: 1px solid rgba(255, 255, 255, 0.05);
          padding-top: 1rem;
          display: flex;
          flex-direction: column;
          gap: 0.4rem;
        }

        .public-link-box label {
          font-size: 0.8rem;
          font-weight: 600;
          color: var(--text-secondary);
        }

        .copy-input {
          font-size: 0.75rem;
          height: 32px;
          padding: 0.25rem 0.5rem;
          background: rgba(255, 255, 255, 0.02);
          cursor: pointer;
        }

        .text-viewer-sheet {
          padding: 1.75rem;
          display: flex;
          flex-direction: column;
          overflow: hidden;
        }

        .text-viewer-sheet h4 {
          font-size: 1rem;
          color: var(--text-secondary);
          text-transform: uppercase;
          letter-spacing: 0.05em;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          padding-bottom: 0.75rem;
          margin-bottom: 1rem;
        }

        .text-scroll-area {
          flex: 1;
          overflow-y: auto;
          background: rgba(0, 0, 0, 0.15);
          border: 1px solid rgba(255, 255, 255, 0.03);
          border-radius: var(--radius-sm);
          padding: 1.25rem;
        }

        .extracted-text-pre {
          white-space: pre-wrap;
          font-family: 'Consolas', monospace;
          font-size: 0.95rem;
          line-height: 1.6;
          color: #d1d5db;
        }

        .empty-text-state {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 100%;
          color: var(--text-muted);
          text-align: center;
          gap: 0.75rem;
          padding: 3rem 1rem;
        }

        .empty-text-state p {
          max-width: 380px;
          font-size: 0.9rem;
        }

        .report-form {
          display: flex;
          flex-direction: column;
          gap: 1rem;
        }

        .text-area {
          resize: vertical;
          font-family: inherit;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        @media (max-width: 768px) {
          .document-sheet-layout {
            grid-template-columns: 1fr;
            height: auto;
          }
        }
      `}</style>
    </div>
  );
};
