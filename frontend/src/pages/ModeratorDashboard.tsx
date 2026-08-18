import React, { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../services/api';
import { formatDateTime } from '../utils/dateTime';
import {
  AlertCircle,
  CheckCircle2,
  ChevronRight,
  Clock3,
  ExternalLink,
  FileCheck2,
  FileWarning,
  History,
  Loader,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldCheck,
  X,
  XCircle,
} from 'lucide-react';

type Tab = 'queue' | 'reports' | 'appeals' | 'history';
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

export const ModeratorDashboard: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedTab = searchParams.get('tab');
  const [tab, setTab] = useState<Tab>(
    requestedTab === 'reports' || requestedTab === 'appeals' || requestedTab === 'history'
      ? requestedTab
      : 'queue',
  );
  const [items, setItems] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('ALL');
  const [detail, setDetail] = useState<any>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [decisionError, setDecisionError] = useState('');
  const [sortKey, setSortKey] = useState('time');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const [summary, setSummary] = useState({
    pendingDocuments: 0,
    pendingReports: 0,
    pendingAppeals: 0,
    completed: 0,
  });
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
          : api.moderation.getHistory();
  const load = async (nextTab = tab) => {
    setLoading(true);
    setError('');
    try {
      const [nextItems, nextSummary] = await Promise.all([
        request(nextTab),
        api.moderation.getSummary(),
      ]);
      setItems(nextItems);
      setSummary(nextSummary);
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
            sortKey === 'content'
              ? item.title || item.documentTitle || item.actorName || ''
              : sortKey === 'time'
                ? item.moderationSubmittedAt || item.createdAt || ''
                : sortKey === 'status'
                  ? item.moderationStatus || item.status || item.newStatus || ''
                  : (item[sortKey] ?? '');
          const a = value(left),
            b = value(right);
          const result =
            typeof a === 'number' && typeof b === 'number'
              ? a - b
              : String(a).localeCompare(String(b), 'vi');
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
    try {
      const value = await api.moderation.getDocument(id);
      setDetail({ ...value, report });
    } catch (e: any) {
      setError(e.message || 'Không thể mở tài liệu.');
    } finally {
      setDetailLoading(false);
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

  const tabs: [Tab, string, React.ReactNode][] = [
    ['queue', 'Chờ xét duyệt', <FileCheck2 key="queue" size={17} />],
    ['reports', 'Báo cáo nội dung', <FileWarning key="reports" size={17} />],
    ['appeals', 'Giải trình', <RotateCcw key="appeals" size={17} />],
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
              placeholder="Tìm tài liệu, người gửi, lý do..."
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
                <tr>
                  <th>{sortHeader('content', 'Nội dung')}</th>
                  <th>Phân loại</th>
                  <th>{sortHeader('status', 'Trạng thái')}</th>
                  <th>{sortHeader('time', 'Thời gian')}</th>
                  <th className="actions-col">Thao tác</th>
                </tr>
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
                            title="Phê duyệt"
                            className="success"
                            onClick={() =>
                              openDecision({
                                kind: 'document',
                                id: x.documentId,
                                action: 'approve',
                                title: x.title,
                                noteRequired: false,
                              })
                            }
                          >
                            <CheckCircle2 />
                          </button>
                          <button
                            title="Yêu cầu chỉnh sửa"
                            className="warning"
                            onClick={() =>
                              openDecision({
                                kind: 'document',
                                id: x.documentId,
                                action: 'request-changes',
                                title: x.title,
                                noteRequired: true,
                              })
                            }
                          >
                            <RefreshCw />
                          </button>
                          <button
                            title="Từ chối"
                            className="danger"
                            onClick={() =>
                              openDecision({
                                kind: 'document',
                                id: x.documentId,
                                action: 'reject',
                                title: x.title,
                                noteRequired: true,
                              })
                            }
                          >
                            <XCircle />
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
                            <>
                              <button
                                onClick={() =>
                                  openDecision({
                                    kind: 'report',
                                    id: x.reportId,
                                    action: 'no-violation',
                                    title: x.documentTitle,
                                    noteRequired: true,
                                  })
                                }
                              >
                                Bác báo cáo
                              </button>
                              <button
                                className="warning"
                                onClick={() =>
                                  openDecision({
                                    kind: 'report',
                                    id: x.reportId,
                                    action: 'temporarily-hide',
                                    title: x.documentTitle,
                                    noteRequired: true,
                                  })
                                }
                              >
                                Ẩn tạm thời
                              </button>
                              <button
                                className="danger"
                                onClick={() =>
                                  openDecision({
                                    kind: 'report',
                                    id: x.reportId,
                                    action: 'confirm-violation',
                                    title: x.documentTitle,
                                    noteRequired: true,
                                  })
                                }
                              >
                                Xác nhận vi phạm
                              </button>
                            </>
                          )}
                          {x.status === 'RESTRICTED' && (
                            <>
                              <button
                                className="success"
                                onClick={() =>
                                  openDecision({
                                    kind: 'report',
                                    id: x.reportId,
                                    action: 'no-violation',
                                    title: x.documentTitle,
                                    noteRequired: true,
                                  })
                                }
                              >
                                Khôi phục & bác báo cáo
                              </button>
                              <button
                                className="danger"
                                onClick={() =>
                                  openDecision({
                                    kind: 'report',
                                    id: x.reportId,
                                    action: 'confirm-violation',
                                    title: x.documentTitle,
                                    noteRequired: true,
                                  })
                                }
                              >
                                Xác nhận vi phạm
                              </button>
                            </>
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
                            <strong>{x.title}</strong>
                            <small>{x.explanation}</small>
                          </div>
                        </div>
                      </td>
                      <td>{x.reportType}</td>
                      <td>
                        <Badge value={x.status} />
                      </td>
                      <td>{formatDate(x.createdAt)}</td>
                      <td>
                        <div className="row-actions textual">
                          {x.evidenceUrl && (
                            <a href={x.evidenceUrl} target="_blank" rel="noreferrer">
                              Bằng chứng
                            </a>
                          )}
                          {x.status === 'PENDING' && (
                            <>
                              <button
                                className="success"
                                onClick={() =>
                                  openDecision({
                                    kind: 'appeal',
                                    id: x.appealId,
                                    action: 'restore',
                                    title: x.title,
                                    noteRequired: false,
                                  })
                                }
                              >
                                Khôi phục
                              </button>
                              <button
                                className="danger"
                                onClick={() =>
                                  openDecision({
                                    kind: 'appeal',
                                    id: x.appealId,
                                    action: 'uphold',
                                    title: x.title,
                                    noteRequired: false,
                                  })
                                }
                              >
                                Giữ quyết định
                              </button>
                            </>
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
                            <strong>{x.action}</strong>
                            <small>
                              {x.actorName} · Tài liệu #{x.documentId || '—'}
                            </small>
                          </div>
                        </div>
                      </td>
                      <td>
                        {x.previousStatus || '—'} <ChevronRight size={13} /> {x.newStatus || '—'}
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
        <div className="modal-overlay" onMouseDown={() => setDetail(null)}>
          <div
            className="moderator-modal detail-modal glass-panel animate-slide-up"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <ModalTitle
              title={detail.document.title + '.' + detail.document.fileExtension}
              subtitle={`Đăng bởi ${detail.document.uploaderName}`}
              close={() => setDetail(null)}
            />
            <div className="detail-summary">
              <div>
                <small>Trạng thái</small>
                <Badge value={detail.report?.status || detail.document.moderationStatus} />
              </div>
              <div>
                <small>Môn học</small>
                <strong>{detail.document.subject || 'Khác'}</strong>
              </div>
              <div>
                <small>Kích thước</small>
                <strong>{Number(detail.document.fileSizeMb || 0).toFixed(2)} MB</strong>
              </div>
              <div>
                <small>Độ phủ trích xuất</small>
                <strong>
                  {typeof detail.document.extractionCoveragePercent === 'number'
                    ? `${Math.round(detail.document.extractionCoveragePercent * 100)}%`
                    : 'N/A'}
                </strong>
              </div>
            </div>
            {detail.report && (
              <section className="report-detail">
                <small>CHI TIẾT BÁO CÁO</small>
                <p>
                  <strong>{detail.report.reasonCode}</strong> · {detail.report.reportType}
                </p>
                <p>
                  {detail.report.additionalDetails ||
                    detail.report.evidenceDescription ||
                    'Không có mô tả bổ sung.'}
                </p>
                <span>Người báo cáo: {detail.report.reporterName}</span>
              </section>
            )}
            <section className="preview-box">
              <div className="preview-box-header">
                <small>NỘI DUNG TRÍCH XUẤT</small>
                <a
                  href={`/document/${detail.document.documentId}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="preview-full-link"
                >
                  Xem bản gốc & OCR đầy đủ <ExternalLink size={13} />
                </a>
              </div>
              <p>{detail.description}</p>
            </section>
            {!detail.report && (
              <div className="modal-actions">
                <button
                  className="btn-secondary"
                  onClick={() =>
                    openDecision({
                      kind: 'document',
                      id: detail.document.documentId,
                      action: 'request-changes',
                      title: detail.document.title,
                      noteRequired: true,
                    })
                  }
                >
                  Yêu cầu chỉnh sửa
                </button>
                <button
                  className="btn-secondary danger"
                  onClick={() =>
                    openDecision({
                      kind: 'document',
                      id: detail.document.documentId,
                      action: 'reject',
                      title: detail.document.title,
                      noteRequired: true,
                    })
                  }
                >
                  Từ chối
                </button>
                <button
                  className="btn-primary"
                  onClick={() =>
                    openDecision({
                      kind: 'document',
                      id: detail.document.documentId,
                      action: 'approve',
                      title: detail.document.title,
                      noteRequired: false,
                    })
                  }
                >
                  Phê duyệt
                </button>
              </div>
            )}
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
.moderator-page{display:grid;gap:1.4rem;min-width:0}.moderator-header{display:flex;align-items:flex-end;justify-content:space-between;gap:1.5rem}.moderator-header h1{margin:.35rem 0;font-size:2rem}.moderator-header p{color:var(--text-secondary);max-width:720px}.eyebrow{display:flex;align-items:center;gap:.45rem;color:var(--accent-blue);font-size:.76rem;font-weight:800;letter-spacing:.12em}.refresh-btn{display:flex;align-items:center;gap:.5rem;white-space:nowrap}.moderator-stats{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:1rem}.stat-card{display:flex;align-items:center;gap:.9rem;padding:1.15rem}.stat-card>div{display:grid;gap:.15rem}.stat-card small{color:var(--text-secondary)}.stat-card strong{font-size:1.55rem}.stat-icon{width:44px;height:44px;display:grid;place-items:center;border-radius:12px}.stat-icon.pending{color:#f59e0b;background:#f59e0b1c}.stat-icon.active{color:#38bdf8;background:#38bdf81c}.stat-icon.done{color:#10b981;background:#10b9811c}.stat-icon.total{color:#a78bfa;background:#a78bfa1c}.moderator-workspace{min-width:0;overflow:hidden}.moderator-tabs{display:flex;gap:.3rem;padding:.7rem;border-bottom:1px solid rgba(255,255,255,.08)}.moderator-tabs button{display:flex;align-items:center;gap:.45rem;padding:.72rem .9rem;border:0;border-radius:8px;background:transparent;color:var(--text-secondary);font-weight:650}.moderator-tabs button.active{color:var(--accent-blue);background:rgba(0,180,216,.12)}.moderator-toolbar{display:flex;align-items:center;gap:.75rem;padding:1rem;border-bottom:1px solid rgba(255,255,255,.07)}.moderator-search{flex:1;display:flex;align-items:center;gap:.55rem;padding:.65rem .8rem;border:1px solid rgba(255,255,255,.1);border-radius:8px;background:rgba(255,255,255,.035)}.moderator-search input{width:100%;border:0;outline:0;background:transparent;color:inherit}.moderator-toolbar select{padding:.65rem .8rem;border:1px solid rgba(255,255,255,.1);border-radius:8px;background:var(--bg-secondary);color:inherit}.result-count{color:var(--text-secondary);font-size:.84rem;white-space:nowrap}.moderator-alert{display:flex;gap:.55rem;margin:1rem;padding:.8rem;color:#fca5a5;background:rgba(239,68,68,.1);border:1px solid rgba(239,68,68,.25);border-radius:8px}.moderator-state{min-height:270px;display:grid;place-content:center;justify-items:center;gap:.65rem;color:var(--text-secondary)}.moderator-state strong{color:var(--text-primary)}.moderator-table-wrap{overflow:auto}.moderator-table{width:100%;border-collapse:collapse;min-width:850px}.moderator-table th{text-align:left;padding:.8rem 1rem;color:var(--text-muted);font-size:.72rem;text-transform:uppercase;letter-spacing:.08em;background:rgba(255,255,255,.025)}.moderator-table td{padding:.9rem 1rem;border-top:1px solid rgba(255,255,255,.06);vertical-align:middle}.moderator-table tbody tr:hover{background:rgba(255,255,255,.025)}.primary-cell{display:flex;align-items:center;gap:.75rem;min-width:260px}.primary-cell>div{display:grid;gap:.25rem}.primary-cell small,.block{display:block;color:var(--text-secondary);max-width:420px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.file-mark{width:42px;height:42px;display:grid;place-items:center;flex:0 0 auto;border-radius:10px;background:rgba(157,78,221,.13);color:var(--accent-purple);font-size:.7rem;font-weight:800}.file-mark.report{color:#f59e0b;background:#f59e0b16}.file-mark.appeal{color:#38bdf8;background:#38bdf816}.file-mark.history{color:#94a3b8;background:#94a3b816}.moderation-badge{display:inline-flex;padding:.3rem .55rem;border-radius:999px;font-size:.68rem;font-weight:800;white-space:nowrap;color:#94a3b8;background:#94a3b818}.moderation-badge.PENDING,.moderation-badge.PENDING_REVIEW{color:#fbbf24;background:#f59e0b1a}.moderation-badge.IN_REVIEW,.moderation-badge.APPEALED{color:#38bdf8;background:#38bdf81a}.moderation-badge.APPROVED,.moderation-badge.RESTORED,.moderation-badge.DISMISSED{color:#34d399;background:#10b9811a}.moderation-badge.REJECTED,.moderation-badge.ACTIONED,.moderation-badge.HIDDEN,.moderation-badge.UPHELD{color:#f87171;background:#ef44441a}.moderation-badge.NEEDS_CHANGES{color:#c084fc;background:#a855f71a}.actions-col{text-align:right!important}.row-actions{display:flex;justify-content:flex-end;gap:.35rem}.row-actions button,.row-actions a{display:inline-flex;align-items:center;justify-content:center;gap:.35rem;min-height:34px;padding:.45rem .6rem;border:1px solid rgba(255,255,255,.1);border-radius:7px;background:rgba(255,255,255,.035);color:var(--text-secondary);font-size:.76rem}.row-actions button svg{width:16px}.row-actions .success{color:#34d399}.row-actions .warning{color:#fbbf24}.row-actions .danger{color:#f87171}.history-note{display:block;max-width:260px;color:var(--text-secondary);font-size:.82rem}.modal-overlay{position:fixed;inset:0;z-index:2000;display:grid;place-items:center;padding:1rem;background:rgba(3,7,18,.8);backdrop-filter:blur(8px)}.moderator-modal{width:min(680px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:1.4rem;border-radius:var(--radius-md)}.loading-modal{width:auto;display:flex;align-items:center;gap:.7rem}.modal-title-row{display:flex;justify-content:space-between;gap:1rem;padding-bottom:1rem;border-bottom:1px solid rgba(255,255,255,.08)}.modal-title-row h3{margin:0 0 .25rem}.modal-title-row p{color:var(--text-secondary)}.modal-title-row button{border:0;background:transparent;color:var(--text-secondary)}.detail-summary{display:grid;grid-template-columns:repeat(4,1fr);gap:.8rem;margin:1rem 0}.detail-summary>div{display:grid;gap:.35rem;padding:.8rem;border:1px solid rgba(255,255,255,.07);border-radius:8px}.detail-summary small,.preview-box small{color:var(--text-muted)}.preview-box{padding:1rem;border-radius:9px;background:rgba(255,255,255,.025);border:1px solid rgba(255,255,255,.07)}.preview-box-header{display:flex;align-items:center;justify-content:space-between;gap:.75rem}.preview-full-link{display:inline-flex;align-items:center;gap:.35rem;color:var(--accent-blue);font-size:.78rem;font-weight:650;white-space:nowrap}.preview-full-link:hover{text-decoration:underline}.preview-box p{margin-top:.7rem;white-space:pre-wrap;line-height:1.65;color:var(--text-secondary)}.modal-actions{display:flex;justify-content:flex-end;gap:.6rem;margin-top:1.2rem}.btn-danger{display:flex;align-items:center;gap:.4rem;padding:.7rem 1rem;border:1px solid rgba(239,68,68,.4);border-radius:8px;background:rgba(239,68,68,.15);color:#fca5a5}.decision-banner{padding:.9rem 1rem;margin:1rem 0;border-radius:9px}.decision-banner p{margin:.3rem 0 0;color:var(--text-secondary)}.decision-banner.safe{background:rgba(16,185,129,.08);border:1px solid rgba(16,185,129,.2)}.decision-banner.danger{background:rgba(239,68,68,.08);border:1px solid rgba(239,68,68,.2)}.note-field{display:grid;gap:.5rem}.note-field em{color:#f87171}.note-field textarea{resize:vertical;padding:.8rem;border:1px solid rgba(255,255,255,.12);border-radius:8px;background:rgba(255,255,255,.035);color:inherit;outline:none}.note-field textarea:focus{border-color:var(--accent-blue)}@media(max-width:1000px){.moderator-stats{grid-template-columns:repeat(2,1fr)}}@media(max-width:700px){.moderator-header{align-items:flex-start;flex-direction:column}.moderator-stats{grid-template-columns:1fr}.moderator-tabs{overflow:auto}.moderator-tabs button{white-space:nowrap}.moderator-toolbar{align-items:stretch;flex-direction:column}.detail-summary{grid-template-columns:1fr}.modal-actions{flex-wrap:wrap}.modal-actions button{flex:1}}`;
