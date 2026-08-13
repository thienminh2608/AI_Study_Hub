import React, { useEffect, useState } from 'react';
import { Bell, X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { api } from '../services/api';

export const ModerationNoticePopup: React.FC = () => {
  const [notices, setNotices] = useState<any[]>([]);
  const navigate = useNavigate();
  useEffect(() => {
    api.document
      .getModerationNotices(true)
      .then((x) => setNotices(x.slice(0, 3)))
      .catch(() => {});
  }, []);
  const dismiss = (id: number) => setNotices((x) => x.filter((n) => n.noticeId !== id));
  const open = async (n: any) => {
    await api.document.readModerationNotice(n.noticeId);
    dismiss(n.noticeId);
    navigate('/notifications', { state: { noticeId: n.noticeId } });
  };
  return (
    <div style={s.stack}>
      {notices.map((n) => (
        <div key={n.noticeId} className="glass-panel" style={s.toast}>
          <Bell size={20} color="#38bdf8" />
          <button style={s.body} onClick={() => open(n)}>
            <strong>{n.title}</strong>
            <span>{n.message}</span>
          </button>
          <button style={s.close} onClick={() => dismiss(n.noticeId)} aria-label="Ẩn thông báo">
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
    width: 'min(390px,calc(100vw - 40px))',
  },
  toast: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: 10,
    padding: '1rem',
    borderRadius: 12,
    boxShadow: '0 16px 50px rgba(0,0,0,.35)',
  },
  body: {
    display: 'grid',
    gap: 5,
    flex: 1,
    textAlign: 'left',
    border: 0,
    background: 'transparent',
    color: 'inherit',
  },
  close: { border: 0, background: 'transparent', color: 'var(--text-secondary)' },
};
