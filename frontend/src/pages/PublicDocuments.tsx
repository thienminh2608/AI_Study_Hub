import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  Bookmark,
  Download,
  Eye,
  FileCode2,
  Files,
  FileSpreadsheet,
  FileText,
  FileType2,
  Globe2,
  Loader,
  Presentation,
  Search,
  X,
} from 'lucide-react';
import { api } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { FileTypeIcon } from '../components/FileTypeIcon';

interface PublicDocument {
  documentId: number;
  userId: number;
  title: string;
  fileExtension: string;
  subject?: string;
  fileSizeMb: number;
  uploaderName: string;
  aiParsingStatus: string;
  createdAt?: string;
  bookmarkCount?: number;
  downloadCount?: number;
  viewCount?: number;
  fileAvailable?: boolean;
}

const FILE_CATEGORIES = [
  { id: 'ALL', label: 'Tất cả', extensions: [] as string[], icon: Files, color: '#9d4edd' },
  { id: 'PDF', label: 'PDF', extensions: ['pdf'], icon: FileText, color: '#ef4444' },
  { id: 'WORD', label: 'Word', extensions: ['doc', 'docx'], icon: FileType2, color: '#3b82f6' },
  {
    id: 'SHEET',
    label: 'Bảng tính',
    extensions: ['xls', 'xlsx', 'csv'],
    icon: FileSpreadsheet,
    color: '#10b981',
  },
  {
    id: 'SLIDE',
    label: 'Trình chiếu',
    extensions: ['ppt', 'pptx'],
    icon: Presentation,
    color: '#f59e0b',
  },
  { id: 'TEXT', label: 'Văn bản', extensions: ['txt', 'md'], icon: FileCode2, color: '#06b6d4' },
];

