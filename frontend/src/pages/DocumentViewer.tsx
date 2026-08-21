import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { createPortal } from 'react-dom';
import { useParams, useNavigate, useSearchParams } from 'react-router-dom';
import { api, type SubjectTreeNode } from '../services/api';
import { useUiFeedback } from '../context/UiFeedbackContext';
import { useAuth } from '../context/AuthContext';
import { ArrowLeft, Trash2, Share2, AlertOctagon, Loader, Download, History, Pencil } from 'lucide-react';
import { FileTypeIcon } from '../components/FileTypeIcon';
import { OriginalDocumentPreview } from '../components/OriginalDocumentPreview';
import { ManageAccessModal } from '../components/ManageAccessModal';
import { DocumentVersionHistoryModal } from '../components/DocumentVersionHistoryModal';

const ORIGINAL_PREVIEW_EXTENSIONS = [
  'pdf',
  'docx',
  'xlsx',
  'pptx',
  'png',
  'jpg',
  'jpeg',
  'webp',
  'gif',
  'svg',
  'txt',
  'md',
  'csv',
  'json',
];

interface DocumentDetails {
  documentId: number;
  userId: number;
  uploaderName: string;
  folderId?: number;
  title: string;
  subject?: string;
  fileExtension: string;
  cloudStorageUrl: string;
  fileSizeMb: number;
  aiParsingStatus: string;
  sharingPermission: string;
  requestedVisibility?: string;
  moderationStatus?: string;
  moderationNote?: string;
  shareLinkToken?: string;
  createdAt?: string;
  fileAvailable?: boolean;
  requiresAppeal?: boolean;
  publicReviewBlocked?: boolean;
  appealStatus?: string;
  extractionCoveragePercent?: number | null;
}

const flattenSubjectTree = (nodes: SubjectTreeNode[]): SubjectTreeNode[] =>
  nodes.flatMap((node) => [node, ...flattenSubjectTree(node.children || [])]);

const OTHER_SUBJECT_VALUE = '__OTHER__';

interface SearchableSelectOption {
  value: string;
  label: string;
}

const SearchableSelect: React.FC<{
  value: string;
  options: SearchableSelectOption[];
  placeholder: string;
  searchPlaceholder: string;
  onChange: (value: string) => void;
}> = ({ value, options, placeholder, searchPlaceholder, onChange }) => {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const containerRef = useRef<HTMLDivElement | null>(null);
  const selected = options.find((option) => option.value === value);
  const filtered = options.filter((option) =>
    option.label.toLocaleLowerCase('vi').includes(search.trim().toLocaleLowerCase('vi')));

  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close, true);
    return () => document.removeEventListener('mousedown', close, true);
  }, [open]);

  return (
    <div className={`searchable-select ${open ? 'open' : ''}`} ref={containerRef}>
      <button type="button" className="searchable-select-trigger input-control" onClick={() => setOpen((current) => !current)}>
        <span>{selected?.label || placeholder}</span><span aria-hidden="true">⌄</span>
      </button>
      {open && (
        <div className="searchable-select-menu">
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={searchPlaceholder}
            autoFocus
          />
          <div className="searchable-select-options">
            {filtered.map((option) => (
              <button
                type="button"
                key={option.value}
                className={option.value === value ? 'active' : ''}
                onClick={() => { onChange(option.value); setSearch(''); setOpen(false); }}
              >
                {option.label}
              </button>
            ))}
            {!filtered.length && <span className="searchable-select-empty">Không có kết quả phù hợp</span>}
          </div>
        </div>
      )}
    </div>
  );
};

