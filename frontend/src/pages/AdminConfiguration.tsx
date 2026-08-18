import React, { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { NavLink } from 'react-router-dom';
import { formatDateTime } from '../utils/dateTime';
import {
  Bookmark,
  CalendarDays,
  Download,
  Eye,
  FileText,
  HardDrive,
  UserRound,
  X,
} from 'lucide-react';
import { api } from '../services/api';
import { useUiFeedback } from '../context/UiFeedbackContext';

type AdminConfigTab = 'documents' | 'report-config' | 'system-config' | 'transfer-config';

export const AdminConfiguration: React.FC<{ tab: AdminConfigTab }> = ({ tab }) => {
  const { confirm, notify } = useUiFeedback();
  const [items, setItems] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('ALL');
  const [sortKey, setSortKey] = useState('title');
  const [direction, setDirection] = useState<'asc' | 'desc'>('asc');
  const [detail, setDetail] = useState<any | null>(null);
  
  // Pagination states
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 8;

  const [newReason, setNewReason] = useState({
    reasonCode: '',
    severityLevel: 'MEDIUM',
    baseScore: 1,
    autoFlagThreshold: 3,
    description: '',
  });
  const toggleSort = (key: string) => {
    if (sortKey === key) setDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
    else {
      setSortKey(key);
      setDirection('asc');
    }
  };
  const sortHeader = (key: string, label: string) => (
    <button
      className={`sortable-header ${sortKey === key ? 'active' : ''}`}
      onClick={() => toggleSort(key)}
      aria-label={`Sắp xếp theo ${label}`}
    >
      {label}
      <span aria-hidden="true">{sortKey === key ? (direction === 'asc' ? ' ↑' : ' ↓') : ' ↕'}</span>
    </button>
  );

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      if (tab === 'documents') {
        const data = await api.admin.getDocuments(page, pageSize, query, status);
        setItems(data.items);
        setTotalCount(data.totalCount);
      } else {
        const data =
          tab === 'report-config'
            ? await api.admin.getReportReasons()
            : tab === 'transfer-config'
              ? await api.admin.getTransferConfig()
              : await api.admin.getSubscriptions();
        setItems(tab === 'transfer-config' ? [data] : data);
      }
    } catch (err: any) {
      setError(err.message || 'Không thể tải dữ liệu.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setPage(1);
    setQuery('');
    setStatus('ALL');
    setDirection('asc');
    setSortKey(tab === 'documents' ? 'title' : tab === 'report-config' ? 'reasonCode' : 'tierName');
  }, [tab]);

  useEffect(() => {
    load();
  }, [tab, page, query, status]);

  const visible = useMemo(
    () => {
      if (tab === 'documents') {
        return items.map((item, index) => ({ ...item, __index: index }));
      }
      return items
        .map((item, index) => ({ ...item, __index: index }))
        .filter((item) => {
          const matchesQuery =
            !query.trim() ||
            JSON.stringify(item)
              .toLocaleLowerCase('vi')
              .includes(query.trim().toLocaleLowerCase('vi'));
          return (
            matchesQuery &&
            (tab !== 'documents' || status === 'ALL' || item.sharingPermission === status)
          );
        })
        .sort((a, b) => {
          const av = a[sortKey] ?? '',
            bv = b[sortKey] ?? '';
          const result =
            typeof av === 'number' && typeof bv === 'number'
              ? av - bv
              : String(av).localeCompare(String(bv), 'vi');
          return direction === 'asc' ? result : -result;
        });
    },
    [items, query, status, sortKey, direction, tab],
  );

  const change = (index: number, key: string, value: string | number | boolean) =>
    setItems((current) =>
      current.map((item, position) => (position === index ? { ...item, [key]: value } : item)),
    );
  const updateDocument = async (doc: any) => {
    if (doc.sharingPermission === 'PRIVATE') return;
    await api.admin.updateDocumentVisibility(doc.documentId, 'PRIVATE');
    await load();
  };
  const deleteDocument = async (id: number) => {
    if (
      await confirm({
        title: 'Xóa tài liệu',
        message: 'Xóa vĩnh viễn tài liệu này?',
        confirmLabel: 'Xóa tài liệu',
        danger: true,
      })
    ) {
      await api.admin.deleteDocument(id);
      await load();
      notify('Đã xóa tài liệu.', 'success');
    }
  };
  const addReason = async (event: React.FormEvent) => {
    event.preventDefault();
    await api.admin.createReportReason(newReason);
    setNewReason({
      reasonCode: '',
      severityLevel: 'MEDIUM',
      baseScore: 1,
      autoFlagThreshold: 3,
      description: '',
    });
    await load();
  };
  const removeReason = async (code: string) => {
    if (
      await confirm({
        title: 'Xóa lý do báo cáo',
        message: `Xóa lý do ${code}?`,
        confirmLabel: 'Xóa',
        danger: true,
      })
    ) {
      await api.admin.deleteReportReason(code);
      await load();
      notify(`Đã xóa lý do ${code}.`, 'success');
    }
  };

  if (loading) return <div className="admin-config-state">Đang tải dữ liệu...</div>;
  if (error) return <div className="error-alert">{error}</div>;

  if (tab === 'transfer-config') {
    const config = items[0] || {};
    const previewUrl =
      config.bankCode && config.accountNumber
        ? `https://img.vietqr.io/image/${encodeURIComponent(config.bankCode)}-${encodeURIComponent(config.accountNumber)}-${encodeURIComponent(config.qrTemplate || 'compact2')}.png?amount=100000&addInfo=${encodeURIComponent((config.transferContentPrefix || 'AIStudyHub') + ' demo')}&accountName=${encodeURIComponent(config.accountName || '')}`
        : '';
    return (
      <div className="admin-config-page">
        <div className="admin-section-tabs">
          <NavLink className={() => ''} to="/admin?tab=report-config">
            Quy tắc báo cáo
          </NavLink>
          <NavLink className={() => ''} to="/admin?tab=system-config">
            Gói dịch vụ
          </NavLink>
          <NavLink className="active" to="/admin?tab=transfer-config">
            Chuyển khoản
          </NavLink>
        </div>
        <h3>Cấu hình chuyển khoản và mã QR</h3>
        <div className="transfer-config-layout">
          <form
            className="transfer-config-form glass-card"
            onSubmit={async (event) => {
              event.preventDefault();
              await api.admin.updateTransferConfig(config);
              await load();
            }}
          >
            <label>
              Mã ngân hàng (VietQR)
              <input
                className="input-control"
                placeholder="VD: MB, VCB, ACB"
                value={config.bankCode || ''}
                onChange={(e) => change(0, 'bankCode', e.target.value.toUpperCase())}
              />
            </label>
            <label>
              Tên ngân hàng
              <input
                className="input-control"
                placeholder="VD: MB Bank"
                value={config.bankName || ''}
                onChange={(e) => change(0, 'bankName', e.target.value)}
              />
            </label>
            <label>
              Số tài khoản
              <input
                className="input-control"
                value={config.accountNumber || ''}
                onChange={(e) => change(0, 'accountNumber', e.target.value)}
              />
            </label>
            <label>
              Chủ tài khoản
              <input
                className="input-control"
                value={config.accountName || ''}
                onChange={(e) => change(0, 'accountName', e.target.value.toUpperCase())}
              />
            </label>
            <label>
              Tiền tố nội dung
              <input
                className="input-control"
                value={config.transferContentPrefix || ''}
                onChange={(e) => change(0, 'transferContentPrefix', e.target.value)}
              />
            </label>
            <label>
              Mẫu QR
              <select
                className="input-control"
                value={config.qrTemplate || 'compact2'}
                onChange={(e) => change(0, 'qrTemplate', e.target.value)}
              >
                <option value="compact2">Compact 2</option>
                <option value="compact">Compact</option>
                <option value="qr_only">Chỉ QR</option>
                <option value="print">Print</option>
              </select>
            </label>
            <label className="active-toggle">
              <input
                type="checkbox"
                checked={Boolean(config.isActive)}
                onChange={(e) => change(0, 'isActive', e.target.checked)}
              />
              <span>Bật chuyển khoản cho người dùng</span>
            </label>
            <button className="btn-primary">Lưu cấu hình chuyển khoản</button>
          </form>
          <div className="transfer-preview glass-card">
            <h4>Xem trước QR 100.000đ</h4>
            {previewUrl ? (
              <img src={previewUrl} alt="Xem trước mã QR chuyển khoản" />
            ) : (
              <p>Nhập mã ngân hàng và số tài khoản để xem trước.</p>
            )}
            <small>QR thực tế sẽ tự điền đúng mệnh giá người dùng chọn.</small>
          </div>
        </div>
        <style>{`.admin-config-page{display:flex;flex-direction:column;gap:1.2rem;min-width:0}.admin-section-tabs{display:flex;gap:.5rem;border-bottom:1px solid rgba(255,255,255,.08);overflow-x:auto}.admin-section-tabs a{padding:.7rem 1rem;color:var(--text-muted);white-space:nowrap}.admin-section-tabs a.active{color:var(--text-primary);border-bottom:2px solid var(--accent-purple)}.transfer-config-layout{display:grid;grid-template-columns:minmax(0,1.25fr) minmax(0,.75fr);gap:1rem;min-width:0}.transfer-config-form{min-width:0;padding:1.2rem;display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:1rem}.transfer-config-form label{min-width:0;display:flex;flex-direction:column;gap:.4rem;color:var(--text-secondary);font-size:.85rem}.transfer-config-form .input-control{width:100%;min-width:0}.transfer-config-form .active-toggle{grid-column:1/-1;flex-direction:row;align-items:center}.transfer-config-form button{grid-column:1/-1}.transfer-preview{min-width:0;padding:1.2rem;display:flex;flex-direction:column;align-items:center;gap:1rem;text-align:center;overflow:hidden}.transfer-preview img{width:min(280px,100%);border-radius:12px;background:white}.transfer-preview p,.transfer-preview small{color:var(--text-muted)}@media(max-width:950px){.transfer-config-layout{grid-template-columns:1fr}}@media(max-width:650px){.transfer-config-form{grid-template-columns:1fr}.transfer-config-form .active-toggle,.transfer-config-form button{grid-column:auto}}`}</style>
      </div>
    );
  }

  const sortOptions =
    tab === 'documents'
      ? [
          ['title', 'Tên'],
          ['uploaderName', 'Người đăng'],
          ['downloadCount', 'Lượt tải'],
          ['viewCount', 'Lượt xem'],
          ['createdAt', 'Ngày tạo'],
        ]
      : tab === 'report-config'
        ? [
            ['reasonCode', 'Mã lý do'],
            ['severityLevel', 'Mức độ'],
            ['baseScore', 'Điểm'],
          ]
        : [
            ['tierName', 'Tên gói'],
            ['price', 'Giá'],
            ['maxStorageMb', 'Dung lượng'],
          ];

  return (
    <div className="admin-config-page">
      <div className="admin-section-tabs">
        {tab === 'documents' ? (
          <>
            <NavLink to="/admin?tab=reports">Báo cáo vi phạm</NavLink>
            <NavLink className="active" to="/admin?tab=documents">
              Tài liệu
            </NavLink>
          </>
        ) : (
          <>
            <NavLink
              className={tab === 'report-config' ? 'active' : ''}
              to="/admin?tab=report-config"
            >
              Quy tắc báo cáo
            </NavLink>
            <NavLink
              className={tab === 'system-config' ? 'active' : ''}
              to="/admin?tab=system-config"
            >
              Gói dịch vụ
            </NavLink>
            <NavLink to="/admin?tab=transfer-config">Chuyển khoản</NavLink>
          </>
        )}
      </div>
      <h3>
        {tab === 'documents'
          ? 'Quản lý tài liệu'
          : tab === 'report-config'
            ? 'Cấu hình quy tắc báo cáo'
            : 'Cấu hình gói dịch vụ'}
      </h3>
      <div className="admin-toolbar">
        <input
          className="input-control"
          placeholder={
            tab === 'documents'
              ? 'Tìm tên tài liệu hoặc người đăng...'
              : tab === 'report-config'
                ? 'Lọc quy tắc báo cáo...'
                : 'Lọc gói dịch vụ...'
          }
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        {tab === 'documents' && (
          <select
            className="input-control"
            value={status}
            onChange={(e) => setStatus(e.target.value)}
          >
            <option value="ALL">Tất cả trạng thái</option>
            <option value="PUBLIC">Công khai</option>
            <option value="PRIVATE">Riêng tư</option>
          </select>
        )}
        {tab !== 'documents' && (
          <>
            <select
              className="input-control"
              aria-label="Sắp xếp theo"
              value={sortKey}
              onChange={(e) => setSortKey(e.target.value)}
            >
              {sortOptions.map(([key, label]) => (
                <option key={key} value={key}>
                  {label}
                </option>
              ))}
            </select>
            <select
              className="input-control"
              aria-label="Chiều sắp xếp"
              value={direction}
              onChange={(e) => setDirection(e.target.value as 'asc' | 'desc')}
            >
              <option value="asc">Tăng dần</option>
              <option value="desc">Giảm dần</option>
            </select>
          </>
        )}
      </div>

      {tab === 'documents' && (
        <>
        <div className="table-scroll">
          <table className="admin-table document-admin-table">
            <thead>
              <tr>
                <th>{sortHeader('documentId', 'ID')}</th>
                <th>{sortHeader('title', 'Tên tài liệu')}</th>
                <th>{sortHeader('uploaderName', 'Người đăng')}</th>
                <th>{sortHeader('createdAt', 'Ngày đăng')}</th>
                <th>{sortHeader('bookmarkCount', 'Lượt lưu')}</th>
                <th>{sortHeader('viewCount', 'Lượt xem')}</th>
                <th>{sortHeader('downloadCount', 'Lượt tải')}</th>
                <th>{sortHeader('sharingPermission', 'Trạng thái')}</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {visible.map((doc) => (
                <tr key={doc.documentId}>
                  <td className="monospace-text">#{doc.documentId}</td>
                  <td>
                    <button
                      className="document-title-button"
                      onClick={() => api.admin.getDocumentDetail(doc.documentId).then(setDetail)}
                    >
                      {doc.title}.{doc.fileExtension}
                    </button>
                  </td>
                  <td>{doc.uploaderName}</td>
                  <td>{formatDateTime(doc.createdAt)}</td>
                  <td>{doc.bookmarkCount ?? 0}</td>
                  <td>{doc.viewCount ?? 0}</td>
                  <td>{doc.downloadCount ?? 0}</td>
                  <td>
                    <span className={`config-status ${doc.sharingPermission}`}>
                      {doc.sharingPermission}
                    </span>
                  </td>
                  <td>
                    <div className="document-row-actions">
                      <button
                        className="btn-secondary"
                        disabled={doc.sharingPermission === 'PRIVATE'}
                        onClick={() => updateDocument(doc)}
                      >
                        {doc.sharingPermission === 'PRIVATE' ? 'Đã riêng tư' : 'Chuyển riêng tư'}
                      </button>
                      <button
                        className="btn-secondary danger"
                        onClick={() => deleteDocument(doc.documentId)}
                      >
                        Xóa
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="admin-pagination">
          <small>{totalCount} kết quả</small>
          <div>
            <button className="btn-secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Trước
            </button>
            <span>
              {page}/{Math.ceil(totalCount / pageSize) || 1}
            </span>
            <button
              className="btn-secondary"
              disabled={page >= Math.ceil(totalCount / pageSize)}
              onClick={() => setPage(page + 1)}
            >
              Sau
            </button>
          </div>
        </div>
        </>
      )}

      {tab === 'report-config' && (
        <>
          <form className="new-reason-form glass-card" onSubmit={addReason}>
            <strong>Thêm lý do</strong>
            <input
              className="input-control"
              required
              placeholder="Mã lý do"
              value={newReason.reasonCode}
              onChange={(e) =>
                setNewReason({ ...newReason, reasonCode: e.target.value.toUpperCase() })
              }
            />
            <select
              className="input-control"
              value={newReason.severityLevel}
              onChange={(e) => setNewReason({ ...newReason, severityLevel: e.target.value })}
            >
              <option>LOW</option>
              <option>MEDIUM</option>
              <option>HIGH</option>
            </select>
            <input
              className="input-control"
              type="number"
              min="0"
              value={newReason.baseScore}
              onChange={(e) => setNewReason({ ...newReason, baseScore: Number(e.target.value) })}
            />
            <input
              className="input-control"
              type="number"
              min="1"
              value={newReason.autoFlagThreshold}
              onChange={(e) =>
                setNewReason({ ...newReason, autoFlagThreshold: Number(e.target.value) })
              }
            />
            <input
              className="input-control"
              placeholder="Mô tả"
              value={newReason.description}
              onChange={(e) => setNewReason({ ...newReason, description: e.target.value })}
            />
            <button className="btn-primary">Thêm</button>
          </form>
          <div className="admin-config-list">
            {visible.map((reason) => (
              <div className="admin-config-form" key={reason.reasonCode}>
                <strong>{reason.reasonCode}</strong>
                <input
                  className="input-control"
                  value={reason.severityLevel}
                  onChange={(e) => change(reason.__index, 'severityLevel', e.target.value)}
                />
                <input
                  className="input-control"
                  type="number"
                  value={reason.baseScore}
                  onChange={(e) => change(reason.__index, 'baseScore', Number(e.target.value))}
                />
                <input
                  className="input-control"
                  type="number"
                  value={reason.autoFlagThreshold}
                  onChange={(e) =>
                    change(reason.__index, 'autoFlagThreshold', Number(e.target.value))
                  }
                />
                <input
                  className="input-control wide"
                  value={reason.description ?? ''}
                  onChange={(e) => change(reason.__index, 'description', e.target.value)}
                />
                <button
                  className="btn-primary"
                  onClick={async () => {
                    await api.admin.updateReportReason(reason.reasonCode, items[reason.__index]);
                    await load();
                  }}
                >
                  Lưu
                </button>
                <button
                  className="btn-secondary danger"
                  onClick={() => removeReason(reason.reasonCode)}
                >
                  Xóa
                </button>
              </div>
            ))}
          </div>
        </>
      )}

      {tab === 'system-config' && (
        <div className="tier-config-grid">
          {visible.map((tier) => (
            <div className="tier-config-card glass-card" key={tier.tierId}>
              <h4>{tier.tierName}</h4>
              {[
                ['maxStorageMb', 'Dung lượng tối đa (MB)'],
                ['totalStorageMb', 'Dung lượng tổng (MB)'],
                ['aiPromptLimitPerDay', 'AI prompt/ngày'],
                ['price', 'Giá (VNĐ)'],
              ].map(([key, label]) => (
                <label key={key}>
                  {label}
                  <input
                    className="input-control"
                    type="number"
                    value={items[tier.__index][key]}
                    onChange={(e) => change(tier.__index, key, Number(e.target.value))}
                  />
                </label>
              ))}
              <button
                className="btn-primary"
                onClick={async () => {
                  await api.admin.updateSubscription(tier.tierId, items[tier.__index]);
                  await load();
                }}
              >
                Lưu cấu hình
              </button>
            </div>
          ))}
        </div>
      )}

      {detail &&
        createPortal(
          <div className="modal-overlay document-modal-overlay" onMouseDown={() => setDetail(null)}>
            <article
              className="document-detail-modal glass-panel"
              role="dialog"
              aria-modal="true"
              aria-labelledby="document-detail-title"
              onMouseDown={(e) => e.stopPropagation()}
            >
              <header className="document-detail-header">
                <div className="document-title-icon">
                  <FileText size={28} />
                </div>
                <div>
                  <span className="document-id-label">Tài liệu #{detail.document.documentId}</span>
                  <h3 id="document-detail-title">
                    {detail.document.title}.{detail.document.fileExtension}
                  </h3>
                  <p>Thông tin tài liệu và lịch sử tương tác</p>
                </div>
                <button
                  className="modal-close"
                  onClick={() => setDetail(null)}
                  aria-label="Đóng chi tiết tài liệu"
                >
                  <X size={20} />
                </button>
              </header>
              <div className="document-metric-grid">
                <div>
                  <Eye />
                  <strong>{detail.document.viewCount ?? 0}</strong>
                  <span>Lượt xem</span>
                </div>
                <div>
                  <Download />
                  <strong>{detail.document.downloadCount ?? 0}</strong>
                  <span>Lượt tải</span>
                </div>
                <div>
                  <Bookmark />
                  <strong>{detail.document.bookmarkCount ?? 0}</strong>
                  <span>Lượt lưu</span>
                </div>
              </div>
              <section className="document-info-section">
                <h4>Thông tin chung</h4>
                <div className="detail-grid">
                  <div>
                    <UserRound />
                    <span>Người đăng</span>
                    <strong>{detail.document.uploaderName}</strong>
                  </div>
                  <div>
                    <FileText />
                    <span>Trạng thái</span>
                    <strong className={`detail-status ${detail.document.sharingPermission}`}>
                      {detail.document.sharingPermission}
                    </strong>
                  </div>
                  <div>
                    <HardDrive />
                    <span>Kích thước</span>
                    <strong>{Number(detail.document.fileSizeMb ?? 0).toFixed(2)} MB</strong>
                  </div>
                  <div>
                    <CalendarDays />
                    <span>Ngày đăng</span>
                    <strong>
                      {detail.document.createdAt
                        ? formatDateTime(detail.document.createdAt)
                        : 'Không có dữ liệu'}
                    </strong>
                  </div>
                </div>
              </section>
              <section className="document-content-section">
                <h4>Nội dung / mô tả trích xuất</h4>
                <div className="description">{detail.description || 'Chưa có nội dung mô tả.'}</div>
              </section>
              <section className="audience-section">
                <div className="section-title-row">
                  <h4>Người xem và tải</h4>
                  <span>{detail.audience.length} người</span>
                </div>
                <div className="audience-list">
                  {detail.audience.length ? (
                    detail.audience.map((person: any) => (
                      <div className="audience-row" key={person.userId}>
                        <span className="audience-avatar">
                          {person.username?.charAt(0).toUpperCase()}
                        </span>
                        <span className="audience-identity">
                          <strong>{person.username}</strong>
                          <small>{person.email || 'Không có email'}</small>
                        </span>
                        <span className="audience-metrics">
                          <strong>
                            {person.viewCount} xem · {person.downloadCount} tải
                          </strong>
                          <small>Lần cuối: {formatDateTime(person.lastActivityAt)}</small>
                        </span>
                      </div>
                    ))
                  ) : (
                    <div className="audience-empty">
                      <UserRound size={28} />
                      <p>Chưa có người dùng nào xem hoặc tải tài liệu.</p>
                    </div>
                  )}
                </div>
              </section>
            </article>
          </div>,
          document.body,
        )}

      <style>{`.admin-config-page{display:flex;flex-direction:column;gap:1.2rem;height:100%;overflow:auto}.admin-section-tabs{display:flex;gap:.5rem;border-bottom:1px solid rgba(255,255,255,.08)}.admin-section-tabs a{padding:.7rem 1rem;color:var(--text-muted)}.admin-section-tabs a.active{color:var(--text-primary);border-bottom:2px solid var(--accent-purple)}.admin-toolbar{display:grid;grid-template-columns:minmax(180px,1fr) repeat(3,minmax(120px,180px));gap:.7rem}.admin-config-list{display:flex;flex-direction:column;gap:.65rem}.admin-config-row{display:grid;grid-template-columns:minmax(220px,1fr) auto repeat(3,auto);align-items:center;gap:.7rem;padding:1rem;border-bottom:1px solid rgba(255,255,255,.06)}.admin-config-row>div{display:flex;flex-direction:column;gap:.3rem}.admin-config-row small{color:var(--text-muted)}.config-status.PUBLIC{color:var(--success)}.danger{color:var(--danger)!important}.admin-config-form,.new-reason-form{display:grid;grid-template-columns:110px 110px 85px 85px minmax(160px,1fr) auto auto;gap:.6rem;align-items:center;padding:.8rem}.tier-config-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:1rem}.tier-config-card{padding:1.2rem;display:flex;flex-direction:column;gap:.8rem}.tier-config-card label{display:flex;flex-direction:column;gap:.35rem}.document-detail-modal{position:relative;width:min(760px,calc(100vw - 2rem));max-height:85vh;overflow:auto;padding:1.5rem}.modal-close{position:absolute;right:1rem;top:1rem;background:none;border:0;color:var(--text-primary)}.detail-grid{display:grid;grid-template-columns:130px 1fr;gap:.6rem;margin:1rem 0}.detail-grid span{color:var(--text-muted)}.description{white-space:pre-wrap;max-height:160px;overflow:auto;color:var(--text-secondary)}.audience-list>div{display:flex;justify-content:space-between;gap:1rem;padding:.7rem 0;border-bottom:1px solid rgba(255,255,255,.06)}.audience-list span{display:flex;flex-direction:column}.audience-list span:last-child{text-align:right}@media(max-width:900px){.admin-toolbar,.admin-config-row,.admin-config-form,.new-reason-form{grid-template-columns:1fr}.wide{grid-column:auto}.admin-config-row{align-items:stretch}.detail-grid{grid-template-columns:1fr}.audience-list>div{flex-direction:column}.audience-list span:last-child{text-align:left}}`}</style>
      <style>{`
      .document-modal-overlay{padding:1rem}
      .document-detail-modal{position:relative;width:min(880px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:0;border-radius:18px}
      .document-detail-header{position:sticky;top:0;z-index:2;display:grid;grid-template-columns:auto minmax(0,1fr) auto;align-items:center;gap:1rem;padding:1.25rem 1.5rem;background:rgba(13,20,39,.96);border-bottom:1px solid rgba(255,255,255,.08);backdrop-filter:blur(14px)}
      .document-title-icon{width:52px;height:52px;display:grid;place-items:center;border-radius:14px;color:var(--accent-blue);background:rgba(0,180,216,.12)}
      .document-detail-header h3{margin:.15rem 0 0!important;padding:0!important;border:0!important;font-size:1.2rem!important;overflow-wrap:anywhere}
      .document-detail-header p,.document-id-label{color:var(--text-muted);font-size:.8rem}
      .document-detail-modal .modal-close{position:static;width:36px;height:36px;display:grid;place-items:center;border:1px solid rgba(255,255,255,.1);border-radius:50%;background:rgba(255,255,255,.04);color:var(--text-primary);cursor:pointer}
      .document-detail-modal .modal-close:hover{border-color:var(--accent-blue);color:var(--accent-blue)}
      .document-metric-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:.8rem;padding:1.25rem 1.5rem}
      .document-metric-grid>div{display:grid;grid-template-columns:auto 1fr;grid-template-rows:auto auto;gap:.05rem .65rem;align-items:center;padding:1rem;border:1px solid rgba(255,255,255,.07);border-radius:12px;background:rgba(255,255,255,.035)}
      .document-metric-grid svg{grid-row:1/3;color:var(--accent-blue)}.document-metric-grid strong{font-size:1.25rem}.document-metric-grid span{color:var(--text-muted);font-size:.78rem}
      .document-detail-modal section{padding:0 1.5rem 1.25rem}.document-detail-modal section h4{margin:0 0 .8rem}
      .document-detail-modal .detail-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.7rem;margin:0}
      .document-detail-modal .detail-grid>div{display:grid;grid-template-columns:auto 1fr;gap:.1rem .6rem;align-items:center;padding:.8rem;border-radius:10px;background:rgba(255,255,255,.03)}
      .document-detail-modal .detail-grid svg{grid-row:1/3;color:var(--text-muted);width:18px}.document-detail-modal .detail-grid span{color:var(--text-muted);font-size:.76rem}.document-detail-modal .detail-grid strong{overflow-wrap:anywhere}
      .detail-status.PUBLIC{color:var(--success)}.detail-status.PRIVATE{color:var(--warning)}
      .document-detail-modal .description{white-space:pre-wrap;max-height:220px;overflow:auto;padding:1rem;border-radius:10px;background:rgba(255,255,255,.035);color:var(--text-secondary);line-height:1.6}
      .section-title-row{display:flex;justify-content:space-between;align-items:center}.section-title-row>span{padding:.25rem .55rem;border-radius:999px;background:rgba(0,180,216,.1);color:var(--accent-blue);font-size:.75rem}
      .document-detail-modal .audience-list{border:1px solid rgba(255,255,255,.07);border-radius:12px;overflow:hidden}.document-detail-modal .audience-row{display:grid;grid-template-columns:auto minmax(0,1fr) auto;align-items:center;gap:.75rem;padding:.8rem 1rem;border-bottom:1px solid rgba(255,255,255,.06)}
      .document-detail-modal .audience-row:last-child{border-bottom:0}.audience-avatar{width:34px;height:34px;display:grid!important;place-items:center;border-radius:50%;background:var(--accent-glow);color:white;font-weight:700}.audience-identity,.audience-metrics{display:flex;flex-direction:column}.audience-identity small,.audience-metrics small{color:var(--text-muted)}.audience-metrics{text-align:right}.audience-empty{display:flex;flex-direction:column;align-items:center;gap:.5rem;padding:2rem;color:var(--text-muted)}
      @media(max-width:700px){.document-detail-header{padding:1rem;gap:.7rem}.document-title-icon{width:42px;height:42px}.document-metric-grid{grid-template-columns:1fr;padding:1rem}.document-detail-modal .detail-grid{grid-template-columns:1fr}.document-detail-modal section{padding:0 1rem 1rem}.document-detail-modal .audience-row{grid-template-columns:auto minmax(0,1fr)}.audience-metrics{grid-column:2;text-align:left}}
    `}</style>
    </div>
  );
};
