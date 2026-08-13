import React, { useEffect, useState } from 'react';
import { Bell, CheckCheck, Trash2, X } from 'lucide-react';
import { api } from '../services/api';

export const Notifications: React.FC = () => {
  const [items, setItems] = useState<any[]>([]);
  const [selected, setSelected] = useState<any>(null);
  const load = () => api.document.getModerationNotices().then(setItems);
  useEffect(() => {
    load();
  }, []);
  const open = async (n: any) => {
    if (!n.isRead) {
      await api.document.readModerationNotice(n.noticeId);
      setItems((x) => x.map((i) => (i.noticeId === n.noticeId ? { ...i, isRead: true } : i)));
    }
    setSelected({ ...n, isRead: true });
  };
  const readAll = async () => {
    await api.document.readAllModerationNotices();
    setItems((x) => x.map((i) => ({ ...i, isRead: true })));
  };
  const remove = async (id: number) => {
    await api.document.deleteModerationNotice(id);
    setItems((x) => x.filter((i) => i.noticeId !== id));
    if (selected?.noticeId === id) setSelected(null);
  };
  return (
    <div className="notifications-page">
      <header>
        <div>
          <small>TRUNG TÂM THÔNG BÁO</small>
          <h1>Thông báo</h1>
          <p>Các cập nhật và quyết định liên quan đến tài liệu của bạn.</p>
        </div>
        <button className="btn-secondary" onClick={readAll}>
          <CheckCheck size={17} /> Đánh dấu đã xem tất cả
        </button>
      </header>
      <section className="glass-panel notice-list">
        {items.length === 0 ? (
          <div className="empty">
            <Bell />
            <strong>Chưa có thông báo</strong>
          </div>
        ) : (
          items.map((n) => (
            <article
              key={n.noticeId}
              className={n.isRead ? 'read' : 'unread'}
              onClick={() => open(n)}
            >
              <span className="dot" />
              <div>
                <strong>{n.title}</strong>
                <p>{n.message}</p>
                <small>{new Date(n.createdAt).toLocaleString('vi-VN')}</small>
              </div>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  remove(n.noticeId);
                }}
                title="Xóa"
              >
                <Trash2 />
              </button>
            </article>
          ))
        )}
      </section>
      {selected && (
        <div className="modal-overlay" onMouseDown={() => setSelected(null)}>
          <div className="glass-panel notice-modal" onMouseDown={(e) => e.stopPropagation()}>
            <button className="close" onClick={() => setSelected(null)}>
              <X />
            </button>
            <small>CHI TIẾT THÔNG BÁO</small>
            <h2>{selected.title}</h2>
            <p>{selected.message}</p>
            <div>
              <button className="btn-secondary danger" onClick={() => remove(selected.noticeId)}>
                <Trash2 size={16} /> Xóa thông báo
              </button>
              <button className="btn-primary" onClick={() => setSelected(null)}>
                Đã hiểu
              </button>
            </div>
          </div>
        </div>
      )}
      <style>{css}</style>
    </div>
  );
};
const css = `.notifications-page{display:grid;gap:1.3rem}.notifications-page header{display:flex;justify-content:space-between;align-items:end}.notifications-page header button{display:flex;gap:.5rem}.notifications-page h1{margin:.3rem 0}.notifications-page p{color:var(--text-secondary)}.notice-list{overflow:hidden}.notice-list article{display:flex;gap:.8rem;padding:1rem;cursor:pointer;border-bottom:1px solid rgba(255,255,255,.07)}.notice-list article:hover{background:rgba(255,255,255,.03)}.notice-list article.unread{background:rgba(56,189,248,.06)}.notice-list .dot{width:8px;height:8px;margin-top:.45rem;border-radius:50%;background:transparent}.notice-list .unread .dot{background:#38bdf8}.notice-list article div{flex:1}.notice-list article p{margin:.35rem 0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.notice-list article button,.notice-modal .close{border:0;background:transparent;color:var(--text-secondary)}.empty{min-height:260px;display:grid;place-content:center;justify-items:center;gap:.6rem;color:var(--text-secondary)}.notice-modal{position:relative;width:min(560px,calc(100vw - 2rem));padding:1.5rem}.notice-modal .close{position:absolute;right:1rem;top:1rem}.notice-modal>p{white-space:pre-wrap;line-height:1.7;margin:1rem 0}.notice-modal>div{display:flex;justify-content:flex-end;gap:.6rem}.modal-overlay{position:fixed;inset:0;z-index:3100;display:grid;place-items:center;background:rgba(3,7,18,.82);backdrop-filter:blur(8px)}@media(max-width:700px){.notifications-page header{align-items:start;flex-direction:column;gap:1rem}}`;
