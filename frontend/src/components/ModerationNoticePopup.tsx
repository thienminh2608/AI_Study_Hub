import React, { useEffect, useState } from 'react';
import { X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { api } from '../services/api';
import { NotificationTypeIcon } from './NotificationTypeIcon';

export const ModerationNoticePopup: React.FC = () => {
  const [notices, setNotices] = useState<any[]>([]);
  const navigate = useNavigate();
  useEffect(() => {
    api.document
      .getModerationNotices(true)
      .then((x) => setNotices(x.slice(0, 3)))
      .catch(() => {});
  }, []);
  useEffect(() => {
    if (notices.length === 0) return;
    const timer = window.setTimeout(() => setNotices([]), 8000);
    return () => window.clearTimeout(timer);
  }, [notices.length]);
  const dismiss = (id: number) => setNotices((x) => x.filter((n) => n.noticeId !== id));
  const open = async (n: any) => {
    await api.document.readModerationNotice(n.noticeId);
    dismiss(n.noticeId);
    navigate(
      n.canAppeal ? `/notifications?reportId=${n.reportId}` : n.actionUrl || '/notifications',
      {
        state: { noticeId: n.noticeId },
      },
    );
  };
  return (
    <div style={s.stack}>
      {notices.map((n) => (
        <div key={n.noticeId} className="glass-panel" style={s.toast}>
          <NotificationTypeIcon type={n.type} size={20} />
          <button style={s.body} onClick={() => open(n)} title={`${n.title}\n${n.message}`}>
            <strong style={s.title}>{n.title}</strong>
            <span style={s.message}>{n.message}</span>
          </button>
          <button
            type="button"
            style={s.close}
            onClick={() => dismiss(n.noticeId)}
            aria-label="Đóng thông báo"
            title="Đóng thông báo"
          >
            <X size={16} />
          </button>
        </div>
      ))}
    </div>
  );
};
const s: Record<string, React.CSSProperties> = {
  stack: {
    position: 'fixed',
    right: 20,
    top: 20,
    zIndex: 3000,
    display: 'grid',
    gap: 10,
    width: 'min(390px, calc(100vw - 40px))',
    maxHeight: 'calc(100dvh - 40px)',
    overflowY: 'auto',
    pointerEvents: 'none',
  },
  toast: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: 10,
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
    padding: '1rem',
    borderRadius: 12,
    boxShadow: '0 16px 50px rgba(0,0,0,.35)',
    overflow: 'hidden',
    pointerEvents: 'auto',
  },
  body: {
    display: 'grid',
    gap: 5,
    flex: 1,
    minWidth: 0,
    padding: 0,
    cursor: 'pointer',
    textAlign: 'left',
    border: 0,
    background: 'transparent',
    color: 'inherit',
  },
  title: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  message: {
    display: '-webkit-box',
    minWidth: 0,
    overflow: 'hidden',
    overflowWrap: 'anywhere',
    WebkitBoxOrient: 'vertical',
    WebkitLineClamp: 3,
  },
  close: {
    display: 'grid',
    placeItems: 'center',
    flex: '0 0 28px',
    width: 28,
    height: 28,
    padding: 0,
    border: 0,
    borderRadius: 6,
    background: 'rgba(255,255,255,.05)',
    color: 'var(--text-secondary)',
    cursor: 'pointer',
  },
};
