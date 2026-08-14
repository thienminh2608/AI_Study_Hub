import React, { useEffect, useRef, useState } from 'react';
import { Bell, CheckCheck, ExternalLink, X } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../services/api';
import { NotificationTypeIcon } from './NotificationTypeIcon';
import { formatDateTime } from '../utils/dateTime';

export const NotificationBell: React.FC = () => {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  const load = async () => {
    setLoading(true);
    try {
      setItems(await api.document.getModerationNotices());
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  useEffect(() => {
    const close = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  const unread = items.filter((item) => !item.isRead).length;
  const markRead = async (notice: any) => {
    if (notice.isRead) return;
    await api.document.readModerationNotice(notice.noticeId);
    setItems((current) =>
      current.map((item) => (item.noticeId === notice.noticeId ? { ...item, isRead: true } : item)),
    );
  };
  const markAllRead = async () => {
    await api.document.readAllModerationNotices();
    setItems((current) => current.map((item) => ({ ...item, isRead: true })));
  };

  return (
    <div className="notification-bell" ref={rootRef}>
      <button
        className="notification-trigger"
        aria-label={`Thông báo${unread ? `, ${unread} chưa đọc` : ''}`}
        aria-expanded={open}
        onClick={() => {
          if (!open) load();
          setOpen((value) => !value);
        }}
      >
        <Bell size={21} />
        {unread > 0 && <span className="notification-count">{unread > 99 ? '99+' : unread}</span>}
      </button>
      {open && (
        <section className="notification-popover glass-panel" aria-label="Danh sách thông báo">
          <header>
            <div>
              <strong>Thông báo</strong>
              <small>{unread ? `${unread} thông báo chưa đọc` : 'Bạn đã xem tất cả'}</small>
            </div>
            <button
              className="icon-button"
              aria-label="Đóng thông báo"
              onClick={() => setOpen(false)}
            >
              <X size={18} />
            </button>
          </header>
          <div className="notification-scroll">
            {loading ? (
              <p className="notification-empty">Đang tải thông báo...</p>
            ) : items.length === 0 ? (
              <p className="notification-empty">Chưa có thông báo.</p>
            ) : (
              items.map((notice) => (
                <Link
                  key={notice.noticeId}
                  className={`notification-item ${notice.isRead ? '' : 'unread'}`}
                  to={
                    notice.canAppeal
                      ? `/notifications?reportId=${notice.reportId}`
                      : notice.actionUrl || '/notifications'
                  }
                  state={{ noticeId: notice.noticeId }}
                  onClick={() => markRead(notice)}
                >
                  <span className={`notification-type-icon ${notice.type || ''}`}>
                    <NotificationTypeIcon type={notice.type} />
                  </span>
                  <span>
                    <strong>{notice.title}</strong>
                    <small>{notice.message}</small>
                    <time>{formatDateTime(notice.createdAt)}</time>
                  </span>
                </Link>
              ))
            )}
          </div>
          <footer>
            <button className="popover-action" disabled={!unread} onClick={markAllRead}>
              <CheckCheck size={16} /> Đánh dấu đã đọc
            </button>
            <Link to="/notifications" onClick={() => setOpen(false)}>
              Xem tất cả <ExternalLink size={14} />
            </Link>
          </footer>
        </section>
      )}
    </div>
  );
};
