import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, type SubjectTreeNode } from '../services/api';
import { formatDateTime } from '../utils/dateTime';
import {
  AlertCircle,
  BookOpen,
  Check,
  CheckCircle2,
  Clock3,
  Eye,
  FileCheck2,
  FileWarning,
  History,
  Loader,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldCheck,
  Trash2,
  X,
  XCircle,
} from 'lucide-react';
import { useUiFeedback } from '../context/UiFeedbackContext';
import { OriginalDocumentPreview } from '../components/OriginalDocumentPreview';

type Tab = 'queue' | 'reports' | 'appeals' | 'subjects' | 'history';
type Decision = {
  kind: 'document' | 'report' | 'appeal';
  id: number;
  action: string;
  title: string;
  noteRequired: boolean;
};

const statusLabel: Record<string, string> = {
  PENDING_REVIEW: 'Chờ xét duyệt',
  IN_REVIEW: 'Đang xử lý',
  PENDING: 'Chờ xử lý',
  APPROVED: 'Đã duyệt',
  REJECTED: 'Từ chối',
  NEEDS_CHANGES: 'Cần chỉnh sửa',
  RESTRICTED: 'Đang ẩn tạm thời',
  NO_VIOLATION: 'Báo cáo không có căn cứ',
  VIOLATION_CONFIRMED: 'Đã xác nhận vi phạm',
  APPEALED: 'Có giải trình',
  RESTORED: 'Đã khôi phục',
  CLOSED: 'Đã đóng',
  UPHELD: 'Giữ quyết định',
};

const DEFAULT_SPLIT_PERCENT = 58;
const SPLIT_STORAGE_KEY = 'moderator_split_percent';

const flattenSubjectTree = (nodes: SubjectTreeNode[]): SubjectTreeNode[] =>
  nodes.flatMap((node) => [node, ...flattenSubjectTree(node.children || [])]);

const getInitialSplitPercent = (): number => {
  try {
    const saved = localStorage.getItem(SPLIT_STORAGE_KEY);
    if (saved) {
      const parsed = parseFloat(saved);
      if (!isNaN(parsed) && parsed >= 30 && parsed <= 70) {
        return parsed;
      }
    }
  } catch {
    // ignore
  }
  return DEFAULT_SPLIT_PERCENT;
};