export const DocumentViewer: React.FC = () => {
  const { notify } = useUiFeedback();
  const { id, token } = useParams<{ id?: string; token?: string }>();
  const navigate = useNavigate();
  const { user } = useAuth();
  const [searchParams] = useSearchParams();

  // Resolve share token vs numeric documentId
  const shareToken = token || (id && isNaN(Number(id)) ? id : undefined);
  const documentId = id && !isNaN(Number(id)) ? parseInt(id, 10) : 0;

  const citationParam = searchParams.get('citation');
  const highlightStart = Number(searchParams.get('start'));
  const highlightEnd = Number(searchParams.get('end'));
  const hasHighlight = Number.isFinite(highlightStart) && Number.isFinite(highlightEnd) && highlightEnd > highlightStart;
  const highlightRef = useRef<HTMLElement | null>(null);
  const highlightPageParam = Number(searchParams.get('page'));
  const hasHighlightPage = Number.isFinite(highlightPageParam) && highlightPageParam > 0;

  const [doc, setDoc] = useState<DocumentDetails | null>(null);
  const [extractedText, setExtractedText] = useState('');
  const [loading, setLoading] = useState(true);
  const [viewMode, setViewMode] = useState<'original' | 'extracted'>('original');

  // Citation Resolution
  const [citationMetadata, setCitationMetadata] = useState<any | null>(null);
  const [resolvedHighlight, setResolvedHighlight] = useState<{ start: number; end: number } | null>(null);
  const [showCitationMismatchModal, setShowCitationMismatchModal] = useState(false);

  // Edit & Share
  const [sharingPermission, setSharingPermission] = useState('PRIVATE');
  const [showAccessModal, setShowAccessModal] = useState(false);
  const [showVersionModal, setShowVersionModal] = useState(false);
  const [showEditMetadataModal, setShowEditMetadataModal] = useState(false);
  const [subjectTree, setSubjectTree] = useState<SubjectTreeNode[]>([]);
  const [editTitle, setEditTitle] = useState('');
  const [editRootSubjectId, setEditRootSubjectId] = useState<number | null>(null);
  const [editChildSubject, setEditChildSubject] = useState('');
  const [showNewRootSubjectInput, setShowNewRootSubjectInput] = useState(false);
  const [newRootSubjectName, setNewRootSubjectName] = useState('');
  const [showNewSubjectInput, setShowNewSubjectInput] = useState(false);
  const [newSubjectName, setNewSubjectName] = useState('');
  const [savingMetadata, setSavingMetadata] = useState(false);
  const [metadataError, setMetadataError] = useState('');

  // Report Modal
  const [showReportModal, setShowReportModal] = useState(false);
  const [reportReason, setReportReason] = useState('INAPPROPRIATE');
  const [reportReasons, setReportReasons] = useState<any[]>([]);
  const [reportDetails, setReportDetails] = useState('');
  const [reporting, setReporting] = useState(false);
  const [formError, setFormError] = useState('');
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [appealForm, setAppealForm] = useState<{
    reportId: number;
    explanation: string;
    evidenceUrl: string;
  } | null>(null);
  const [publicRequestBlock, setPublicRequestBlock] = useState<string | null>(null);

  const loadDocumentDetails = useCallback(async () => {
    if (!documentId && !shareToken) return;
    setLoading(true);
    try {
      if (shareToken) {
        // Load via public share token
        const [details, textData] = await Promise.all([
          api.document.getByShareToken(shareToken),
          api.document.getTextByShareToken(shareToken).catch(() => ({ extractedText: '' })),
        ]);
        setDoc(details);
        setSharingPermission(details.sharingPermission);
        setExtractedText(textData.extractedText || '');
        if (hasHighlight) {
          setViewMode('extracted');
        } else {
          setViewMode('original');
        }
        api.document.getReportReasons().then((reasons) => {
          setReportReasons(reasons || []);
          if (reasons?.length) setReportReason(reasons[0].reasonCode);
        }).catch(() => {});
      } else {
        // Load via document ID
        const [details, reasons] = await Promise.all([
          api.document.getById(documentId),
          api.document.getReportReasons().catch(() => []),
        ]);
        setDoc(details);
        setSharingPermission(details.sharingPermission);
        setReportReasons(reasons || []);
        if (reasons?.length) setReportReason(reasons[0].reasonCode);

        let targetExtractedText = '';

        if (citationParam) {
          try {
            const citId = parseInt(citationParam, 10);
            if (citId > 0) {
              const resolved = await api.chat.resolveCitation(citId);
              setCitationMetadata(resolved);

              if (resolved.documentVersionId) {
                try {
                  const vData = await api.document.getTextByVersion(resolved.documentId, resolved.documentVersionId);
                  targetExtractedText = vData.fullText || '';
                } catch {
                  const tData = await api.document.getText(resolved.documentId).catch(() => ({ extractedText: '' }));
                  targetExtractedText = tData.extractedText || '';
                }
              } else {
                const tData = await api.document.getText(resolved.documentId).catch(() => ({ extractedText: '' }));
                targetExtractedText = tData.extractedText || '';
              }

              const s = resolved.startOffset ?? 0;
              const e = resolved.endOffset ?? 0;
              const snippet = (resolved.snippet || '').trim();

              let matched = false;
              if (e > s && e <= targetExtractedText.length) {
                const slice = targetExtractedText.slice(s, e);
                if (slice.includes(snippet.slice(0, Math.min(snippet.length, 30))) || snippet.includes(slice.slice(0, Math.min(slice.length, 30)))) {
                  setResolvedHighlight({ start: s, end: e });
                  matched = true;
                }
              }

              if (!matched && snippet) {
                const idx = targetExtractedText.indexOf(snippet);
                if (idx !== -1) {
                  setResolvedHighlight({ start: idx, end: idx + snippet.length });
                  matched = true;
                } else {
                  const shortPrefix = snippet.slice(0, Math.min(snippet.length, 60));
                  const pIdx = targetExtractedText.indexOf(shortPrefix);
                  if (pIdx !== -1) {
                    setResolvedHighlight({ start: pIdx, end: pIdx + shortPrefix.length });
                    matched = true;
                  }
                }
              }

              if (!matched) {
                setShowCitationMismatchModal(true);
              }

              setViewMode('extracted');
            }
          } catch (citErr: any) {
            notify(citErr.message || 'Không thể giải mã trích dẫn.', 'error');
            const tData = await api.document.getText(documentId).catch(() => ({ extractedText: '' }));
            targetExtractedText = tData.extractedText || '';
          }
        } else {
          const tData = await api.document.getText(documentId).catch(() => ({ extractedText: '' }));
          targetExtractedText = tData.extractedText || '';
        }

        setExtractedText(targetExtractedText);
        if (citationParam || hasHighlight) {
          setViewMode('extracted');
        } else {
          setViewMode('original');
        }
      }
    } catch (err: any) {
      notify(err.message || 'Lỗi khi tải chi tiết tài liệu.', 'error');
      if (user) {
        navigate('/');
      }
    } finally {
      setLoading(false);
    }
  }, [documentId, shareToken, citationParam, hasHighlight, notify, user, navigate]);

  // Reload when ID or share token changes
  useEffect(() => {
    loadDocumentDetails();
  }, [loadDocumentDetails]);

  const activeHighlight = useMemo(() => {
    return resolvedHighlight || (hasHighlight ? { start: highlightStart, end: highlightEnd } : null);
  }, [resolvedHighlight, hasHighlight, highlightStart, highlightEnd]);

  useEffect(() => {
    if (activeHighlight && extractedText && highlightRef.current) {
      highlightRef.current.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  }, [activeHighlight, extractedText]);

  const handleDelete = async () => {
    if (!doc) return;

    try {
      await api.document.delete(doc.documentId);
      navigate('/');
    } catch (err: any) {
      notify(err.message || 'Không thể xóa tài liệu.', 'error');
    }
  };

  const handleAppeal = async () => {
    if (!doc) return;
    const { reportId } = await api.document.getAppealableReport(doc.documentId);
    if (!reportId) {
      setFormError('Không có quyết định vi phạm có thể giải trình.');
      return;
    }
    setAppealForm({ reportId, explanation: '', evidenceUrl: '' });
    setFormError('');
  };

  const handleReport = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!doc || reporting) return;
    if (!reportDetails.trim()) {
      setFormError('Vui lòng nhập mô tả chi tiết nội dung vi phạm.');
      return;
    }

    setReporting(true);
    try {
      await api.document.report({
        documentId: doc.documentId,
        reasonCode: reportReason,
        additionalDetails: reportDetails.trim(),
      });
      notify('Đã gửi báo cáo vi phạm tài liệu thành công. Cảm ơn phản hồi của bạn!', 'success');
      setShowReportModal(false);
      setReportDetails('');
      setFormError('');
    } catch (err: any) {
      notify(err.message || 'Gửi báo cáo thất bại.', 'error');
    } finally {
      setReporting(false);
    }
  };

  // Build full download URL
  const isOwner = Boolean(doc && user?.userId === doc.userId);
  const selectedEditRoot = subjectTree.find((root) => root.subjectId === editRootSubjectId) || null;
  const availableEditChildren = selectedEditRoot
    ? flattenSubjectTree(selectedEditRoot.children || [])
    : [];

  const openMetadataEditor = async () => {
    if (!doc) return;
    setEditTitle(doc.title);
    setShowNewRootSubjectInput(false);
    setNewRootSubjectName('');
    setShowNewSubjectInput(false);
    setNewSubjectName('');
    setMetadataError('');
    setShowEditMetadataModal(true);
    try {
      const tree = subjectTree.length ? subjectTree : await api.subjects.getTree('APPROVED');
      if (!subjectTree.length) setSubjectTree(tree);
      const currentSubject = doc.subject || 'Khác';
      const matchingRoot = tree.find((root) => root.name === currentSubject);
      const containingRoot = matchingRoot || tree.find((root) =>
        flattenSubjectTree(root.children || []).some((child) => child.name === currentSubject));
      const root = containingRoot || tree.find((item) => item.name === 'Khác') || tree[0];
      setEditRootSubjectId(root?.subjectId ?? null);
      setEditChildSubject(matchingRoot ? '' : currentSubject);
    } catch (error: any) {
      setMetadataError(error.message || 'Không thể tải cây môn học.');
    }
  };

  const handleSaveMetadata = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!doc || savingMetadata) return;
    const selectedRoot = subjectTree.find((root) => root.subjectId === editRootSubjectId);
    if (!editTitle.trim() || (!selectedRoot && !showNewRootSubjectInput)) {
      setMetadataError('Vui lòng nhập tên tài liệu và chọn môn học.');
      return;
    }
    setSavingMetadata(true);
    setMetadataError('');
    try {
      let targetSubject = editChildSubject || selectedRoot?.name || '';
      let pendingSubjectRequested: 'root' | 'child' | null = null;
      if (showNewRootSubjectInput) {
        if (newRootSubjectName.trim()) {
          const resolved = await api.subjects.resolvePath(
            newRootSubjectName.trim(),
            newSubjectName.trim() || null,
          );
          targetSubject = resolved.subject;
          pendingSubjectRequested = newSubjectName.trim() ? 'child' : 'root';
        } else {
          targetSubject = 'Khác';
        }
      } else if (showNewSubjectInput && selectedRoot) {
        if (newSubjectName.trim()) {
          const resolved = await api.subjects.resolve(newSubjectName.trim(), selectedRoot.subjectId);
          targetSubject = resolved.subject;
          pendingSubjectRequested = 'child';
        } else {
          targetSubject = selectedRoot.name;
        }
      }
      const updated = await api.document.updateMetadata(doc.documentId, editTitle.trim(), targetSubject);
      setDoc((current) => current ? { ...current, ...updated } : updated);
      setShowEditMetadataModal(false);
      notify(
        pendingSubjectRequested === 'root'
          ? 'Đã cập nhật tài liệu và gửi môn học mới tới Moderator để duyệt.'
          : pendingSubjectRequested === 'child'
            ? 'Đã cập nhật tài liệu và gửi chuyên mục mới tới Moderator để duyệt.'
          : 'Đã cập nhật tên và môn học của tài liệu.',
        'success',
      );
    } catch (error: any) {
      setMetadataError(error.message || 'Không thể cập nhật thông tin tài liệu.');
    } finally {
      setSavingMetadata(false);
    }
  };

  const handleDownload = async () => {
    if (!doc) return;
    try {
      const blob = shareToken
        ? await api.document.getRawFileByShareToken(shareToken)
        : await api.document.download(doc.documentId);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `${doc.title}.${doc.fileExtension}`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (err: any) {
      notify(err.message || 'Không thể tải file.', 'error');
    }
  };

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
            {/* Share & Access management */}
            {isOwner && (
              <button
                onClick={() => setShowAccessModal(true)}
                className={`btn-secondary ${sharingPermission === 'PUBLIC' ? 'shared-active' : ''}`}
                title="Chia sẻ & Quản lý quyền truy cập"
              >
                <Share2 size={16} />
                <span>
                  {sharingPermission === 'PUBLIC'
                    ? 'Đang công khai'
                    : doc.appealStatus === 'PENDING'
                      ? 'Chờ duyệt giải trình'
                      : doc.moderationStatus === 'PENDING_REVIEW'
                        ? 'Đang chờ duyệt'
                        : 'Chia sẻ & Quyền'}
                </span>
              </button>
            )}

            {/* Download file */}
            <button
              type="button"
              onClick={handleDownload}
              className="btn-secondary download-link"
              disabled={doc.fileAvailable === false}
              title={
                doc.fileAvailable === false
                  ? 'File gốc của dữ liệu mẫu không tồn tại'
                  : 'Tải file gốc'
              }
            >
              <Download size={16} />
              <span>{doc.fileAvailable === false ? 'Thiếu file gốc' : 'Tải file gốc'}</span>
            </button>

            {/* Version History */}
            {isOwner && (
              <button
                type="button"
                onClick={() => setShowVersionModal(true)}
                className="btn-secondary"
                title="Lịch sử phiên bản (Versioning)"
              >
                <History size={16} />
                <span>Lịch sử phiên bản</span>
              </button>
            )}

            {/* Report */}
            {!isOwner && (
              <button
                onClick={() => setShowReportModal(true)}
                className="btn-secondary report-btn"
                title="Báo cáo vi phạm"
              >
                <AlertOctagon size={16} />
                <span>Báo cáo</span>
              </button>
            )}

            {/* Delete */}
            {isOwner && (
              <button
                onClick={() => setShowDeleteModal(true)}
                className="btn-secondary delete-btn"
                title="Xóa tài liệu"
              >
                <Trash2 size={16} />
                <span>Xóa</span>
              </button>
            )}
            {isOwner && doc.requiresAppeal && (
              <button onClick={handleAppeal} className="btn-secondary">
                <AlertOctagon size={16} />
                <span>Cần gửi giải trình</span>
              </button>
            )}
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
            <FileTypeIcon extension={doc.fileExtension} size={48} className="doc-large-icon" />
            <h3>
              {doc.title}.{doc.fileExtension}
            </h3>

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
                <span className="label">Môn học:</span>
                <span className="val subject-value">{doc.subject || 'Khác'}</span>
              </div>
              <div className="meta-row">
                <span className="label">Trạng thái AI:</span>
                <span className={`val ai-badge ${doc.aiParsingStatus}`}>{doc.aiParsingStatus}</span>
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
              <div className="meta-row">
                <span className="label">Kiểm duyệt:</span>
                <span className="val">{doc.moderationStatus || 'NOT_REQUESTED'}</span>
              </div>
              {doc.moderationNote && (
                <div className="meta-row">
                  <span className="label">Phản hồi:</span>
                  <span className="val">{doc.moderationNote}</span>
                </div>
              )}
            </div>

            {sharingPermission === 'PUBLIC' && doc.shareLinkToken && (
              <div className="public-link-box">
                <label>Link công khai:</label>
                <input
                  type="text"
                  readOnly
                  value={`${window.location.origin}/document/${doc.documentId}`}
                  onClick={(e) => (e.target as HTMLInputElement).select()}
                  className="input-control copy-input"
                />
              </div>
            )}

            {isOwner && (
              <button type="button" className="edit-metadata-button" onClick={openMetadataEditor}>
                <Pencil size={16} />
                Chỉnh sửa thông tin
              </button>
            )}
          </div>

          {/* Main text content sheet panel */}
          <div className="text-viewer-sheet glass-panel">
            <div className="viewer-tabs">
              <button
                type="button"
                className={viewMode === 'original' ? 'active' : ''}
                onClick={() => setViewMode('original')}
              >
                Bản gốc
              </button>
              <button
                type="button"
                className={viewMode === 'extracted' ? 'active' : ''}
                onClick={() => setViewMode('extracted')}
              >
                Văn bản trích xuất (AI)
              </button>
            </div>
            {typeof doc.extractionCoveragePercent === 'number' && doc.extractionCoveragePercent < 1 && (
              <div className="coverage-warning">
                <AlertOctagon size={16} />
                Chỉ trích xuất được khoảng {Math.round(doc.extractionCoveragePercent * 100)}% nội
                dung. Một số trang có thể chứa ảnh/nội dung quét chưa được đọc.
              </div>
            )}
            {viewMode === 'original' ? (
              ORIGINAL_PREVIEW_EXTENSIONS.includes(doc.fileExtension.toLowerCase()) ? (
                <div className="text-scroll-area original-scroll-area">
                  <OriginalDocumentPreview
                    documentId={doc.documentId}
                    fileExtension={doc.fileExtension}
                    shareToken={shareToken}
                    highlightPage={hasHighlightPage ? highlightPageParam : null}
                    onDownload={handleDownload}
                  />
                </div>
              ) : (
                <div className="text-scroll-area">
                  <div className="empty-text-state">
                    <AlertOctagon size={32} />
                    <p>
                      Chưa hỗ trợ xem trước bản gốc cho định dạng .{doc.fileExtension}. Hãy xem văn
                      bản trích xuất hoặc tải xuống tài liệu.
                    </p>
                  </div>
                </div>
              )
            ) : (
              <div className="text-scroll-area">
                {extractedText ? (
                  <pre className="extracted-text-pre">
                    {activeHighlight && activeHighlight.end <= extractedText.length ? (
                      <>
                        {extractedText.slice(0, activeHighlight.start)}
                        <mark ref={highlightRef} className="citation-highlight">
                          {extractedText.slice(activeHighlight.start, activeHighlight.end)}
                        </mark>
                        {extractedText.slice(activeHighlight.end)}
                      </>
                    ) : (
                      extractedText
                    )}
                  </pre>
                ) : doc.aiParsingStatus === 'PENDING' ? (
                  <div className="empty-text-state">
                    <Loader className="spin" size={24} />
                    <p>
                      Hệ thống AI đang tiến hành phân tích và trích xuất chữ từ tệp tin gốc. Quá
                      trình này có thể mất vài giây...
                    </p>
                  </div>
                ) : (
                  <div className="empty-text-state">
                    <AlertOctagon size={32} />
                    <p>
                      Không có văn bản nào được trích xuất (Tệp tin trống hoặc định dạng hình
                      ảnh/lỗi quét).
                    </p>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      ) : null}

      {showEditMetadataModal &&
        createPortal(
          <div className="viewport-modal-overlay" onMouseDown={() => !savingMetadata && setShowEditMetadataModal(false)}>
            <form
              className="modal-box glass-panel metadata-edit-modal animate-slide-up"
              onMouseDown={(event) => event.stopPropagation()}
              onSubmit={handleSaveMetadata}
            >
              <div className="metadata-modal-heading">
                <span><Pencil size={20} /></span>
                <div>
                  <h3>Chỉnh sửa thông tin tài liệu</h3>
                  <p>Đổi tên và phân loại tài liệu mà không làm thay đổi thư mục lưu trữ.</p>
                </div>
              </div>
              <div className="form-group">
                <label htmlFor="edit-document-title">Tên tài liệu</label>
                <div className="title-with-extension">
                  <input
                    id="edit-document-title"
                    className="input-control"
                    value={editTitle}
                    onChange={(event) => setEditTitle(event.target.value)}
                    maxLength={255}
                    autoFocus
                    required
                  />
                  <span>.{doc?.fileExtension}</span>
                </div>
              </div>
              <div className="form-group">
                <label>Môn học chính</label>
                <SearchableSelect
                  value={showNewRootSubjectInput ? OTHER_SUBJECT_VALUE : String(editRootSubjectId ?? '')}
                  placeholder="Chọn môn học"
                  searchPlaceholder="Nhập để tìm môn học..."
                  options={[
                    ...subjectTree.map((subject) => ({ value: String(subject.subjectId), label: subject.name })),
                    { value: OTHER_SUBJECT_VALUE, label: 'Thêm môn học mới' },
                  ]}
                  onChange={(value) => {
                    const isOther = value === OTHER_SUBJECT_VALUE;
                    setShowNewRootSubjectInput(isOther);
                    setEditRootSubjectId(isOther ? null : Number(value));
                    setEditChildSubject('');
                    setShowNewSubjectInput(false);
                    setNewSubjectName('');
                    if (!isOther) setNewRootSubjectName('');
                  }}
                />
              </div>
              {showNewRootSubjectInput && (
                <div className="new-root-fields">
                  <div className="form-group">
                    <label htmlFor="new-root-document-subject">Tên môn học mới</label>
                    <input
                      id="new-root-document-subject"
                      className="input-control"
                      value={newRootSubjectName}
                      onChange={(event) => setNewRootSubjectName(event.target.value)}
                      placeholder="Để trống sẽ phân loại là Khác"
                      maxLength={100}
                      autoFocus
                    />
                  </div>
                  <div className="form-group">
                    <label htmlFor="new-root-child-subject">Chuyên mục</label>
                    <input
                      id="new-root-child-subject"
                      className="input-control"
                      value={newSubjectName}
                      onChange={(event) => setNewSubjectName(event.target.value)}
                      placeholder="Để trống sẽ dùng tên môn học làm chuyên mục"
                      maxLength={100}
                    />
                  </div>
                  <small>Nếu không nhập môn học, tài liệu sẽ được phân loại là “Khác” và nội dung chuyên mục sẽ không được tạo.</small>
                </div>
              )}
              {selectedEditRoot && !showNewRootSubjectInput && (
                <div className="form-group subject-child-field">
                  <label>Chuyên mục</label>
                  <SearchableSelect
                    value={showNewSubjectInput ? OTHER_SUBJECT_VALUE : editChildSubject}
                    placeholder="Chọn chuyên mục"
                    searchPlaceholder="Nhập để tìm chuyên mục..."
                    options={[
                      { value: '', label: 'Không có chuyên mục' },
                      ...availableEditChildren.map((subject) => ({ value: subject.name, label: subject.name })),
                      { value: OTHER_SUBJECT_VALUE, label: 'Thêm chuyên mục mới' },
                    ]}
                    onChange={(value) => {
                      const isOther = value === OTHER_SUBJECT_VALUE;
                      setShowNewSubjectInput(isOther);
                      setEditChildSubject(isOther ? '' : value);
                      if (!isOther) setNewSubjectName('');
                    }}
                  />
                </div>
              )}
              {showNewSubjectInput && selectedEditRoot && !showNewRootSubjectInput && (
                <div className="form-group new-child-subject-field">
                  <label htmlFor="new-document-subject">Tên chuyên mục mới</label>
                  <input
                    id="new-document-subject"
                    className="input-control"
                    value={newSubjectName}
                    onChange={(event) => setNewSubjectName(event.target.value)}
                    placeholder="Để trống sẽ không tạo chuyên mục con"
                    maxLength={100}
                    autoFocus
                  />
                  <small>Chuyên mục mới sẽ được gửi tới Moderator để duyệt.</small>
                </div>
              )}
              {metadataError && <span className="form-error" role="alert">{metadataError}</span>}
              <div className="modal-actions">
                <button type="button" className="btn-secondary" onClick={() => setShowEditMetadataModal(false)} disabled={savingMetadata}>
                  Hủy
                </button>
                <button
                  type="submit"
                  className="btn-primary"
                  disabled={
                    savingMetadata ||
                    !editTitle.trim() ||
                    (!showNewRootSubjectInput && !editRootSubjectId)
                  }
                >
                  {savingMetadata ? <><Loader className="spin" size={16} /> Đang lưu...</> : 'Lưu thay đổi'}
                </button>
              </div>
            </form>
          </div>,
          document.body,
        )}

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
                  required
                >
                  {reportReasons.map((reason) => (
                    <option key={reason.reasonCode} value={reason.reasonCode}>
                      {reason.description}
                    </option>
                  ))}
                </select>
              </div>

              <div className="form-group">
                <label>Chi tiết thêm</label>
                <textarea
                  placeholder="Mô tả cụ thể về nội dung vi phạm..."
                  value={reportDetails}
                  onChange={(e) => {
                    setReportDetails(e.target.value);
                    if (e.target.value.trim()) setFormError('');
                  }}
                  className="input-control text-area"
                  rows={4}
                  required
                />
                {formError && (
                  <span className="form-error" role="alert">
                    {formError}
                  </span>
                )}
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  onClick={() => setShowReportModal(false)}
                  className="btn-secondary"
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
      {showDeleteModal && (
        <div className="modal-overlay">
          <div className="modal-box glass-panel">
            <h3>Xóa tài liệu?</h3>
            <p>
              Tài liệu <strong>{doc?.title}</strong> sẽ bị xóa vĩnh viễn và không thể khôi phục.
            </p>
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setShowDeleteModal(false)}>
                Hủy
              </button>
              <button className="btn-secondary delete-btn" onClick={handleDelete}>
                Xác nhận xóa
              </button>
            </div>
          </div>
        </div>
      )}
      {appealForm &&
        createPortal(
          <div
            className="viewport-modal-overlay appeal-overlay"
            onMouseDown={() => !reporting && setAppealForm(null)}
          >
            <form
              className="modal-box glass-panel appeal-modal animate-slide-up"
              onMouseDown={(event) => event.stopPropagation()}
              onSubmit={async (event) => {
                event.preventDefault();
                if (!appealForm.explanation.trim()) {
                  setFormError('Vui lòng nhập nội dung giải trình.');
                  return;
                }
                setReporting(true);
                try {
                  await api.document.appeal(appealForm.reportId, {
                    explanation: appealForm.explanation.trim(),
                    evidenceUrl: appealForm.evidenceUrl.trim() || null,
                  });
                  setDoc((current) =>
                    current
                      ? {
                          ...current,
                          requiresAppeal: false,
                          publicReviewBlocked: true,
                          appealStatus: 'PENDING',
                        }
                      : current,
                  );
                  setAppealForm(null);
                  notify('Đã gửi giải trình tới Moderator.', 'success');
                } catch (error: any) {
                  setFormError(error.message || 'Không thể gửi giải trình.');
                } finally {
                  setReporting(false);
                }
              }}
            >
              <button
                type="button"
                className="appeal-close"
                onClick={() => setAppealForm(null)}
                aria-label="Đóng"
              >
                ×
              </button>
              <div className="appeal-heading">
                <span className="appeal-heading-icon">
                  <AlertOctagon size={23} />
                </span>
                <div>
                  <small>YÊU CẦU XEM XÉT LẠI</small>
                  <h2>Gửi giải trình</h2>
                  <p>
                    Tài liệu:{' '}
                    <strong>
                      {doc?.title}.{doc?.fileExtension}
                    </strong>
                  </p>
                </div>
              </div>
              <p className="appeal-guidance">
                Giải thích rõ lý do và cung cấp căn cứ để Moderator xem xét lại quyết định vi phạm.
                Trong thời gian chờ xử lý, tài liệu không thể yêu cầu công khai.
              </p>
              <div className="report-form">
                <label>
                  <span>
                    Nội dung giải trình <em>*</em>
                  </span>
                  <textarea
                    className="input-control"
                    rows={5}
                    value={appealForm.explanation}
                    onChange={(e) => {
                      setAppealForm({ ...appealForm, explanation: e.target.value });
                      if (e.target.value.trim()) setFormError('');
                    }}
                    placeholder="Nội dung giải trình và căn cứ..."
                  />
                </label>
                <label>
                  <span>
                    Liên kết bằng chứng <small>(không bắt buộc)</small>
                  </span>
                  <input
                    type="url"
                    className="input-control"
                    value={appealForm.evidenceUrl}
                    onChange={(e) => setAppealForm({ ...appealForm, evidenceUrl: e.target.value })}
                    placeholder="URL bằng chứng (không bắt buộc)"
                  />
                </label>
                {formError && (
                  <span className="form-error" role="alert">
                    {formError}
                  </span>
                )}
                <div className="modal-actions">
                  <button
                    type="button"
                    className="btn-secondary"
                    onClick={() => setAppealForm(null)}
                  >
                    Hủy
                  </button>
                  <button type="submit" className="btn-primary" disabled={reporting}>
                    {reporting ? <Loader className="spin" size={16} /> : 'Gửi giải trình'}
                  </button>
                </div>
              </div>
            </form>
          </div>,
          document.body,
        )}
      {publicRequestBlock &&
        createPortal(
          <div className="viewport-modal-overlay" onMouseDown={() => setPublicRequestBlock(null)}>
            <div
              className="modal-box glass-panel public-block-modal animate-slide-up"
              onMouseDown={(event) => event.stopPropagation()}
            >
              <span className="public-block-icon">
                <AlertOctagon size={26} />
              </span>
              <h3>Chưa thể yêu cầu công khai</h3>
              <p>{publicRequestBlock}</p>
              <div className="modal-actions">
                <button className="btn-secondary" onClick={() => setPublicRequestBlock(null)}>
                  Đã hiểu
                </button>
                {doc?.requiresAppeal && (
                  <button
                    className="btn-primary"
                    onClick={async () => {
                      setPublicRequestBlock(null);
                      await handleAppeal();
                    }}
                  >
                    Gửi giải trình ngay
                  </button>
                )}
              </div>
            </div>
          </div>,
          document.body,
        )}
      {showCitationMismatchModal && citationMetadata &&
        createPortal(
          <div className="viewport-modal-overlay" onMouseDown={() => setShowCitationMismatchModal(false)}>
            <div
              className="modal-box glass-panel animate-slide-up"
              onMouseDown={(event) => event.stopPropagation()}
              style={{ maxWidth: '520px' }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '12px' }}>
                <AlertOctagon size={24} style={{ color: '#f59e0b', flexShrink: 0 }} />
                <h3 style={{ margin: 0 }}>Bản ghi trích dẫn AI</h3>
              </div>
              <p style={{ color: 'var(--text-muted, #94a3b8)', fontSize: '0.9rem', marginBottom: '12px' }}>
                Trích dẫn được ghi nhận từ phiên bản <strong>v{citationMetadata.versionNumberSnapshot ?? 1}</strong>:
              </p>
              <div
                style={{
                  background: 'rgba(255, 255, 255, 0.05)',
                  padding: '14px',
                  borderRadius: '8px',
                  marginBottom: '14px',
                  fontSize: '0.88rem',
                  lineHeight: '1.5',
                  fontStyle: 'italic',
                  borderLeft: '4px solid var(--accent-color, #6366f1)',
                  color: 'var(--text-main, #f8fafc)',
                  maxHeight: '180px',
                  overflowY: 'auto',
                }}
              >
                "{citationMetadata.snippet}"
              </div>
              <p style={{ fontSize: '0.84rem', color: '#f59e0b', margin: '0 0 16px 0' }}>
                Vị trí trích dẫn đã thay đổi hoặc không tìm thấy khớp hoàn chỉnh trong văn bản trích xuất hiện tại.
              </p>
              <div className="modal-actions">
                <button className="btn-primary" onClick={() => setShowCitationMismatchModal(false)}>
                  Xem văn bản hiện có
                </button>
              </div>
            </div>
          </div>,
          document.body,
        )}

      {doc && (
        <ManageAccessModal
          itemType="document"
          itemId={doc.documentId}
          isOpen={showAccessModal}
          onClose={() => {
            setShowAccessModal(false);
            loadDocumentDetails();
          }}
        />
      )}

      {doc && (
        <DocumentVersionHistoryModal
          documentId={doc.documentId}
          isOpen={showVersionModal}
          onClose={() => setShowVersionModal(false)}
          onVersionChanged={() => loadDocumentDetails()}
        />
      )}

      <style>{`
        .viewer-container {
          min-height: 80vh;
        }

        .viewer-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 0.75rem;
          margin-top: 3rem;
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
          grid-template-columns: 280px minmax(0, 1fr);
          gap: 1.5rem;
          height: calc(100vh - 10rem);
          min-height: 600px;
        }

        .doc-metadata-panel {
          padding: 1.5rem;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 1rem;
          height: fit-content;
          overflow-y: auto;
          max-height: calc(100vh - 10rem);
        }

        .doc-large-icon {
          width:76px;
          height:76px;
          display:grid;
          place-items:center;
          border-radius:18px;
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

        .meta-row .subject-value {
          max-width: 145px;
          text-align: right;
          overflow-wrap: anywhere;
        }

        .edit-metadata-button {
          width: 100%;
          min-height: 42px;
          display: inline-flex;
          align-items: center;
          justify-content: center;
          gap: 0.5rem;
          margin-top: 0.15rem;
          border: 1px solid rgba(0, 180, 216, 0.35);
          border-radius: var(--radius-sm);
          background: rgba(0, 180, 216, 0.09);
          color: var(--accent-blue);
          font-weight: 700;
          cursor: pointer;
          transition: var(--transition-fast);
        }

        .edit-metadata-button:hover {
          border-color: var(--accent-blue);
          background: rgba(0, 180, 216, 0.16);
          transform: translateY(-1px);
        }

        .metadata-edit-modal {
          width: min(560px, calc(100vw - 2rem));
          padding: 1.6rem;
          border: 1px solid rgba(0, 180, 216, 0.18);
          border-radius: 18px;
          background: linear-gradient(145deg, rgba(18, 20, 32, 0.98), rgba(10, 12, 22, 0.98));
          box-shadow: 0 24px 80px rgba(0, 0, 0, 0.55), 0 0 45px rgba(0, 180, 216, 0.06);
        }

        .metadata-modal-heading {
          display: flex;
          align-items: flex-start;
          gap: 0.9rem;
          margin-bottom: 1.4rem;
          padding-bottom: 1.15rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.07);
        }

        .metadata-modal-heading > span {
          width: 42px;
          height: 42px;
          display: grid;
          place-items: center;
          flex: 0 0 auto;
          border-radius: 12px;
          background: linear-gradient(135deg, rgba(0, 180, 216, 0.2), rgba(157, 78, 221, 0.18));
          color: var(--accent-blue);
        }

        .metadata-modal-heading h3 {
          margin: 0 0 0.3rem;
          font-size: 1.15rem;
        }

        .metadata-modal-heading p {
          margin: 0;
          color: var(--text-secondary);
          font-size: 0.86rem;
          line-height: 1.5;
        }

        .metadata-edit-modal .form-group {
          margin-bottom: 1rem;
        }

        .metadata-edit-modal .form-group label,
        .new-subject-request > label,
        .new-root-subject-request > label {
          display: block;
          margin-bottom: 0.45rem;
          color: var(--text-secondary);
          font-size: 0.82rem;
          font-weight: 700;
        }

        .metadata-edit-modal .input-control {
          min-height: 44px;
          border-color: rgba(255, 255, 255, 0.11);
          background: rgba(255, 255, 255, 0.045);
        }

        .metadata-edit-modal .input-control:focus {
          border-color: rgba(0, 180, 216, 0.65);
          box-shadow: 0 0 0 3px rgba(0, 180, 216, 0.09);
        }

        .subject-child-field {
          padding-left: 0.9rem;
          border-left: 2px solid rgba(0, 180, 216, 0.22);
        }

        .searchable-select {
          position: relative;
        }

        .searchable-select-trigger {
          width: 100%;
          display: flex;
          align-items: center;
          justify-content: space-between;
          cursor: pointer;
          text-align: left;
        }

        .searchable-select.open .searchable-select-trigger {
          border-color: rgba(0, 180, 216, 0.65);
          box-shadow: 0 0 0 3px rgba(0, 180, 216, 0.09);
        }

        .searchable-select-menu {
          position: absolute;
          z-index: 20;
          top: calc(100% + 0.4rem);
          left: 0;
          right: 0;
          padding: 0.55rem;
          border: 1px solid rgba(0, 180, 216, 0.24);
          border-radius: 12px;
          background: rgba(13, 15, 26, 0.99);
          box-shadow: 0 18px 45px rgba(0, 0, 0, 0.5);
        }

        .searchable-select-menu > input {
          width: 100%;
          min-height: 40px;
          padding: 0.55rem 0.75rem;
          border: 1px solid rgba(255, 255, 255, 0.12);
          border-radius: 9px;
          outline: none;
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-primary);
        }

        .searchable-select-menu > input:focus {
          border-color: var(--accent-blue);
        }

        .searchable-select-options {
          max-height: 190px;
          overflow-y: auto;
          display: grid;
          gap: 0.2rem;
          margin-top: 0.45rem;
          padding-right: 0.2rem;
        }

        .searchable-select-options button {
          width: 100%;
          padding: 0.65rem 0.7rem;
          border: 0;
          border-radius: 8px;
          background: transparent;
          color: var(--text-secondary);
          cursor: pointer;
          text-align: left;
        }

        .searchable-select-options button:hover,
        .searchable-select-options button.active {
          background: rgba(0, 180, 216, 0.11);
          color: var(--text-primary);
        }

        .searchable-select-options button:last-of-type {
          margin-top: 0.25rem;
          border-top: 1px solid rgba(255, 255, 255, 0.07);
          color: var(--accent-blue);
        }

        .searchable-select-empty {
          padding: 0.7rem;
          color: var(--text-muted);
          font-size: 0.82rem;
          text-align: center;
        }

        .new-root-fields,
        .new-child-subject-field {
          margin-bottom: 1rem;
          padding: 0.9rem;
          border: 1px dashed rgba(0, 180, 216, 0.26);
          border-radius: var(--radius-sm);
          background: rgba(0, 180, 216, 0.04);
        }

        .new-root-fields > small,
        .new-child-subject-field > small {
          display: block;
          color: var(--text-muted);
          font-size: 0.78rem;
          line-height: 1.45;
        }

        .new-subject-request {
          margin: 0.15rem 0 1.15rem;
          padding: 0.8rem;
          border: 1px dashed rgba(157, 78, 221, 0.28);
          border-radius: var(--radius-sm);
          background: rgba(157, 78, 221, 0.045);
        }

        .new-root-subject-request {
          margin: -0.35rem 0 1rem;
          padding: 0.75rem 0.8rem;
          border: 1px dashed rgba(0, 180, 216, 0.26);
          border-radius: var(--radius-sm);
          background: rgba(0, 180, 216, 0.04);
        }

        .new-subject-request > button,
        .new-root-subject-request > button {
          width: 100%;
          display: flex;
          align-items: center;
          gap: 0.5rem;
          border: 0;
          background: transparent;
          color: #c4a7ff;
          font-weight: 700;
          cursor: pointer;
          text-align: left;
        }

        .new-root-subject-request > button {
          color: var(--accent-blue);
        }

        .new-subject-request > button span,
        .new-root-subject-request > button span {
          font-size: 1.15rem;
        }

        .new-subject-input-row {
          display: flex;
          gap: 0.55rem;
        }

        .new-subject-input-row .input-control {
          min-width: 0;
          flex: 1;
        }

        .new-subject-request small,
        .new-root-subject-request small {
          display: block;
          margin-top: 0.55rem;
          color: var(--text-muted);
          line-height: 1.45;
        }

        .metadata-edit-modal .modal-actions {
          padding-top: 1rem;
          border-top: 1px solid rgba(255, 255, 255, 0.07);
        }

        .title-with-extension {
          display: flex;
          align-items: center;
          gap: 0.6rem;
        }

        .title-with-extension .input-control {
          min-width: 0;
          flex: 1;
        }

        .title-with-extension span {
          color: var(--text-muted);
          font-weight: 700;
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
          padding: 1.5rem;
          display: flex;
          flex-direction: column;
          height: 100%;
          min-height: 0;
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
          min-height: 0;
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

        .citation-highlight {
          background: rgba(0, 180, 216, 0.28);
          color: inherit;
          border-radius: 3px;
          padding: 0.05rem 0.1rem;
        }

        .viewer-tabs {
          display: flex;
          gap: 0.5rem;
          margin-bottom: 0.75rem;
        }

        .viewer-tabs button {
          padding: 0.5rem 1rem;
          border-radius: var(--radius-sm);
          border: 1px solid rgba(255, 255, 255, 0.1);
          background: rgba(255, 255, 255, 0.03);
          color: var(--text-secondary);
          cursor: pointer;
          font-size: 0.85rem;
        }

        .viewer-tabs button.active {
          border-color: var(--accent-purple);
          background: rgba(157, 78, 221, 0.12);
          color: var(--text-primary);
        }

        .original-scroll-area {
          display: flex;
          flex-direction: column;
          flex: 1;
          min-height: 0;
          overflow-y: auto;
          overflow-x: auto;
          background: rgba(0, 0, 0, 0.15);
          border-radius: var(--radius-sm);
        }

        .original-preview-state {
          min-height: 240px;
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          gap: 0.75rem;
          color: var(--text-muted);
          text-align: center;
          padding: 2rem;
        }

        .original-preview-pdf {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.75rem;
          width: 100%;
        }

        .original-preview-pager {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          color: var(--text-secondary);
          font-size: 0.85rem;
        }

        .original-preview-pager button {
          border: 1px solid rgba(255, 255, 255, 0.1);
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-primary);
          border-radius: var(--radius-sm);
          padding: 0.35rem;
          cursor: pointer;
          display: flex;
        }

        .original-preview-pager button:disabled {
          opacity: 0.4;
          cursor: not-allowed;
        }

        .original-preview-docx {
          width: 100%;
          background: #fff;
          color: #111;
          padding: 1.5rem;
          border-radius: var(--radius-sm);
          overflow: auto;
        }

        .original-preview-xlsx {
          width: 100%;
          overflow: auto;
          background: #fff;
          color: #111;
          border-radius: var(--radius-sm);
          padding: 1rem;
        }

        .original-preview-xlsx table {
          border-collapse: collapse;
          font-size: 0.85rem;
        }

        .original-preview-xlsx td,
        .original-preview-xlsx th {
          border: 1px solid #ddd;
          padding: 0.3rem 0.5rem;
        }

        .coverage-warning {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          margin: 0.5rem 0 0.75rem;
          padding: 0.6rem 0.9rem;
          border-radius: var(--radius-sm);
          border: 1px solid rgba(245, 158, 11, 0.3);
          background: rgba(245, 158, 11, 0.08);
          color: var(--warning);
          font-size: 0.85rem;
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

        .appeal-overlay{z-index:3200}.appeal-modal{position:relative;width:min(640px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:1.5rem;display:grid;gap:1rem;background:rgba(17,17,26,.98)}.appeal-close{position:absolute;right:1rem;top:1rem;width:34px;height:34px;border:0;border-radius:8px;background:rgba(255,255,255,.05);color:var(--text-secondary);font-size:1.35rem;cursor:pointer}.appeal-heading{display:flex;align-items:flex-start;gap:.9rem;padding-right:2rem}.appeal-heading h2{margin:.15rem 0}.appeal-heading p{margin:.2rem 0 0;color:var(--text-secondary)}.appeal-heading-icon,.public-block-icon{width:46px;height:46px;display:grid;place-items:center;flex:0 0 auto;border-radius:12px;background:rgba(239,68,68,.12);color:#f87171}.appeal-guidance{padding:.85rem 1rem;border:1px solid rgba(56,189,248,.18);border-radius:9px;background:rgba(56,189,248,.06);color:var(--text-secondary);line-height:1.55}.appeal-modal label{display:grid;gap:.45rem}.appeal-modal label>span{font-weight:700}.appeal-modal label small{color:var(--text-muted);font-weight:400}.appeal-modal em{color:var(--danger)}.appeal-modal textarea{resize:vertical;min-height:130px}.appeal-modal .modal-actions{padding-top:.8rem;border-top:1px solid rgba(255,255,255,.08)}.public-block-modal{position:relative;width:min(520px,calc(100vw - 2rem));padding:1.5rem}.public-block-modal h3{margin:.9rem 0 .45rem}.public-block-modal>p{color:var(--text-secondary);line-height:1.65}.public-block-modal .modal-actions{flex-wrap:wrap}

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