export const PublicDocuments: React.FC = () => {
  const navigate = useNavigate();
  const { user } = useAuth();
  const [documents, setDocuments] = useState<PublicDocument[]>([]);
  const [query, setQuery] = useState('');
  const [sortBy, setSortBy] = useState<'createdAt' | 'bookmarks' | 'downloads' | 'views' | 'title'>(
    'createdAt',
  );
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const [fileType, setFileType] = useState('ALL');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [bookmarkedIds, setBookmarkedIds] = useState<Set<number>>(new Set());
  const [bookmarkingId, setBookmarkingId] = useState<number | null>(null);
  const [reportDocument, setReportDocument] = useState<PublicDocument | null>(null);
  const [reportReason, setReportReason] = useState('INAPPROPRIATE');
  const [reportReasons, setReportReasons] = useState<any[]>([]);
  const [reportDetails, setReportDetails] = useState('');
  const [reportType, setReportType] = useState<'COMMUNITY' | 'COPYRIGHT'>('COMMUNITY');
  const [claimantName, setClaimantName] = useState('');
  const [claimantEmail, setClaimantEmail] = useState('');
  const [originalWorkUrl, setOriginalWorkUrl] = useState('');
  const [evidenceDescription, setEvidenceDescription] = useState('');
  const [informationConfirmed, setInformationConfirmed] = useState(false);
  const [reporting, setReporting] = useState(false);
  const [notice, setNotice] = useState('');
  const [reportValidation, setReportValidation] = useState('');
  const [downloadingId, setDownloadingId] = useState<number | null>(null);

  useEffect(() => {
    Promise.all([
      api.document.getPublicDocuments(),
      api.document.getBookmarks(),
      api.document.getReportReasons(),
    ])
      .then(([data, bookmarkIds, reasons]) => {
        setDocuments(data as PublicDocument[]);
        setBookmarkedIds(new Set(bookmarkIds));
        setReportReasons(reasons);
        if (reasons.length) setReportReason(reasons[0].reasonCode);
      })
      .catch((err: Error) => setError(err.message || 'Không thể tải tài liệu công khai.'))
      .finally(() => setLoading(false));
  }, []);

  const toggleBookmark = async (document: PublicDocument) => {
    if (bookmarkingId !== null) return;
    const isBookmarked = bookmarkedIds.has(document.documentId);
    setBookmarkingId(document.documentId);
    try {
      const result = isBookmarked
        ? await api.document.removeBookmark(document.documentId)
        : await api.document.addBookmark(document.documentId);
      setBookmarkedIds((current) => {
        const next = new Set(current);
        if (result.isBookmarked) next.add(document.documentId);
        else next.delete(document.documentId);
        return next;
      });
      setDocuments((current) =>
        current.map((item) =>
          item.documentId === document.documentId
            ? { ...item, bookmarkCount: result.bookmarkCount }
            : item,
        ),
      );
    } catch (err: any) {
      setError(err.message || 'Không thể cập nhật bookmark.');
    } finally {
      setBookmarkingId(null);
    }
  };

  const submitReport = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!reportDocument || reporting) return;
    if (!reportDetails.trim()) {
      setReportValidation('Vui lòng nhập mô tả chi tiết nội dung vi phạm.');
      return;
    }
    setReporting(true);
    try {
      await api.document.report({
        documentId: reportDocument.documentId,
        reasonCode: reportReason,
        additionalDetails: reportDetails.trim(),
        reportType,
        claimantName,
        claimantEmail,
        originalWorkUrl,
        evidenceDescription,
        informationConfirmed,
      });
      setNotice(`Đã gửi báo cáo cho tài liệu “${reportDocument.title}”.`);
      setReportDocument(null);
      setReportReason('INAPPROPRIATE');
      setReportDetails('');
      setReportType('COMMUNITY');
      setClaimantName('');
      setClaimantEmail('');
      setOriginalWorkUrl('');
      setEvidenceDescription('');
      setInformationConfirmed(false);
    } catch (err: any) {
      setError(err.message || 'Không thể gửi báo cáo.');
    } finally {
      setReporting(false);
    }
  };

  const downloadDocument = async (document: PublicDocument) => {
    if (downloadingId !== null) return;
    setDownloadingId(document.documentId);
    setError('');
    try {
      const blob = await api.document.download(document.documentId);
      const url = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = url;
      anchor.download = `${document.title}.${document.fileExtension}`;
      window.document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      setDocuments((current) =>
        current.map((item) =>
          item.documentId === document.documentId
            ? { ...item, downloadCount: (item.downloadCount ?? 0) + 1 }
            : item,
        ),
      );
    } catch (err: any) {
      setError(err.message || 'Không thể tải tài liệu.');
    } finally {
      setDownloadingId(null);
    }
  };

  const filteredDocuments = useMemo(() => {
    const keyword = query.trim().toLocaleLowerCase('vi');
    const selectedCategory =
      FILE_CATEGORIES.find((category) => category.id === fileType) ?? FILE_CATEGORIES[0];
    const filtered = documents.filter(
      (document) =>
        (fileType === 'ALL' ||
          selectedCategory.extensions.includes(document.fileExtension.toLowerCase())) &&
        (!keyword ||
          document.title.toLocaleLowerCase('vi').includes(keyword) ||
          document.uploaderName.toLocaleLowerCase('vi').includes(keyword) ||
          document.fileExtension.toLocaleLowerCase('vi').includes(keyword)),
    );
    return [...filtered].sort((left, right) => {
      let value = 0;
      if (sortBy === 'bookmarks') value = (left.bookmarkCount ?? 0) - (right.bookmarkCount ?? 0);
      else if (sortBy === 'downloads')
        value = (left.downloadCount ?? 0) - (right.downloadCount ?? 0);
      else if (sortBy === 'views') value = (left.viewCount ?? 0) - (right.viewCount ?? 0);
      else if (sortBy === 'title') value = left.title.localeCompare(right.title, 'vi');
      else
        value = new Date(left.createdAt ?? 0).getTime() - new Date(right.createdAt ?? 0).getTime();
      return sortDirection === 'asc' ? value : -value;
    });
  }, [documents, query, fileType, sortBy, sortDirection]);

  return (
    <div className="public-documents-page">
      <header className="public-documents-header">
        <div>
          <div className="eyebrow">
            <Globe2 size={18} /> Thư viện cộng đồng
          </div>
          <h1>Tài liệu công khai</h1>
          <p>Khám phá tài liệu học tập được chia sẻ bởi cộng đồng AI Study Hub.</p>
        </div>
        <div className="public-toolbar">
          <label className="public-search">
            <Search size={18} />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Tìm theo tên, người đăng hoặc định dạng..."
            />
          </label>
          <select
            className="public-sort"
            value={sortBy}
            onChange={(event) => setSortBy(event.target.value as typeof sortBy)}
            aria-label="Tiêu chí sắp xếp"
          >
            <option value="createdAt">Ngày đăng</option>
            <option value="title">Tên</option>
            <option value="downloads">Lượt tải</option>
            <option value="views">Lượt xem</option>
            <option value="bookmarks">Lượt lưu</option>
          </select>
          <select
            className="public-sort"
            value={sortDirection}
            onChange={(event) => setSortDirection(event.target.value as typeof sortDirection)}
            aria-label="Chiều sắp xếp"
          >
            <option value="asc">Tăng dần</option>
            <option value="desc">Giảm dần</option>
          </select>
        </div>
      </header>

      <div className="file-category-bar" aria-label="Danh mục loại tài liệu">
        {FILE_CATEGORIES.map((category) => {
          const Icon = category.icon;
          const count =
            category.id === 'ALL'
              ? documents.length
              : documents.filter((document) =>
                  category.extensions.includes(document.fileExtension.toLowerCase()),
                ).length;
          return (
            <button
              key={category.id}
              type="button"
              className={fileType === category.id ? 'active' : ''}
              onClick={() => setFileType(category.id)}
              aria-pressed={fileType === category.id}
            >
              <span
                className="category-icon"
                style={{ color: category.color, backgroundColor: `${category.color}18` }}
              >
                <Icon size={22} />
              </span>
              <span>
                <strong>{category.label}</strong>
                <small>{count} tài liệu</small>
              </span>
            </button>
          );
        })}
      </div>

      {notice && <div className="public-notice">{notice}</div>}

      {loading ? (
        <div className="public-state glass-panel">
          <Loader className="spin" /> Đang tải tài liệu...
        </div>
      ) : error ? (
        <div className="public-state error-alert">{error}</div>
      ) : filteredDocuments.length === 0 ? (
        <div className="public-state glass-panel">
          <Globe2 size={40} />
          <strong>
            {query ? 'Không tìm thấy tài liệu phù hợp' : 'Chưa có tài liệu công khai'}
          </strong>
        </div>
      ) : (
        <>
          <div className="public-result-count">{filteredDocuments.length} tài liệu</div>
          <div className="public-document-grid">
            {filteredDocuments.map((document) => (
              <article
                key={document.documentId}
                className="public-document-card glass-card"
                onClick={() => navigate(`/document/${document.documentId}`)}
              >
                <FileTypeIcon
                  extension={document.fileExtension}
                  size={30}
                  className="public-file-icon"
                />
                <div className="public-card-body">
                  <h3>
                    {document.title}.{document.fileExtension}
                  </h3>
                  <p>
                    Đăng bởi <strong>{document.uploaderName}</strong>
                  </p>
                  <div className="public-card-meta">
                    <span>{document.subject || 'Khác'}</span>
                    <span>{document.fileSizeMb.toFixed(2)} MB</span>
                    <span>{document.aiParsingStatus}</span>
                    <span>
                      {document.createdAt
                        ? new Date(document.createdAt).toLocaleDateString('vi-VN')
                        : 'N/A'}
                    </span>
                  </div>
                  <div className="public-card-stats">
                    <button
                      className={`public-stat-action ${bookmarkedIds.has(document.documentId) ? 'active' : ''}`}
                      onClick={(event) => {
                        event.stopPropagation();
                        toggleBookmark(document);
                      }}
                      disabled={bookmarkingId === document.documentId}
                      title={bookmarkedIds.has(document.documentId) ? 'Bỏ lưu' : 'Lưu tài liệu'}
                    >
                      {bookmarkingId === document.documentId ? (
                        <Loader className="spin" size={15} />
                      ) : (
                        <Bookmark
                          size={15}
                          fill={bookmarkedIds.has(document.documentId) ? 'currentColor' : 'none'}
                        />
                      )}
                      {document.bookmarkCount ?? 0} lượt lưu
                    </button>
                    <button
                      className="public-stat-action download"
                      onClick={(event) => {
                        event.stopPropagation();
                        downloadDocument(document);
                      }}
                      disabled={
                        downloadingId === document.documentId || document.fileAvailable === false
                      }
                      title={
                        document.fileAvailable === false
                          ? 'File gốc của dữ liệu mẫu không tồn tại'
                          : 'Tải tài liệu'
                      }
                    >
                      {downloadingId === document.documentId ? (
                        <Loader className="spin" size={15} />
                      ) : (
                        <Download size={15} />
                      )}
                      {document.fileAvailable === false
                        ? 'Thiếu file gốc'
                        : `${document.downloadCount ?? 0} lượt tải`}
                    </button>
                    <span>
                      <Eye size={15} /> {document.viewCount ?? 0} lượt xem
                    </span>
                    {document.userId !== user?.userId && (
                      <button
                        className="public-stat-action report"
                        onClick={(event) => {
                          event.stopPropagation();
                          setError('');
                          setNotice('');
                          setReportDocument(document);
                        }}
                      >
                        <AlertTriangle size={15} /> Báo cáo
                      </button>
                    )}
                  </div>
                </div>
              </article>
            ))}
          </div>
        </>
      )}

      {reportDocument && (
        <div
          className="public-report-overlay"
          onMouseDown={() => !reporting && setReportDocument(null)}
        >
          <div
            className="public-report-modal glass-panel animate-slide-up"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <button
              className="public-report-close"
              onClick={() => setReportDocument(null)}
              disabled={reporting}
              aria-label="Đóng"
            >
              <X size={20} />
            </button>
            <h3>Báo cáo tài liệu</h3>
            <p>
              {reportDocument.title}.{reportDocument.fileExtension}
            </p>
            <form onSubmit={submitReport}>
              <label>
                Loại báo cáo
                <select
                  value={reportType}
                  onChange={(event) => setReportType(event.target.value as any)}
                  className="input-control"
                >
                  <option value="COMMUNITY">Vi phạm cộng đồng</option>
                  <option value="COPYRIGHT">Khiếu nại bản quyền</option>
                </select>
              </label>
              <label>
                Lý do báo cáo
                <select
                  value={reportReason}
                  onChange={(event) => setReportReason(event.target.value)}
                  className="input-control"
                  required
                >
                  {reportReasons.map((reason) => (
                    <option key={reason.reasonCode} value={reason.reasonCode}>
                      {reason.description}
                    </option>
                  ))}
                </select>
              </label>
              {reportType === 'COPYRIGHT' && (
                <div className="copyright-fields">
                  <label>
                    Họ tên người khiếu nại
                    <input
                      className="input-control"
                      value={claimantName}
                      onChange={(e) => setClaimantName(e.target.value)}
                      required
                    />
                  </label>
                  <label>
                    Email liên hệ
                    <input
                      type="email"
                      className="input-control"
                      value={claimantEmail}
                      onChange={(e) => setClaimantEmail(e.target.value)}
                      required
                    />
                  </label>
                  <label>
                    URL tác phẩm gốc
                    <input
                      type="url"
                      className="input-control"
                      value={originalWorkUrl}
                      onChange={(e) => setOriginalWorkUrl(e.target.value)}
                      placeholder="https://..."
                    />
                  </label>
                  <label>
                    Mô tả bằng chứng
                    <textarea
                      className="input-control"
                      rows={3}
                      value={evidenceDescription}
                      onChange={(e) => setEvidenceDescription(e.target.value)}
                      required
                    />
                  </label>
                  <label className="copyright-confirm">
                    <input
                      type="checkbox"
                      checked={informationConfirmed}
                      onChange={(e) => setInformationConfirmed(e.target.checked)}
                      required
                    />{' '}
                    Tôi xác nhận thông tin trên được cung cấp trung thực.
                  </label>
                </div>
              )}
              <label>
                Mô tả chi tiết
                <textarea
                  value={reportDetails}
                  onChange={(event) => {
                    setReportDetails(event.target.value);
                    if (event.target.value.trim()) setReportValidation('');
                  }}
                  className="input-control"
                  rows={4}
                  placeholder="Cho chúng tôi biết vấn đề của tài liệu..."
                  required
                />
                {reportValidation && (
                  <span role="alert" style={{ color: '#f87171', fontSize: '.82rem' }}>
                    {reportValidation}
                  </span>
                )}
              </label>
              <div className="public-report-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setReportDocument(null)}
                  disabled={reporting}
                >
                  Hủy
                </button>
                <button type="submit" className="btn-primary" disabled={reporting || !reportReason}>
                  {reporting ? <Loader className="spin" size={16} /> : 'Gửi báo cáo'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .public-documents-page { min-width:0; display: flex; flex-direction: column; gap: 1.5rem; }
        .public-documents-header { min-width:0; display: flex; align-items: flex-end; justify-content: space-between; gap: 2rem; }
        .public-documents-header h1 { margin: .35rem 0; font-size: 2rem; }
        .public-documents-header p { color: var(--text-secondary); }
        .eyebrow { display: flex; align-items: center; gap: .45rem; color: var(--accent-blue); font-weight: 700; }
        .public-search { min-width: 360px; display: flex; align-items: center; gap: .6rem; padding: .8rem 1rem; border: 1px solid rgba(255,255,255,.1); border-radius: var(--radius-sm); background: rgba(255,255,255,.04); }
        .public-search input { width: 100%; border: 0; outline: 0; background: transparent; color: var(--text-primary); }
        .public-toolbar { min-width:0;display:flex;flex-wrap:wrap;gap:.65rem;align-items:center; }
        .public-sort { padding:.8rem; border:1px solid rgba(255,255,255,.1); border-radius:var(--radius-sm); background:var(--bg-secondary); color:var(--text-primary); }
        .file-category-bar { display:grid;grid-template-columns:repeat(6,minmax(120px,1fr));gap:.7rem;margin-top:-.5rem; }
        .file-category-bar button { min-width:0;display:flex;align-items:center;gap:.65rem;padding:.75rem;border:1px solid rgba(255,255,255,.08);border-radius:var(--radius-sm);background:rgba(255,255,255,.03);color:var(--text-primary);cursor:pointer;text-align:left;transition:var(--transition-fast); }
        .file-category-bar button:hover { transform:translateY(-2px);border-color:rgba(255,255,255,.18); }
        .file-category-bar button.active { border-color:var(--accent-purple);background:rgba(157,78,221,.1);box-shadow:0 0 0 2px rgba(157,78,221,.08); }
        .category-icon { width:38px;height:38px;display:grid;place-items:center;border-radius:10px;flex:0 0 auto; }
        .file-category-bar button>span:last-child { min-width:0;display:flex;flex-direction:column; }
        .file-category-bar strong { white-space:nowrap;overflow:hidden;text-overflow:ellipsis; }
        .file-category-bar small { color:var(--text-muted);font-size:.72rem;margin-top:.12rem; }
        .public-result-count { color: var(--text-muted); font-size: .9rem; }
        .public-document-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
        .public-document-card { padding: 1.25rem; display: flex; flex-direction: column; gap: 1rem; cursor: pointer; transition: var(--transition-fast); }
        .public-document-card:hover { transform: translateY(-2px); border-color: rgba(255,255,255,.15); }
        .public-file-icon { width: 52px; height: 52px; display: grid; place-items: center; border-radius: 14px; color: var(--accent-blue); background: rgba(0,180,216,.1); }
        .public-card-body { flex: 1; min-width: 0; }
        .public-card-body h3 { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-bottom: .45rem; }
        .public-card-body p { color: var(--text-secondary); font-size: .9rem; }
        .public-card-meta { display: flex; flex-wrap: wrap; gap: .45rem; margin-top: .9rem; }
        .public-card-meta span { padding: .25rem .5rem; border-radius: 999px; color: var(--text-muted); background: rgba(255,255,255,.05); font-size: .75rem; }
        .public-card-stats { display: flex; flex-wrap: wrap; align-items: center; gap: .9rem; margin-top: 1rem; color: var(--text-secondary); font-size: .82rem; }
        .public-card-stats span, .public-stat-action { display: inline-flex; align-items: center; gap: .35rem; }
        .public-stat-action { padding: 0; border: 0; background: transparent; color: var(--text-secondary); font: inherit; cursor: pointer; }
        .public-stat-action:hover, .public-stat-action.active { color: var(--warning); }
        .public-stat-action.download:hover { color: var(--accent-blue); }
        .public-stat-action.report:hover { color: var(--danger); }
        .public-notice { padding: .75rem 1rem; border: 1px solid rgba(16,185,129,.22); border-radius: var(--radius-sm); background: rgba(16,185,129,.09); color: var(--success); }
        .public-report-overlay { position: fixed; inset: 0; z-index: 1000; display: grid; place-items: center; padding: 1rem; background: rgba(3,7,18,.78); backdrop-filter: blur(8px); }
        .public-report-modal { position: relative; width: min(500px, 100%); padding: 2rem; border-radius: var(--radius-md); }
        .public-report-modal > p { margin: .4rem 2rem 1.4rem 0; color: var(--text-secondary); }
        .public-report-modal form, .public-report-modal label { display: flex; flex-direction: column; gap: .55rem; }
        .public-report-modal form { gap: 1rem; }
        .public-report-modal textarea { resize: vertical; min-height: 105px; }
        .public-report-close { position: absolute; top: 1rem; right: 1rem; border: 0; background: transparent; color: var(--text-secondary); cursor: pointer; }
        .public-report-actions { display: flex; justify-content: flex-end; gap: .65rem; margin-top: .25rem; }
        .public-state { min-height: 260px; display: flex; flex-direction: column; justify-content: center; align-items: center; gap: .75rem; color: var(--text-secondary); }
        @media (max-width: 1200px) { .public-documents-header { align-items: stretch; flex-direction: column; gap:1rem; } .public-toolbar{align-items:stretch}.public-search{min-width:0;flex:1 1 100%}.file-category-bar{grid-template-columns:repeat(3,1fr)} }
        @media (max-width: 800px) { .public-toolbar{flex-direction:column}.public-search { min-width: 0; } }
        @media (max-width: 520px) { .file-category-bar{grid-template-columns:repeat(2,1fr)} }
      `}</style>
    </div>
  );
};
