import React, { useEffect, useState, useCallback, useMemo } from 'react';
import { api, type TrashItem, type PagedResult } from '../services/api';
import { Pagination } from '../components/Pagination';
import { Trash2, RotateCcw, Folder, Loader } from 'lucide-react';
import { useUiFeedback } from '../context/UiFeedbackContext';
import { formatDateTime } from '../utils/dateTime';
import { FileTypeIcon } from '../components/FileTypeIcon';

const getCleanTitle = (title: string, ext?: string) => {
  if (!title) return '';
  let clean = title;
  const lastSlash = Math.max(clean.lastIndexOf('/'), clean.lastIndexOf('\\'));
  if (lastSlash >= 0) {
    clean = clean.substring(lastSlash + 1);
  }
  if (ext) {
    const lowerExt = `.${ext.toLowerCase()}`;
    if (clean.toLowerCase().endsWith(lowerExt)) {
      clean = clean.substring(0, clean.length - lowerExt.length);
    }
  }
  return clean;
};

export const TrashPage: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const [trashData, setTrashData] = useState<PagedResult<TrashItem> | null>(null);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [sortKey, setSortKey] = useState<'name' | 'itemType' | 'deletedAt'>('deletedAt');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const pageSize = 10;
  const sortedItems = useMemo(() => [...(trashData?.items || [])].sort((left, right) => {
    const a = left[sortKey] || '';
    const b = right[sortKey] || '';
    const result = sortKey === 'deletedAt'
      ? new Date(a).getTime() - new Date(b).getTime()
      : String(a).localeCompare(String(b), 'vi', { numeric: true });
    return sortDirection === 'asc' ? result : -result;
  }), [trashData, sortKey, sortDirection]);
  const sortHeader = (key: typeof sortKey, label: string) => (
    <button type="button" className="trash-sort-header" onClick={() => {
      if (sortKey === key) setSortDirection((current) => current === 'asc' ? 'desc' : 'asc');
      else { setSortKey(key); setSortDirection('asc'); }
    }}>
      {label} {sortKey === key ? (sortDirection === 'asc' ? '↑' : '↓') : '↕'}
    </button>
  );

  const fetchTrash = useCallback(async (p = 1) => {
    setLoading(true);
    try {
      const res = await api.trash.getTrashItems(p, pageSize);
      setTrashData(res);
      setPage(p);
    } catch (err: any) {
      notify(err.message || 'Không thể tải danh sách thùng rác.', 'error');
    } finally {
      setLoading(false);
    }
  }, [notify, pageSize]);

  useEffect(() => {
    fetchTrash(1);
  }, [fetchTrash]);

  const handleRestore = async (item: TrashItem) => {
    try {
      if (item.itemType === 'DOCUMENT') {
        await api.trash.restoreDocument(item.itemId);
      } else {
        await api.trash.restoreFolder(item.itemId);
      }
      notify(`Đã khôi phục '${getCleanTitle(item.name, item.fileExtension)}'`, 'success');
      await fetchTrash(page);
    } catch (err: any) {
      notify(err.message || 'Khôi phục thất bại.', 'error');
    }
  };

  const handlePermanentDelete = async (item: TrashItem) => {
    const cleanName = getCleanTitle(item.name, item.fileExtension);
    if (
      !(await confirm({
        title: 'Xóa vĩnh viễn',
        message: `Bạn có chắc muốn xóa VĨNH VIỄN '${cleanName}'? Hành động này không thể hoàn tác!`,
        confirmLabel: 'Xóa vĩnh viễn',
        danger: true,
      }))
    )
      return;

    try {
      if (item.itemType === 'DOCUMENT') {
        await api.trash.permanentDeleteDocument(item.itemId);
      } else {
        await api.trash.permanentDeleteFolder(item.itemId);
      }
      notify(`Đã xóa vĩnh viễn '${cleanName}'`, 'success');
      await fetchTrash(page);
    } catch (err: any) {
      notify(err.message || 'Không thể xóa vĩnh viễn.', 'error');
    }
  };

  const handleEmptyTrash = async () => {
    if (
      !(await confirm({
        title: 'Dọn sạch thùng rác',
        message: 'Bạn có chắc muốn dọn sạch TẤT CẢ các mục trong Thùng rác? Hành động này sẽ xóa vĩnh viễn dữ liệu!',
        confirmLabel: 'Dọn sạch thùng rác',
        danger: true,
      }))
    )
      return;

    try {
      await api.trash.emptyTrash();
      notify('Đã dọn sạch thùng rác.', 'success');
      await fetchTrash(1);
    } catch (err: any) {
      notify(err.message || 'Không thể dọn thùng rác.', 'error');
    }
  };

  return (
    <div className="trash-page animate-fade-in">
      <div className="trash-header">
        <div>
          <h2>Thùng rác của tôi</h2>
          <p className="trash-subtitle">
            Các mục trong thùng rác có thể được khôi phục hoặc xóa vĩnh viễn để giải phóng dung lượng.
          </p>
        </div>
        {trashData && trashData.items.length > 0 && (
          <button onClick={handleEmptyTrash} className="empty-trash-btn">
            <Trash2 size={16} />
            <span>Dọn sạch thùng rác</span>
          </button>
        )}
      </div>

      {loading ? (
        <div className="trash-loading glass-card">
          <Loader className="spin" size={28} />
          <span>Đang tải danh sách thùng rác...</span>
        </div>
      ) : !trashData || trashData.items.length === 0 ? (
        <div className="trash-empty glass-card">
          <Trash2 size={48} className="empty-icon" />
          <h3>Thùng rác trống</h3>
          <p>Không có tài liệu hoặc thư mục nào trong thùng rác.</p>
        </div>
      ) : (
        <div className="trash-container">
          <div className="table-responsive glass-card">
            <table className="trash-table">
              <thead>
                <tr>
                  <th>{sortHeader('name', 'Tên mục')}</th>
                  <th>{sortHeader('itemType', 'Loại')}</th>
                  <th>{sortHeader('deletedAt', 'Ngày xóa')}</th>
                  <th style={{ textAlign: 'right' }}>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {sortedItems.map((item) => {
                  const cleanName = getCleanTitle(item.name, item.fileExtension);
                  const fullName = item.itemType === 'FOLDER' ? cleanName : `${cleanName}.${item.fileExtension || ''}`;

                  return (
                    <tr key={`${item.itemType}-${item.itemId}`}>
                      <td>
                        <div className="item-name-cell">
                          {item.itemType === 'FOLDER' ? (
                            <Folder size={24} className="folder-icon" />
                          ) : (
                            <FileTypeIcon extension={item.fileExtension || 'file'} size={24} />
                          )}
                          <span className="item-name-text" title={fullName}>
                            {fullName}
                          </span>
                        </div>
                      </td>
                      <td>
                        <span className="item-type-badge">
                          {item.itemType === 'FOLDER' ? 'Thư mục' : `Tài liệu (${item.fileExtension?.toUpperCase() || 'FILE'})`}
                        </span>
                      </td>
                      <td className="item-date">{formatDateTime(item.deletedAt)}</td>
                      <td>
                        <div className="action-buttons">
                          <button
                            onClick={() => handleRestore(item)}
                            className="btn-restore"
                            title="Khôi phục về vị trí cũ"
                          >
                            <RotateCcw size={15} />
                            <span>Khôi phục</span>
                          </button>
                          <button
                            onClick={() => handlePermanentDelete(item)}
                            className="btn-perm-delete"
                            title="Xóa vĩnh viễn"
                          >
                            <Trash2 size={15} />
                            <span>Xóa vĩnh viễn</span>
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <Pagination
            currentPage={trashData.pageNumber}
            totalPages={trashData.totalPages}
            totalCount={trashData.totalCount}
            onPageChange={(p) => fetchTrash(p)}
          />
        </div>
      )}

      <style>{`
        .trash-page {
          max-width: 1200px;
          margin: 0 auto;
          padding: 1.5rem 1rem;
        }

        .trash-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 1.5rem;
          gap: 1rem;
          flex-wrap: wrap;
        }

        .trash-header h2 {
          font-size: 1.6rem;
          font-weight: 700;
          color: var(--text-primary);
          margin-bottom: 0.25rem;
        }

        .trash-subtitle {
          font-size: 0.88rem;
          color: var(--text-muted);
        }

        .empty-trash-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.5rem;
          background: rgba(239, 68, 68, 0.15);
          color: #f87171;
          border: 1px solid rgba(239, 68, 68, 0.3);
          padding: 0.55rem 1rem;
          border-radius: var(--radius-md);
          font-size: 0.88rem;
          font-weight: 600;
          cursor: pointer;
          transition: var(--transition-fast);
        }

        .empty-trash-btn:hover {
          background: rgba(239, 68, 68, 0.25);
          border-color: rgba(239, 68, 68, 0.5);
          color: #ef4444;
        }

        .trash-loading {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          padding: 4rem 2rem;
          gap: 1rem;
          color: var(--text-muted);
          border-radius: var(--radius-lg);
        }

        .trash-empty {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          padding: 4rem 2rem;
          text-align: center;
          border-radius: var(--radius-lg);
        }

        .trash-empty .empty-icon {
          color: var(--text-muted);
          margin-bottom: 1rem;
          opacity: 0.6;
        }

        .trash-empty h3 {
          font-size: 1.2rem;
          color: var(--text-primary);
          margin-bottom: 0.4rem;
        }

        .trash-empty p {
          font-size: 0.88rem;
          color: var(--text-muted);
        }

        .trash-container {
          display: flex;
          flex-direction: column;
          gap: 1rem;
        }

        .table-responsive {
          border-radius: var(--radius-lg);
          overflow-x: auto;
          border: 1px solid rgba(255, 255, 255, 0.08);
        }

        .trash-table {
          width: 100%;
          border-collapse: collapse;
          text-align: left;
        }

        .trash-table th {
          background: rgba(255, 255, 255, 0.03);
          padding: 0.9rem 1.25rem;
          font-size: 0.8rem;
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--text-muted);
          border-bottom: 1px solid rgba(255, 255, 255, 0.08);
        }

        .trash-sort-header { border:0;background:transparent;color:inherit;font:inherit;text-transform:inherit;letter-spacing:inherit;cursor:pointer;padding:0; }
        .trash-sort-header:hover { color:var(--accent-blue); }

        .trash-table td {
          padding: 1rem 1.25rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          font-size: 0.9rem;
          color: var(--text-primary);
          vertical-align: middle;
        }

        .trash-table tbody tr {
          transition: background 0.15s ease;
        }

        .trash-table tbody tr:hover {
          background: rgba(255, 255, 255, 0.04);
        }

        .trash-table tbody tr:last-child td {
          border-bottom: none;
        }

        .item-name-cell {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          max-width: 380px;
        }

        .folder-icon {
          color: var(--accent-purple);
          flex-shrink: 0;
        }

        .item-name-text {
          font-weight: 600;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .item-type-badge {
          display: inline-flex;
          align-items: center;
          padding: 0.25rem 0.65rem;
          border-radius: 999px;
          background: rgba(255, 255, 255, 0.06);
          border: 1px solid rgba(255, 255, 255, 0.08);
          font-size: 0.78rem;
          color: var(--text-secondary);
        }

        .item-date {
          font-size: 0.82rem;
          color: var(--text-muted);
          white-space: nowrap;
        }

        .action-buttons {
          display: flex;
          justify-content: flex-end;
          align-items: center;
          gap: 0.5rem;
        }

        .btn-restore {
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          background: rgba(99, 102, 241, 0.12);
          color: #818cf8;
          border: 1px solid rgba(99, 102, 241, 0.25);
          padding: 0.4rem 0.75rem;
          border-radius: var(--radius-sm);
          font-size: 0.82rem;
          font-weight: 600;
          cursor: pointer;
          transition: var(--transition-fast);
        }

        .btn-restore:hover {
          background: rgba(99, 102, 241, 0.25);
          color: #a5b4fc;
        }

        .btn-perm-delete {
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          background: rgba(239, 68, 68, 0.12);
          color: #f87171;
          border: 1px solid rgba(239, 68, 68, 0.25);
          padding: 0.4rem 0.75rem;
          border-radius: var(--radius-sm);
          font-size: 0.82rem;
          font-weight: 600;
          cursor: pointer;
          transition: var(--transition-fast);
        }

        .btn-perm-delete:hover {
          background: rgba(239, 68, 68, 0.25);
          color: #ef4444;
        }
      `}</style>
    </div>
  );
};