export const ModeratorDashboard: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedTab = searchParams.get('tab');
  const [tab, setTab] = useState<Tab>(
    requestedTab === 'reports' || requestedTab === 'appeals' || requestedTab === 'subjects' || requestedTab === 'history'
      ? (requestedTab as Tab)
      : 'queue',
  );
  const [items, setItems] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('ALL');
  const [detail, setDetail] = useState<any>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [activeAction, setActiveAction] = useState<string>('approve');
  const [splitPercent, setSplitPercent] = useState<number>(getInitialSplitPercent);
  const [isDragging, setIsDragging] = useState<boolean>(false);
  const splitContainerRef = useRef<HTMLDivElement | null>(null);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [decisionError, setDecisionError] = useState('');
  const [sortKey, setSortKey] = useState('time');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');

  // Subject Moderation States
  const [showAddSubjectModal, setShowAddSubjectModal] = useState(false);
  const [newSubjectName, setNewSubjectName] = useState('');
  const [newSubjectParentId, setNewSubjectParentId] = useState<number | null>(null);
  const [subjectTree, setSubjectTree] = useState<SubjectTreeNode[]>([]);
  const [addingSubject, setAddingSubject] = useState(false);
  const [rejectingSubject, setRejectingSubject] = useState<{ id: number; name: string } | null>(null);
  const [rejectSubjectReason, setRejectSubjectReason] = useState('');
  const [summary, setSummary] = useState({
    pendingDocuments: 0,
    pendingReports: 0,
    pendingAppeals: 0,
    completed: 0,
  });

  useEffect(() => {
    if (!isDragging) return;
    const handleMouseMove = (e: MouseEvent) => {
      if (!splitContainerRef.current) return;
      const rect = splitContainerRef.current.getBoundingClientRect();
      const relativeX = e.clientX - rect.left;
      const newPercent = (relativeX / rect.width) * 100;
      // Giới hạn trong khoảng 30% đến 70% để đảm bảo mỗi cột luôn hiển thị ít nhất 30%
      const clamped = Math.max(30, Math.min(70, Math.round(newPercent * 10) / 10));
      setSplitPercent(clamped);
      try {
        localStorage.setItem(SPLIT_STORAGE_KEY, String(clamped));
      } catch {
        // ignore
      }
    };
    const handleMouseUp = () => {
      setIsDragging(false);
    };
    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging]);
  const toggleSort = (key: string) => {
    if (sortKey === key) setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
    else {
      setSortKey(key);
      setSortDirection('asc');
    }
  };
  const sortHeader = (key: string, label: string) => (
    <button
      className={`sortable-header ${sortKey === key ? 'active' : ''}`}
      onClick={() => toggleSort(key)}
    >
      {label}
      <span aria-hidden="true">
        {sortKey === key ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : ' ↕'}
      </span>
    </button>
  );

  const request = (nextTab = tab) =>
    nextTab === 'queue'
      ? api.moderation.getQueue()
      : nextTab === 'reports'
        ? api.moderation.getReports()
        : nextTab === 'appeals'
          ? api.moderation.getAppeals()
          : nextTab === 'subjects'
            ? api.moderatorSubjects.getSubjects()
            : api.moderation.getHistory();

  const load = async (nextTab = tab) => {
    setLoading(true);
    setError('');
    try {
      const [nextItems, nextSummary, nextSubjectTree] = await Promise.all([
        request(nextTab),
        api.moderation.getSummary(),
        nextTab === 'subjects' ? api.moderatorSubjects.getTree('APPROVED') : Promise.resolve(null),
      ]);
      setItems(nextItems);
      setSummary(nextSummary);
      if (nextSubjectTree) setSubjectTree(nextSubjectTree);
    } catch (e: any) {
      setError(e.message || 'Không thể tải dữ liệu kiểm duyệt.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setQuery('');
    setStatus('ALL');
    load(tab);
  }, [tab]);

  const filtered = useMemo(
    () =>
      items
        .filter((item) => {
          const haystack = JSON.stringify(item).toLocaleLowerCase('vi');
          const keyword = query.trim().toLocaleLowerCase('vi');
          const itemStatus = item.moderationStatus || item.status || item.newStatus || '';
          return (
            (!keyword || haystack.includes(keyword)) && (status === 'ALL' || itemStatus === status)
          );
        })
        .sort((left, right) => {
          const value = (item: any) =>
            sortKey === 'content' || sortKey === 'name'
              ? item.name || item.title || item.documentTitle || item.actorName || ''
              : sortKey === 'requestedBy'
                ? item.requestedByUsername || ''
                : sortKey === 'time'
                  ? item.moderationSubmittedAt || item.createdAt || ''
                  : sortKey === 'category'
                    ? item.subject || item.reportType || item.explanation || item.action || ''
                    : sortKey === 'note'
                      ? item.note || item.reviewNote || ''
                  : sortKey === 'status'
                    ? item.moderationStatus || item.status || item.newStatus || ''
                    : (item[sortKey] ?? '');
          const a = value(left),
            b = value(right);
          const result = sortKey === 'time'
            ? new Date(a || 0).getTime() - new Date(b || 0).getTime()
            : typeof a === 'number' && typeof b === 'number'
              ? a - b
              : String(a).localeCompare(String(b), 'vi', { numeric: true });
          return sortDirection === 'asc' ? result : -result;
        }),
    [items, query, status, sortKey, sortDirection],
  );
  const openStat = (nextTab: Tab) => {
    setTab(nextTab);
    setSearchParams({ tab: nextTab });
  };
  const openDocument = async (id: number, report?: any) => {
    setDetailLoading(true);
    setError('');
    setActiveAction(report ? 'confirm-violation' : 'approve');
    setNote('');
    setDecisionError('');
    try {
      const [value, textEvidence] = await Promise.all([
        api.moderation.getDocument(id),
        report
          ? api.moderation.getReportTextEvidence(report.reportId).catch(() => null)
          : Promise.resolve(null),
      ]);
      setDetail({
        ...value,
        report,
        evidenceText: textEvidence?.extractedText || null,
        isVersionPinned: textEvidence?.isVersionPinned ?? false,
        isLegacyFallback: textEvidence?.isLegacyFallback ?? false,
      });
    } catch (e: any) {
      setError(e.message || 'Không thể mở tài liệu.');
    } finally {
      setDetailLoading(false);
    }
  };

  const handleInlineSubmitDecision = async () => {
    if (!detail || !activeAction || saving) return;
    const noteRequired =
      activeAction === 'request-changes' ||
      activeAction === 'reject' ||
      activeAction === 'confirm-violation' ||
      activeAction === 'temporarily-hide';
    if (noteRequired && !note.trim()) {
      setDecisionError(requiredNoteMessage(activeAction));
      return;
    }
    setSaving(true);
    setError('');
    setDecisionError('');
    try {
      if (detail.report) {
        await api.moderation.resolveReport(detail.report.reportId, activeAction, note.trim());
      } else {
        await api.moderation.reviewDocument(detail.document.documentId, activeAction, note.trim());
      }
      setDetail(null);
      await load();
    } catch (e: any) {
      setDecisionError(e.message || 'Không thể lưu quyết định.');
    } finally {
      setSaving(false);
    }
  };

  const openDecision = (value: Decision) => {
    setDecision(value);
    setNote('');
    setDecisionError('');
  };
  const submitDecision = async () => {
    if (!decision || saving) return;
    if (decision.noteRequired && !note.trim()) {
      setDecisionError(requiredNoteMessage(decision.action));
      return;
    }
    setSaving(true);
    setError('');
    setDecisionError('');
    try {
      if (decision.kind === 'document')
        await api.moderation.reviewDocument(decision.id, decision.action, note.trim());
      else if (decision.kind === 'report')
        await api.moderation.resolveReport(decision.id, decision.action, note.trim());
      else await api.moderation.resolveAppeal(decision.id, decision.action, note.trim());
      setDecision(null);
      setDetail(null);
      await load();
    } catch (e: any) {
      setError(e.message || 'Không thể lưu quyết định.');
    } finally {
      setSaving(false);
    }
  };

  const handleCreateSubject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newSubjectName.trim()) return;
    setAddingSubject(true);
    try {
      await api.moderatorSubjects.createSubject(newSubjectName.trim(), newSubjectParentId);
      setShowAddSubjectModal(false);
      setNewSubjectName('');
      setNewSubjectParentId(null);
      await load('subjects');
    } catch (err: any) {
      setError(err.message || 'Lỗi thêm môn học.');
    } finally {
      setAddingSubject(false);
    }
  };

  const handleApproveSubject = async (subjectId: number) => {
    try {
      await api.moderatorSubjects.approveSubject(subjectId);
      await load('subjects');
    } catch (err: any) {
      setError(err.message || 'Lỗi phê duyệt môn học.');
    }
  };

  const handleRejectSubject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!rejectingSubject || !rejectSubjectReason.trim()) return;
    try {
      await api.moderatorSubjects.rejectSubject(rejectingSubject.id, rejectSubjectReason.trim());
      setRejectingSubject(null);
      setRejectSubjectReason('');
      await load('subjects');
    } catch (err: any) {
      setError(err.message || 'Lỗi từ chối môn học.');
    }
  };

  const handleDeleteSubject = async (subjectId: number, subjectName: string) => {
    if (
      !(await confirm({
        title: 'Xóa môn học',
        message: `Bạn có chắc chắn muốn xóa danh mục môn học “${subjectName}” không?`,
        confirmLabel: 'Xóa môn học',
        danger: true,
      }))
    ) {
      return;
    }
    try {
      await api.moderatorSubjects.deleteSubject(subjectId);
      notify(`Đã xóa môn học "${subjectName}".`, 'success');
      await load('subjects');
    } catch (err: any) {
      setError(err.message || 'Lỗi xóa môn học.');
    }
  };

  const tabs: [Tab, string, React.ReactNode][] = [
    ['queue', 'Chờ xét duyệt', <FileCheck2 key="queue" size={17} />],
    ['reports', 'Báo cáo nội dung', <FileWarning key="reports" size={17} />],
    ['appeals', 'Giải trình', <RotateCcw key="appeals" size={17} />],
    ['subjects', 'Danh mục môn học', <BookOpen key="subjects" size={17} />],
    ['history', 'Lịch sử xử lý', <History key="history" size={17} />],
  ];
  return (
    <div className="moderator-page">
      <header className="moderator-header">
        <div>
          <span className="eyebrow">
            <ShieldCheck size={17} /> CONTENT MODERATION
          </span>
          <h1>Trung tâm kiểm duyệt</h1>
          <p>Xét duyệt tài liệu công khai và xử lý phản hồi cộng đồng theo quy trình minh bạch.</p>
        </div>
        <button className="btn-secondary refresh-btn" onClick={() => load()} disabled={loading}>
          <RefreshCw size={17} className={loading ? 'spin' : ''} /> Làm mới
        </button>
      </header>
      <section className="moderator-stats">
        <button className="stat-card glass-card" onClick={() => openStat('queue')}>
          <span className="stat-icon pending">
            <Clock3 />
          </span>
          <div>
            <small>Chờ xử lý</small>
            <strong>{summary.pendingDocuments}</strong>
          </div>
        </button>
        <button className="stat-card glass-card" onClick={() => openStat('reports')}>
          <span className="stat-icon active">
            <AlertCircle />
          </span>
          <div>
            <small>Báo cáo cần xử lý</small>
            <strong>{summary.pendingReports}</strong>
          </div>
        </button>
        <button className="stat-card glass-card" onClick={() => openStat('appeals')}>
          <span className="stat-icon done">
            <CheckCircle2 />
          </span>
          <div>
            <small>Giải trình cần giải quyết</small>
            <strong>{summary.pendingAppeals}</strong>
          </div>
        </button>
        <div className="stat-card glass-card">
          <span className="stat-icon total">
            <ShieldCheck />
          </span>
          <div>
            <small>Đã hoàn tất</small>
            <strong>{summary.completed}</strong>
          </div>
        </div>
      </section>
      <section className="moderator-workspace glass-panel">
        <nav className="moderator-tabs">
          {tabs.map(([id, label, icon]) => (
            <button key={id} className={tab === id ? 'active' : ''} onClick={() => openStat(id)}>
              {icon}
              <span>{label}</span>
            </button>
          ))}
        </nav>
        <div className="moderator-toolbar">
          <label className="moderator-search">
            <Search size={17} />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={tab === 'subjects' ? 'Tìm tên môn học, người đề xuất...' : 'Tìm tài liệu, người gửi, lý do...'}
            />
          </label>
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="ALL">Tất cả trạng thái</option>
            {Array.from(
              new Set(
                items.map((x) => x.moderationStatus || x.status || x.newStatus).filter(Boolean),
              ),
            ).map((x) => (
              <option key={x} value={x}>
                {statusLabel[x] || x}
              </option>
            ))}
          </select>
          {tab === 'subjects' && (
            <button
              className="btn-primary"
              style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', padding: '0.65rem 1rem', whiteSpace: 'nowrap', fontWeight: 600 }}
              onClick={() => setShowAddSubjectModal(true)}
            >
              <Plus size={16} /> Thêm môn học
            </button>
          )}
          <span className="result-count">{filtered.length} kết quả</span>
        </div>
        {error && (
          <div className="moderator-alert">
            <AlertCircle size={18} />
            {error}
          </div>
        )}
        {loading ? (
          <div className="moderator-state">
            <Loader className="spin" />
            <strong>Đang tải dữ liệu kiểm duyệt</strong>
          </div>
        ) : filtered.length === 0 ? (
          <div className="moderator-state">
            <ShieldCheck size={42} />
            <strong>Không có dữ liệu phù hợp</strong>
            <p>Hàng đợi hiện đã được xử lý.</p>
          </div>
        ) : (
          <div className="moderator-table-wrap">
            <table className="moderator-table">
              <thead>
                {tab === 'subjects' ? (
                  <tr>
                    <th>{sortHeader('name', 'Tên môn học')}</th>
                    <th>{sortHeader('requestedBy', 'Người đề xuất')}</th>
                    <th>{sortHeader('status', 'Trạng thái')}</th>
                    <th>{sortHeader('time', 'Thời gian')}</th>
                    <th className="actions-col">Thao tác</th>
                  </tr>
                ) : tab === 'history' ? (
                  <tr>
                    <th>{sortHeader('content', 'Tài liệu')}</th>
                    <th>{sortHeader('category', 'Hành động')}</th>
                    <th>{sortHeader('status', 'Trạng thái')}</th>
                    <th>{sortHeader('time', 'Thời gian')}</th>
                    <th>{sortHeader('note', 'Ghi chú')}</th>
                  </tr>
                ) : (
                  <tr>
                    <th>{sortHeader('content', 'Nội dung')}</th>
                    <th>{sortHeader('category', 'Phân loại')}</th>
                    <th>{sortHeader('status', 'Trạng thái')}</th>
                    <th>{sortHeader('time', 'Thời gian')}</th>
                    <th className="actions-col">Thao tác</th>
                  </tr>
                )}
              </thead>
              <tbody>
                {tab === 'queue' &&
                  filtered.map((x) => (
                    <tr
                      key={x.documentId}
                      className="clickable-row"
                      onClick={() => openDocument(x.documentId)}
                    >
                      <td>
                        <div className="primary-cell">
                          <span className="file-mark">{x.fileExtension?.toUpperCase()}</span>
                          <div>
                            <strong>{x.title}</strong>
                            <small>
                              {x.uploaderName} · {Number(x.fileSizeMb || 0).toFixed(2)} MB
                            </small>
                          </div>
                        </div>
                      </td>
                      <td>{x.subject || 'Khác'}</td>
                      <td>
                        <Badge value={x.moderationStatus} />
                      </td>
                      <td>{formatDate(x.moderationSubmittedAt)}</td>
                      <td>
                        <div className="row-actions" onClick={(e) => e.stopPropagation()}>
                          <button
                            title="Xem chi tiết & Xét duyệt"
                            className="btn-review-row"
                            onClick={() => openDocument(x.documentId)}
                          >
                            <Eye size={15} />
                            <span>Xem & Xét duyệt</span>
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                {tab === 'reports' &&
                  filtered.map((x) => (
                    <tr
                      key={x.reportId}
                      className="clickable-row"
                      onClick={() => openDocument(x.documentId, x)}
                    >
                      <td>
                        <div className="primary-cell">
                          <span className="file-mark report">
                            <FileWarning />
                          </span>
                          <div>
                            <strong>{x.documentTitle}</strong>
                            <small>Báo cáo bởi {x.reporterName}</small>
                          </div>
                        </div>
                      </td>
                      <td>
                        <strong>{x.reportType}</strong>
                        <small className="block">{x.reasonCode}</small>
                      </td>
                      <td>
                        <Badge value={x.status} />
                      </td>
                      <td>{formatDate(x.createdAt)}</td>
                      <td>
                        <div className="row-actions textual" onClick={(e) => e.stopPropagation()}>
                          {x.status === 'PENDING' && (
                            <button
                              onClick={async () => {
                                setError('');
                                try {
                                  await api.moderation.assignReport(x.reportId);
                                  await load();
                                } catch (e: any) {
                                  setError(e.message || 'Không thể nhận báo cáo.');
                                }
                              }}
                            >
                              Nhận xử lý
                            </button>
                          )}
                          {x.status === 'IN_REVIEW' && (
                            <button
                              className="btn-review-row"
                              onClick={() => openDocument(x.documentId, x)}
                            >
                              <Eye size={15} />
                              <span>Xử lý báo cáo</span>
                            </button>
                          )}
                          {x.status === 'RESTRICTED' && (
                            <button
                              className="btn-review-row"
                              onClick={() => openDocument(x.documentId, x)}
                            >
                              <Eye size={15} />
                              <span>Xem & Mở khóa</span>
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                {tab === 'appeals' &&
                  filtered.map((x) => (
                    <tr key={x.appealId}>
                      <td>
                        <div className="primary-cell">
                          <span className="file-mark appeal">
                            <RotateCcw />
                          </span>
                          <div>
                            <strong>{x.documentTitle}</strong>
                            <small>Người gửi: {x.submittedByName}</small>
                          </div>
                        </div>
                      </td>
                      <td>
                        <span className="block" title={x.explanation}>
                          {x.explanation}
                        </span>
                      </td>
                      <td>
                        <Badge value={x.status} />
                      </td>
                      <td>{formatDate(x.createdAt)}</td>
                      <td>
                        <div className="row-actions textual">
                          {x.status === 'PENDING' ? (
                            <>
                              <button
                                className="success"
                                onClick={() =>
                                  openDecision({
                                    kind: 'appeal',
                                    id: x.appealId,
                                    action: 'restore',
                                    title: x.documentTitle,
                                    noteRequired: false,
                                  })
                                }
                              >
                                Chấp thuận
                              </button>
                              <button
                                className="danger"
                                onClick={() =>
                                  openDecision({
                                    kind: 'appeal',
                                    id: x.appealId,
                                    action: 'uphold',
                                    title: x.documentTitle,
                                    noteRequired: true,
                                  })
                                }
                              >
                                Bác giải trình
                              </button>
                            </>
                          ) : (
                            <span className="history-note">{x.reviewNote || 'Đã xử lý'}</span>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                {tab === 'subjects' &&
                  filtered.map((x) => (
                    <tr key={x.subjectId}>
                      <td>
                        <div className="primary-cell">
                          <span className="file-mark" style={{ background: 'rgba(0,180,216,0.13)', color: 'var(--accent-blue)' }}>
                            <BookOpen size={18} />
                          </span>
                          <div>
                            <strong>{x.name}</strong>
                            <small>Mã số: #{x.subjectId}</small>
                          </div>
                        </div>
                      </td>
                      <td>
                        <strong>{x.requestedByUsername || 'Hệ thống'}</strong>
                        {x.approvedByUsername && <small className="block">Duyệt bởi: {x.approvedByUsername}</small>}
                      </td>
                      <td>
                        <Badge value={x.status} />
                        {x.rejectionReason && (
                          <small className="block" style={{ color: '#f87171', marginTop: '0.2rem' }}>
                            Lý do: {x.rejectionReason}
                          </small>
                        )}
                      </td>
                      <td>{formatDate(x.createdAt)}</td>
                      <td>
                        <div className="row-actions textual">
                          {x.status === 'PENDING' && (
                            <>
                              <button
                                className="success"
                                onClick={() => handleApproveSubject(x.subjectId)}
                                title="Phê duyệt môn học này"
                              >
                                <Check size={14} /> Duyệt
                              </button>
                              <button
                                className="danger"
                                onClick={() => {
                                  setRejectingSubject({ id: x.subjectId, name: x.name });
                                  setRejectSubjectReason('');
                                }}
                                title="Từ chối môn học này"
                              >
                                <X size={14} /> Từ chối
                              </button>
                            </>
                          )}
                          {x.status !== 'PENDING' && (
                            <button
                              className="danger"
                              onClick={() => handleDeleteSubject(x.subjectId, x.name)}
                              title="Xóa môn học"
                            >
                              <Trash2 size={14} /> Xóa
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                {tab === 'history' &&
                  filtered.map((x) => (
                    <tr key={x.actionId}>
                      <td>
                        <div className="primary-cell">
                          <span className="file-mark history">
                            <History />
                          </span>
                          <div>
                            <strong>{x.documentTitle || `Mục #${x.targetId || x.actionId}`}</strong>
                            <small>Bởi {x.actorName}</small>
                          </div>
                        </div>
                      </td>
                      <td>
                        <strong>{x.action}</strong>
                      </td>
                      <td>
                        <Badge value={x.newStatus} />
                      </td>
                      <td>{formatDate(x.createdAt)}</td>
                      <td>
                        <span className="history-note">{x.note || 'Không có ghi chú'}</span>
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      {detailLoading && (
        <div className="modal-overlay">
          <div className="moderator-modal glass-panel loading-modal">
            <Loader className="spin" /> Đang tải nội dung...
          </div>
        </div>
      )}
      {detail && !detailLoading && (
        <div className="modal-overlay" onMouseDown={() => !saving && setDetail(null)}>
          <div
            className="moderator-modal detail-split-modal glass-panel animate-slide-up"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="split-modal-header">
              <ModalTitle
                title={detail.document.title + '.' + detail.document.fileExtension}
                subtitle={`Đăng bởi ${detail.document.uploaderName} · ${detail.document.subject || 'Khác'} · ${Number(detail.document.fileSizeMb || 0).toFixed(2)} MB`}
                close={() => !saving && setDetail(null)}
              />
            </div>

            <div
              ref={splitContainerRef}
              className={`detail-split-body ${isDragging ? 'is-resizing' : ''}`}
            >
              {/* Left Column: Content preview & metadata */}
              <div className="split-left-content" style={{ width: `calc(${splitPercent}% - 8px)` }}>
                <div className="detail-summary-grid">
                  <div>
                    <small>Trạng thái hiện tại</small>
                    <Badge value={detail.report?.status || detail.document.moderationStatus} />
                  </div>
                  <div>
                    <small>Môn học</small>
                    <strong>{detail.document.subject || 'Khác'}</strong>
                  </div>
                  <div>
                    <small>Kích thước tệp</small>
                    <strong>{Number(detail.document.fileSizeMb || 0).toFixed(2)} MB</strong>
                  </div>
                  <div>
                    <small>Độ phủ trích xuất</small>
                    <strong>
                      {typeof detail.document.extractionCoveragePercent === 'number'
                        ? `${Math.round(detail.document.extractionCoveragePercent * 100)}%`
                        : 'Hoàn tất'}
                    </strong>
                  </div>
                </div>

                {detail.report && (
                  <section className="report-detail-box">
                    <small>CHI TIẾT BÁO CÁO VI PHẠM</small>
                    <p>
                      <strong>{detail.report.reasonCode}</strong> · Loại báo cáo: {detail.report.reportType}
                    </p>
                    <p>
                      {detail.report.additionalDetails ||
                        detail.report.evidenceDescription ||
                        'Không có mô tả bổ sung từ người báo cáo.'}
                    </p>
                    {detail.report.originalWorkUrl && (
                      <p>
                        Link tác phẩm gốc:{' '}
                        <a
                          href={detail.report.originalWorkUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="preview-full-link"
                        >
                          {detail.report.originalWorkUrl}
                        </a>
                      </p>
                    )}
                    <span>Người báo cáo: {detail.report.reporterName || detail.report.claimantName || 'Ẩn danh'}</span>
                  </section>
                )}

                <section className="preview-box-enhanced">
                  <div className="original-file-preview-container custom-scroll" style={{ padding: 0 }}>
                    <OriginalDocumentPreview
                      documentId={detail.document.documentId}
                      fileExtension={detail.document.fileExtension}
                      evidenceReportId={detail.report?.reportId}
                      showToolbar={true}
                    />
                  </div>
                </section>
              </div>

              {/* Draggable Resizer Divider */}
              <div
                className={`split-resizer ${isDragging ? 'active' : ''}`}
                onMouseDown={(e) => {
                  e.preventDefault();
                  setIsDragging(true);
                }}
                title="Kéo sang trái/phải để điều chỉnh độ rộng 2 cột (Tối thiểu 30%)"
              >
                <div className="resizer-handle" />
              </div>

              {/* Right Column: Action Side Panel for Live Review & Comment */}
              <div className="split-right-actions" style={{ width: `calc(${100 - splitPercent}% - 8px)` }}>
                <div className="side-panel-header">
                  <h4>Thao tác kiểm duyệt</h4>
                  <small>Chọn quyết định và để lại phản hồi cho tác giả</small>
                </div>

                {/* Queue Review Actions */}
                {!detail.report ? (
                  <div className="decision-selector">
                    <button
                      type="button"
                      className={`decision-option-btn approve ${activeAction === 'approve' ? 'active' : ''}`}
                      onClick={() => {
                        setActiveAction('approve');
                        setDecisionError('');
                      }}
                    >
                      <CheckCircle2 size={18} />
                      <div>
                        <strong>Phê duyệt công khai</strong>
                        <small>Cho phép xuất hiện trên Thư viện</small>
                      </div>
                    </button>

                    <button
                      type="button"
                      className={`decision-option-btn request-changes ${activeAction === 'request-changes' ? 'active' : ''}`}
                      onClick={() => {
                        setActiveAction('request-changes');
                        setDecisionError('');
                      }}
                    >
                      <RefreshCw size={18} />
                      <div>
                        <strong>Yêu cầu chỉnh sửa</strong>
                        <small>Bắt buộc nêu rõ nội dung cần sửa</small>
                      </div>
                    </button>

                    <button
                      type="button"
                      className={`decision-option-btn reject ${activeAction === 'reject' ? 'active' : ''}`}
                      onClick={() => {
                        setActiveAction('reject');
                        setDecisionError('');
                      }}
                    >
                      <XCircle size={18} />
                      <div>
                        <strong>Từ chối công khai</strong>
                        <small>Bắt buộc nêu rõ lý do từ chối</small>
                      </div>
                    </button>
                  </div>
                ) : (
                  /* Report Resolution Actions */
                  <div className="decision-selector">
                    <button
                      type="button"
                      className={`decision-option-btn reject ${activeAction === 'confirm-violation' ? 'active' : ''}`}
                      onClick={() => {
                        setActiveAction('confirm-violation');
                        setDecisionError('');
                      }}
                    >
                      <XCircle size={18} />
                      <div>
                        <strong>Xác nhận vi phạm</strong>
                        <small>Khóa tài liệu & gắn cờ vi phạm</small>
                      </div>
                    </button>

                    <button
                      type="button"
                      className={`decision-option-btn request-changes ${activeAction === 'temporarily-hide' ? 'active' : ''}`}
                      onClick={() => {
                        setActiveAction('temporarily-hide');
                        setDecisionError('');
                      }}
                    >
                      <AlertCircle size={18} />
                      <div>
                        <strong>Tạm ẩn tài liệu</strong>
                        <small>Khóa tạm thời để thẩm định thêm</small>
                      </div>
                    </button>

                    <button
                      type="button"
                      className={`decision-option-btn approve ${activeAction === 'no-violation' ? 'active' : ''}`}
                      onClick={() => {
                        setActiveAction('no-violation');
                        setDecisionError('');
                      }}
                    >
                      <CheckCircle2 size={18} />
                      <div>
                        <strong>Bác bỏ báo cáo</strong>
                        <small>Báo cáo không có căn cứ vi phạm</small>
                      </div>
                    </button>
                  </div>
                )}

                {/* Comment / Note Box */}
                <div className="comment-note-box">
                  <label className="comment-label">
                    <span>
                      {activeAction === 'approve'
                        ? 'Ghi chú phê duyệt (Tùy chọn)'
                        : activeAction === 'request-changes'
                          ? 'Nội dung yêu cầu chỉnh sửa'
                          : activeAction === 'no-violation'
                            ? 'Kết luận bác bỏ báo cáo'
                            : 'Lý do & Căn cứ xử lý'}
                    </span>
                    {(activeAction === 'request-changes' ||
                      activeAction === 'reject' ||
                      activeAction === 'confirm-violation' ||
                      activeAction === 'temporarily-hide') && <em className="required-star">*</em>}
                  </label>
                  <textarea
                    className="comment-textarea"
                    rows={5}
                    value={note}
                    onChange={(e) => {
                      setNote(e.target.value);
                      if (e.target.value.trim()) setDecisionError('');
                    }}
                    placeholder={
                      activeAction === 'approve'
                        ? 'Nhập ghi chú thêm cho tài liệu nếu cần...'
                        : activeAction === 'request-changes'
                          ? 'Chỉ rõ các phần cần sửa (VD: Nội dung trang 2 bị thiếu, sai môn học...)'
                          : activeAction === 'no-violation'
                            ? 'Nêu căn cứ xác định tài liệu không có vi phạm...'
                            : 'Nêu rõ lý do và căn cứ cho quyết định này...'
                    }
                  />
                  {decisionError && <span className="decision-error-text">{decisionError}</span>}
                </div>

                {/* Panel Footer Submit */}
                <div className="side-panel-footer">
                  <button
                    type="button"
                    className="btn-secondary"
                    onClick={() => setDetail(null)}
                    disabled={saving}
                  >
                    Đóng
                  </button>
                  <button
                    type="button"
                    className={`btn-primary submit-decision-btn ${
                      activeAction === 'reject' || activeAction === 'confirm-violation'
                        ? 'btn-danger'
                        : activeAction === 'request-changes' || activeAction === 'temporarily-hide'
                          ? 'btn-warning'
                          : 'btn-success'
                    }`}
                    onClick={handleInlineSubmitDecision}
                    disabled={saving || !activeAction}
                  >
                    {saving ? (
                      <>
                        <Loader className="spin" size={15} /> Đang lưu...
                      </>
                    ) : (
                      'Xác nhận quyết định'
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
      {decision && (
        <div className="modal-overlay" onMouseDown={() => !saving && setDecision(null)}>
          <div
            className="moderator-modal decision-modal glass-panel animate-slide-up"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <ModalTitle
              title="Xác nhận quyết định"
              subtitle={decision.title}
              close={() => !saving && setDecision(null)}
            />
            <div
              className={`decision-banner ${isDestructive(decision.action) ? 'danger' : 'safe'}`}
            >
              <strong>{decisionName(decision.action)}</strong>
              <p>Quyết định sẽ được lưu vào lịch sử kiểm duyệt.</p>
            </div>
            <label className="note-field">
              {noteFieldLabel(decision.action)} {decision.noteRequired && <em>*</em>}
              <textarea
                rows={5}
                value={note}
                aria-invalid={!!decisionError}
                onChange={(e) => {
                  setNote(e.target.value);
                  if (e.target.value.trim()) setDecisionError('');
                }}
                placeholder={notePlaceholder(decision.action)}
              />
              {decisionError && (
                <span role="alert" style={{ color: '#f87171', fontSize: '.82rem' }}>
                  {decisionError}
                </span>
              )}
            </label>
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setDecision(null)} disabled={saving}>
                Hủy
              </button>
              <button
                className={isDestructive(decision.action) ? 'btn-danger' : 'btn-primary'}
                onClick={submitDecision}
                disabled={saving}
              >
                {saving ? (
                  <>
                    <Loader className="spin" size={16} /> Đang lưu
                  </>
                ) : (
                  decisionName(decision.action)
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Add Subject Modal */}
      {showAddSubjectModal && (
        <div className="modal-overlay" onClick={() => setShowAddSubjectModal(false)}>
          <div className="moderator-modal glass-panel" onClick={(e) => e.stopPropagation()}>
            <ModalTitle
              title="Thêm danh mục môn học mới"
              subtitle="Môn học được tạo trực tiếp bởi Ban kiểm duyệt sẽ được tự động kích hoạt (APPROVED)."
              close={() => setShowAddSubjectModal(false)}
            />
            <form onSubmit={handleCreateSubject} style={{ marginTop: '1rem', display: 'grid', gap: '1rem' }}>
              <label className="note-field">
                Danh mục cha
                <select
                  value={newSubjectParentId ?? ''}
                  onChange={(e) => setNewSubjectParentId(e.target.value ? Number(e.target.value) : null)}
                >
                  <option value="">Không có — tạo môn học chính</option>
                  {flattenSubjectTree(subjectTree)
                    .filter((subject) => subject.depth < 3)
                    .map((subject) => (
                      <option key={subject.subjectId} value={subject.subjectId}>
                        {`${'— '.repeat(subject.depth)}${subject.name}`}
                      </option>
                    ))}
                </select>
                <small>
                  Chọn môn học chính để tạo chuyên mục con. Đây chỉ là cây phân loại, không tạo hoặc di chuyển thư mục tài liệu.
                </small>
              </label>
              <label className="note-field">
                Tên môn học hoặc chuyên mục <em>*</em>
                <input
                  type="text"
                  required
                  value={newSubjectName}
                  onChange={(e) => setNewSubjectName(e.target.value)}
                  placeholder="Ví dụ: Đại số tuyến tính, An toàn thông tin, Vi sinh vật học..."
                  maxLength={100}
                  style={{
                    width: '100%',
                    padding: '0.65rem 0.8rem',
                    borderRadius: '8px',
                    border: '1px solid rgba(255,255,255,0.15)',
                    background: 'rgba(0,0,0,0.2)',
                    color: 'inherit',
                    outline: 'none',
                  }}
                />
              </label>
              <div className="modal-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setShowAddSubjectModal(false)}
                  disabled={addingSubject}
                >
                  Hủy
                </button>
                <button
                  type="submit"
                  className="btn-primary"
                  disabled={addingSubject || !newSubjectName.trim()}
                >
                  {addingSubject ? (
                    <>
                      <Loader className="spin" size={16} /> Đang tạo...
                    </>
                  ) : (
                    'Tạo môn học'
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reject Subject Modal */}
      {rejectingSubject && (
        <div className="modal-overlay" onClick={() => setRejectingSubject(null)}>
          <div className="moderator-modal glass-panel" onClick={(e) => e.stopPropagation()}>
            <ModalTitle
              title={`Từ chối môn học “${rejectingSubject.name}”`}
              subtitle="Vui lòng nêu rõ lý do từ chối để thông báo tới sinh viên đề xuất."
              close={() => setRejectingSubject(null)}
            />
            <form onSubmit={handleRejectSubject} style={{ marginTop: '1rem', display: 'grid', gap: '1rem' }}>
              <label className="note-field">
                Lý do từ chối <em>*</em>
                <textarea
                  rows={4}
                  required
                  value={rejectSubjectReason}
                  onChange={(e) => setRejectSubjectReason(e.target.value)}
                  placeholder="Ví dụ: Tên môn học không hợp lệ, chứa từ ngữ vi phạm, hoặc đã có môn học tương tự..."
                  style={{
                    width: '100%',
                    padding: '0.65rem 0.8rem',
                    borderRadius: '8px',
                    border: '1px solid rgba(255,255,255,0.15)',
                    background: 'rgba(0,0,0,0.2)',
                    color: 'inherit',
                    outline: 'none',
                  }}
                />
              </label>
              <div className="modal-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setRejectingSubject(null)}
                >
                  Hủy
                </button>
                <button
                  type="submit"
                  className="btn-danger"
                  disabled={!rejectSubjectReason.trim()}
                >
                  Xác nhận từ chối
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{styles}</style>
    </div>
  );
};

const Badge = ({ value }: { value?: string }) => (
  <span className={`moderation-badge ${value || 'UNKNOWN'}`}>
    {statusLabel[value || ''] || value || 'Không xác định'}
  </span>
);
const ModalTitle = ({
  title,
  subtitle,
  close,
}: {
  title: string;
  subtitle: string;
  close: () => void;
}) => (
  <div className="modal-title-row">
    <div>
      <h3>{title}</h3>
      <p>{subtitle}</p>
    </div>
    <button aria-label="Đóng" onClick={close}>
      <X />
    </button>
  </div>
);
const formatDate = formatDateTime;
const isDestructive = (action: string) =>
  ['reject', 'temporarily-hide', 'confirm-violation', 'uphold'].includes(action);
const decisionName = (action: string) =>
  ({
    approve: 'Phê duyệt',
    'request-changes': 'Yêu cầu chỉnh sửa',
    reject: 'Từ chối',
    'no-violation': 'Bác báo cáo',
    'temporarily-hide': 'Ẩn tạm thời',
    'confirm-violation': 'Xác nhận vi phạm',
    restore: 'Khôi phục',
    uphold: 'Giữ quyết định',
  })[action] || action;
const noteFieldLabel = (action: string) =>
  action === 'request-changes'
    ? 'Nội dung cần chỉnh sửa'
    : action === 'no-violation'
      ? 'Kết luận xử lý'
      : 'Lý do và căn cứ';
const notePlaceholder = (action: string) =>
  action === 'request-changes'
    ? 'Mô tả cụ thể những nội dung người đăng cần chỉnh sửa...'
    : action === 'no-violation'
      ? 'Nêu căn cứ xác định báo cáo không có vi phạm...'
      : 'Nêu rõ lý do và căn cứ cho quyết định này...';
const requiredNoteMessage = (action: string) =>
  action === 'request-changes'
    ? 'Vui lòng nhập nội dung cần chỉnh sửa.'
    : action === 'reject'
      ? 'Vui lòng nhập lý do từ chối tài liệu.'
      : action === 'temporarily-hide'
        ? 'Vui lòng nhập căn cứ ẩn tài liệu tạm thời.'
        : action === 'confirm-violation'
          ? 'Vui lòng nhập căn cứ xác nhận vi phạm.'
          : action === 'no-violation'
            ? 'Vui lòng nhập kết luận bác báo cáo.'
            : 'Vui lòng nhập lý do xử lý.';
const styles = `
.moderator-stats button.stat-card{border:0;color:inherit;text-align:left;cursor:pointer}
.moderator-page{display:grid;gap:1.4rem;min-width:0}.moderator-header{display:flex;align-items:flex-end;justify-content:space-between;gap:1.5rem}.moderator-header h1{margin:.35rem 0;font-size:2rem}.moderator-header p{color:var(--text-secondary);max-width:720px}.eyebrow{display:flex;align-items:center;gap:.45rem;color:var(--accent-blue);font-size:.76rem;font-weight:800;letter-spacing:.12em}.refresh-btn{display:flex;align-items:center;gap:.5rem;white-space:nowrap}.moderator-stats{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:1rem}.stat-card{display:flex;align-items:center;gap:.9rem;padding:1.15rem}.stat-card>div{display:grid;gap:.15rem}.stat-card small{color:var(--text-secondary)}.stat-card strong{font-size:1.55rem}.stat-icon{width:44px;height:44px;display:grid;place-items:center;border-radius:12px}.stat-icon.pending{color:#f59e0b;background:#f59e0b1c}.stat-icon.active{color:#38bdf8;background:#38bdf81c}.stat-icon.done{color:#10b981;background:#10b9811c}.stat-icon.total{color:#a78bfa;background:#a78bfa1c}.moderator-workspace{min-width:0;overflow:hidden}.moderator-tabs{display:flex;gap:.3rem;padding:.7rem;border-bottom:1px solid rgba(255,255,255,.08)}.moderator-tabs button{display:flex;align-items:center;gap:.45rem;padding:.72rem .9rem;border:0;border-radius:8px;background:transparent;color:var(--text-secondary);font-weight:650}.moderator-tabs button.active{color:var(--accent-blue);background:rgba(0,180,216,.12)}.moderator-toolbar{display:flex;align-items:center;gap:.75rem;padding:1rem;border-bottom:1px solid rgba(255,255,255,.07)}.moderator-search{flex:1;display:flex;align-items:center;gap:.55rem;padding:.65rem .8rem;border:1px solid rgba(255,255,255,.1);border-radius:8px;background:rgba(255,255,255,.035)}.moderator-search input{width:100%;border:0;outline:0;background:transparent;color:inherit}.moderator-toolbar select{padding:.65rem .8rem;border:1px solid rgba(255,255,255,.1);border-radius:8px;background:var(--bg-secondary);color:inherit}.result-count{color:var(--text-secondary);font-size:.84rem;white-space:nowrap}.moderator-alert{display:flex;gap:.55rem;margin:1rem;padding:.8rem;color:#fca5a5;background:rgba(239,68,68,.1);border:1px solid rgba(239,68,68,.25);border-radius:8px}.moderator-state{min-height:270px;display:grid;place-content:center;justify-items:center;gap:.65rem;color:var(--text-secondary)}.moderator-state strong{color:var(--text-primary)}.moderator-table-wrap{overflow:auto}.moderator-table{width:100%;border-collapse:collapse;min-width:850px}.moderator-table th{text-align:left;padding:.8rem 1rem;color:var(--text-muted);font-size:.72rem;text-transform:uppercase;letter-spacing:.08em;background:rgba(255,255,255,.025)}.moderator-table td{padding:.9rem 1rem;border-top:1px solid rgba(255,255,255,.06);vertical-align:middle}.moderator-table tbody tr:hover{background:rgba(255,255,255,.025)}.primary-cell{display:flex;align-items:center;gap:.75rem;min-width:260px}.primary-cell>div{display:grid;gap:.25rem}.primary-cell small,.block{display:block;color:var(--text-secondary);max-width:420px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.file-mark{width:42px;height:42px;display:grid;place-items:center;flex:0 0 auto;border-radius:10px;background:rgba(157,78,221,.13);color:var(--accent-purple);font-size:.7rem;font-weight:800}.file-mark.report{color:#f59e0b;background:#f59e0b16}.file-mark.appeal{color:#38bdf8;background:#38bdf816}.file-mark.history{color:#94a3b8;background:#94a3b816}.moderation-badge{display:inline-flex;padding:.3rem .55rem;border-radius:999px;font-size:.68rem;font-weight:800;white-space:nowrap;color:#94a3b8;background:#94a3b818}.moderation-badge.PENDING,.moderation-badge.PENDING_REVIEW{color:#fbbf24;background:#f59e0b1a}.moderation-badge.IN_REVIEW,.moderation-badge.APPEALED{color:#38bdf8;background:#38bdf81a}.moderation-badge.APPROVED,.moderation-badge.RESTORED,.moderation-badge.DISMISSED{color:#34d399;background:#10b9811a}.moderation-badge.REJECTED,.moderation-badge.ACTIONED,.moderation-badge.HIDDEN,.moderation-badge.UPHELD{color:#f87171;background:#ef44441a}.moderation-badge.NEEDS_CHANGES{color:#c084fc;background:#a855f71a}.actions-col{text-align:right!important}.row-actions{display:flex;justify-content:flex-end;gap:.35rem}.row-actions button,.row-actions a{display:inline-flex;align-items:center;justify-content:center;gap:.35rem;min-height:34px;padding:.45rem .6rem;border:1px solid rgba(255,255,255,.1);border-radius:7px;background:rgba(255,255,255,.035);color:var(--text-secondary);font-size:.76rem}.row-actions button svg{width:16px}.row-actions .success{color:#34d399}.row-actions .warning{color:#fbbf24}.row-actions .danger{color:#f87171}.history-note{display:block;max-width:260px;color:var(--text-secondary);font-size:.82rem}.modal-overlay{position:fixed;inset:0;z-index:2000;display:grid;place-items:center;padding:1rem;background:rgba(3,7,18,.8);backdrop-filter:blur(8px)}.moderator-modal{width:min(680px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:1.4rem;border-radius:var(--radius-md)}.loading-modal{width:auto;display:flex;align-items:center;gap:.7rem}.modal-title-row{display:flex;justify-content:space-between;gap:1rem;padding-bottom:1rem;border-bottom:1px solid rgba(255,255,255,.08)}.modal-title-row h3{margin:0 0 .25rem}.modal-title-row p{color:var(--text-secondary)}.modal-title-row button{border:0;background:transparent;color:var(--text-secondary)}
.btn-review-row{display:inline-flex;align-items:center;gap:.4rem;padding:.45rem .8rem;border:1px solid rgba(0,180,216,.35);border-radius:7px;background:rgba(0,180,216,.12);color:var(--accent-blue);font-size:.78rem;font-weight:650;cursor:pointer;transition:all .2s ease}.btn-review-row:hover{background:rgba(0,180,216,.22);border-color:var(--accent-blue)}
.detail-split-modal{width:min(1100px,calc(100vw - 2rem));max-height:calc(100vh - 2.5rem);padding:1.4rem;display:flex;flex-direction:column;gap:.9rem;overflow:hidden}
.split-modal-header{border-bottom:1px solid rgba(255,255,255,.08);padding-bottom:.4rem}
.detail-split-body{display:flex;gap:0;min-height:480px;max-height:calc(100vh - 12rem);align-items:stretch;overflow:hidden;position:relative}
.detail-split-body.is-resizing{user-select:none;cursor:col-resize}
.split-left-content{display:flex;flex-direction:column;gap:1rem;min-width:30%;max-width:70%;overflow-y:auto;padding-right:.75rem;box-sizing:border-box}
.detail-summary-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:.65rem}
.detail-summary-grid>div{padding:.65rem .75rem;border-radius:8px;background:rgba(255,255,255,.025);border:1px solid rgba(255,255,255,.07);display:grid;gap:.2rem}
.detail-summary-grid small{color:var(--text-muted);font-size:.72rem}
.detail-summary-grid strong{font-size:.88rem}
.report-detail-box{padding:.85rem;border-radius:8px;background:rgba(245,158,11,.08);border:1px solid rgba(245,158,11,.25);display:grid;gap:.4rem;font-size:.84rem}
.report-detail-box small{color:#fbbf24;font-weight:700;font-size:.72rem;letter-spacing:.06em}
.preview-box-enhanced{border-radius:9px;background:rgba(255,255,255,.025);border:1px solid rgba(255,255,255,.07);display:flex;flex-direction:column;flex:1;min-height:380px}
.preview-box-enhanced .preview-box-header{padding:.75rem .9rem;border-bottom:1px solid rgba(255,255,255,.06);display:flex;align-items:center;justify-content:space-between;gap:.75rem}
.preview-full-link{display:inline-flex;align-items:center;gap:.35rem;color:var(--accent-blue);font-size:.78rem;font-weight:650;white-space:nowrap}.preview-full-link:hover{text-decoration:underline}
.original-file-preview-container{min-height:320px;overflow:visible;padding:0;background:rgba(0,0,0,.15);border-radius:0 0 9px 9px}
.original-preview-state{display:grid;place-content:center;justify-items:center;gap:.6rem;padding:2.5rem 1rem;color:var(--text-secondary);text-align:center}
.original-preview-pdf{display:flex;flex-direction:column;align-items:center;gap:.75rem;width:100%}
.original-preview-pager{display:flex;align-items:center;gap:.75rem;color:var(--text-secondary);font-size:.82rem;padding:.4rem .8rem;background:rgba(255,255,255,.05);border-radius:20px}
.original-preview-pager button{border:1px solid rgba(255,255,255,.1);background:rgba(255,255,255,.05);color:var(--text-primary);border-radius:6px;padding:.3rem .45rem;cursor:pointer;display:flex}
.original-preview-pager button:disabled{opacity:.35;cursor:not-allowed}
.original-preview-docx{width:100%;background:#fff;color:#111;padding:1.5rem;border-radius:6px;overflow-x:auto;overflow-y:visible}
.original-preview-xlsx{width:100%;overflow-x:auto;overflow-y:visible;background:#fff;color:#111;border-radius:6px;padding:1rem}
.original-preview-xlsx table{border-collapse:collapse;font-size:.82rem;width:100%}
.original-preview-xlsx td,.original-preview-xlsx th{border:1px solid #ddd;padding:.35rem .5rem}
.split-resizer{width:16px;margin:0 -4px;cursor:col-resize;display:flex;align-items:center;justify-content:center;z-index:10;flex-shrink:0;user-select:none;transition:all .15s ease}
.resizer-handle{width:4px;height:52px;border-radius:4px;background:rgba(255,255,255,.18);transition:all .2s ease}
.split-resizer:hover .resizer-handle,.split-resizer.active .resizer-handle{background:var(--accent-blue);height:80px;box-shadow:0 0 10px rgba(0,180,216,.6)}
.split-right-actions{display:flex;flex-direction:column;gap:1rem;min-width:30%;max-width:70%;overflow-y:auto;padding:1.1rem;padding-left:.85rem;border-radius:10px;background:rgba(255,255,255,.025);border:1px solid rgba(255,255,255,.08);box-sizing:border-box}
.side-panel-header h4{margin:0 0 .2rem;font-size:1.05rem}.side-panel-header small{color:var(--text-secondary);font-size:.78rem}
.decision-selector{display:grid;gap:.55rem}
.decision-option-btn{display:flex;align-items:center;gap:.75rem;padding:.75rem .9rem;border-radius:8px;border:1px solid rgba(255,255,255,.09);background:rgba(255,255,255,.03);color:var(--text-secondary);text-align:left;cursor:pointer;transition:all .18s ease}
.decision-option-btn div{display:grid;gap:.1rem}.decision-option-btn strong{font-size:.86rem;color:var(--text-primary)}.decision-option-btn small{font-size:.73rem;color:var(--text-muted)}
.decision-option-btn:hover{background:rgba(255,255,255,.06)}
.decision-option-btn.approve.active{border-color:#10b981;background:rgba(16,185,129,.12);color:#34d399}
.decision-option-btn.request-changes.active{border-color:#f59e0b;background:rgba(245,158,11,.12);color:#fbbf24}
.decision-option-btn.reject.active{border-color:#ef4444;background:rgba(239,68,68,.12);color:#f87171}
.comment-note-box{display:grid;gap:.45rem}
.comment-label{font-size:.8rem;font-weight:650;color:var(--text-secondary);display:flex;align-items:center;gap:.25rem}
.required-star{color:#f87171}
.comment-textarea{width:100%;resize:vertical;min-height:90px;max-height:200px;padding:.75rem .85rem;border-radius:8px;border:1px solid rgba(255,255,255,.12);background:rgba(0,0,0,.2);color:inherit;font-size:.84rem;outline:none}
.comment-textarea:focus{border-color:var(--accent-blue)}
.decision-error-text{color:#f87171;font-size:.78rem}
.side-panel-footer{display:flex;justify-content:flex-end;gap:.6rem;margin-top:.4rem}
.submit-decision-btn{display:inline-flex;align-items:center;gap:.4rem;padding:.65rem 1.1rem;font-weight:650}
.btn-success{background:#10b981!important;color:#fff!important;border:none!important}
.btn-warning{background:#f59e0b!important;color:#fff!important;border:none!important}
@media(max-width:900px){.detail-split-body{flex-direction:column;max-height:none}.split-left-content,.split-right-actions{width:100%!important;min-width:100%;max-width:100%}.split-resizer{display:none}.detail-summary-grid{grid-template-columns:repeat(2,1fr)}.moderator-stats{grid-template-columns:repeat(2,1fr)}}
@media(max-width:700px){.moderator-header{align-items:flex-start;flex-direction:column}.moderator-stats{grid-template-columns:1fr}.moderator-tabs{overflow:auto}.moderator-tabs button{white-space:nowrap}.moderator-toolbar{align-items:stretch;flex-direction:column}.detail-summary-grid{grid-template-columns:1fr}}
`;
