import React, { useEffect, useState, useCallback } from 'react';
import { createPortal } from 'react-dom';
import {
  Bell,
  CheckCheck,
  ExternalLink,
  Loader,
  MessageSquareReply,
  Trash2,
  X,
} from 'lucide-react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../services/api';
import { NotificationTypeIcon } from '../components/NotificationTypeIcon';
import { formatDateTime } from '../utils/dateTime';
import { useAuth } from '../context/AuthContext';

const appealCss = `.appeal-heading{display:flex;align-items:flex-start;gap:.9rem;padding-right:2rem}.appeal-heading h2{margin:.15rem 0}.appeal-heading p{margin:.2rem 0 0}.appeal-icon{width:44px;height:44px;display:grid;place-items:center;flex:0 0 auto;border-radius:12px;background:rgba(56,189,248,.12);color:#38bdf8}.appeal-guidance{margin:0;padding:.8rem 1rem;border:1px solid rgba(56,189,248,.16);border-radius:9px;background:rgba(56,189,248,.06);line-height:1.55}.appeal-modal label>span{font-weight:700;color:var(--text-primary)}.appeal-modal label small{font-weight:400;color:var(--text-muted)}.appeal-actions{padding-top:.25rem;border-top:1px solid rgba(255,255,255,.08)}`;

export const Notifications: React.FC = () => {
  const [items, setItems] = useState<any[]>([]);
  const [selected, setSelected] = useState<any>(null);
  const [appealNotice, setAppealNotice] = useState<any>(null);
  const [explanation, setExplanation] = useState('');
  const [evidenceUrl, setEvidenceUrl] = useState('');
  const [submittingAppeal, setSubmittingAppeal] = useState(false);
  const [appealError, setAppealError] = useState('');
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { user } = useAuth();
  const open = useCallback(async (n: any) => {
    if (!n.isRead) {
      await api.document.readModerationNotice(n.noticeId);
      setItems((x) => x.map((i) => (i.noticeId === n.noticeId ? { ...i, isRead: true } : i)));
    }
    setSelected({ ...n, isRead: true });
  }, []);

  useEffect(() => {
    const noticeId = location.state?.noticeId;
    const reportId = Number(searchParams.get('reportId'));
    api.document.getModerationNotices().then((data) => {
      setItems(data);
      const target = data.find((item: any) =>
        noticeId ? item.noticeId === noticeId : reportId ? item.reportId === reportId : false,
      );
      if (target) open(target);
    });
  }, [location.state?.noticeId, searchParams, open]);
  const readAll = async () => {
    await api.document.readAllModerationNotices();
    setItems((x) => x.map((i) => ({ ...i, isRead: true })));
  };
  const remove = async (id: number) => {
    await api.document.deleteModerationNotice(id);
    setItems((x) => x.filter((i) => i.noticeId !== id));
    if (selected?.noticeId === id) setSelected(null);
  };
  const submitAppeal = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!appealNotice?.reportId || !explanation.trim()) {
      setAppealError('Vui lòng nhập nội dung giải trình.');
      return;
    }
    setSubmittingAppeal(true);
    setAppealError('');
    try {
      await api.document.appeal(appealNotice.reportId, {
        explanation: explanation.trim(),
        evidenceUrl: evidenceUrl.trim() || null,
      });
      setItems((current) =>
        current.map((item) =>
          item.noticeId === appealNotice.noticeId ? { ...item, canAppeal: false } : item,
        ),
      );
      setAppealNotice(null);
      setExplanation('');
      setEvidenceUrl('');
    } catch (error: any) {
      setAppealError(error.message || 'Không thể gửi giải trình.');
    } finally {
      setSubmittingAppeal(false);
    }
  };
  const roleCopy =
    user?.role?.toUpperCase() === 'ADMIN'
      ? 'Giao dịch mới và các công việc quản trị cần bạn xử lý.'
      : user?.role?.toUpperCase() === 'MODERATOR'
        ? 'Tài liệu, báo cáo và giải trình mới trong hàng đợi kiểm duyệt.'
        : 'Lời mời kết bạn, tài liệu, giao dịch và các quyết định liên quan đến bạn.';
  return (
    <div className="notifications-page">
      <header>
        <div>
          <small>TRUNG TÂM THÔNG BÁO</small>
          <h1>Thông báo</h1>
          <p>{roleCopy}</p>
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
              <span className={`notice-type-icon ${n.type || ''}`}>
                <NotificationTypeIcon type={n.type} size={19} />
              </span>
              <div>
                <strong>{n.title}</strong>
                <p>{n.message}</p>
                <small>{formatDateTime(n.createdAt)}</small>
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
      {selected &&
        createPortal(
          <div className="viewport-modal-overlay" onMouseDown={() => setSelected(null)}>
            <div className="glass-panel notice-modal" onMouseDown={(e) => e.stopPropagation()}>
              <button className="close" onClick={() => setSelected(null)}>
                <X />
              </button>
              <small>CHI TIẾT THÔNG BÁO</small>
              <span className="notice-modal-icon">
                <NotificationTypeIcon type={selected.type} size={22} />
              </span>
              <h2>{selected.title}</h2>
              <p>{selected.message}</p>
              <div>
                <button className="btn-secondary danger" onClick={() => remove(selected.noticeId)}>
                  <Trash2 size={16} /> Xóa thông báo
                </button>
                <button className="btn-primary" onClick={() => setSelected(null)}>
                  Đã hiểu
                </button>
                {selected.actionUrl && !selected.canAppeal && (
                  <button className="btn-primary" onClick={() => navigate(selected.actionUrl)}>
                    Mở công việc <ExternalLink size={16} />
                  </button>
                )}
                {selected.canAppeal && (
                  <button
                    className="btn-primary"
                    onClick={() => {
                      setAppealError('');
                      setExplanation('');
                      setEvidenceUrl('');
                      setAppealNotice(selected);
                      setSelected(null);
                    }}
                  >
                    <MessageSquareReply size={16} /> Gửi giải trình
                  </button>
                )}
              </div>
            </div>
          </div>,
          document.body,
        )}
      {appealNotice &&
        createPortal(
          <div
            className="viewport-modal-overlay appeal-overlay"
            onMouseDown={() => !submittingAppeal && setAppealNotice(null)}
          >
            <form
              className="glass-panel appeal-modal"
              onSubmit={submitAppeal}
              onMouseDown={(event) => event.stopPropagation()}
            >
              <button type="button" className="close" onClick={() => setAppealNotice(null)}>
                <X />
              </button>
              <div className="appeal-heading">
                <span className="appeal-icon">
                  <MessageSquareReply size={22} />
                </span>
                <div>
                  <small>YÊU CẦU XEM XÉT LẠI</small>
                  <h2>Gửi giải trình</h2>
                  <p>
                    Tài liệu: <strong>{appealNotice.title}</strong>
                  </p>
                </div>
              </div>
              <p className="appeal-guidance">
                Trình bày rõ lý do bạn cho rằng quyết định cần được xem xét lại. Moderator sẽ nhận
                nội dung này trong hàng đợi giải trình.
              </p>
              <label>
                <span>
                  Nội dung giải trình <em>*</em>
                </span>
                <textarea
                  value={explanation}
                  onChange={(event) => setExplanation(event.target.value)}
                  rows={6}
                  placeholder="Trình bày lý do và thông tin cần Moderator xem xét lại..."
                />
              </label>
              <label>
                <span>
                  Liên kết bằng chứng <small>(không bắt buộc)</small>
                </span>
                <input
                  className="input-control"
                  type="url"
                  value={evidenceUrl}
                  onChange={(event) => setEvidenceUrl(event.target.value)}
                  placeholder="https://..."
                />
              </label>
              {appealError && <p className="appeal-error">{appealError}</p>}
              <div className="appeal-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setAppealNotice(null)}
                >
                  Hủy
                </button>
                <button className="btn-primary" disabled={submittingAppeal}>
                  {submittingAppeal ? (
                    <Loader className="spin" size={16} />
                  ) : (
                    <MessageSquareReply size={16} />
                  )}{' '}
                  Gửi giải trình
                </button>
              </div>
            </form>
          </div>,
          document.body,
        )}
      <style>{css + appealCss}</style>
    </div>
  );
};
const css = `.notifications-page{display:grid;gap:1.3rem}.notifications-page header{display:flex;justify-content:space-between;align-items:end}.notifications-page header button{display:flex;gap:.5rem}.notifications-page h1{margin:.3rem 0}.notifications-page p{color:var(--text-secondary)}.notice-list{overflow:hidden}.notice-list article{display:grid;grid-template-columns:8px 38px minmax(0,1fr) auto;align-items:start;gap:.8rem;padding:1rem;cursor:pointer;border-bottom:1px solid rgba(255,255,255,.07)}.notice-list article:hover{background:rgba(255,255,255,.03)}.notice-list article.unread{background:rgba(56,189,248,.06)}.notice-list .dot{width:8px;height:8px;margin-top:.8rem;border-radius:50%;background:transparent}.notice-list .unread .dot{background:#38bdf8}.notice-type-icon,.notice-modal-icon{width:38px;height:38px;display:grid;place-items:center;border-radius:10px;background:rgba(0,180,216,.1);color:var(--accent-blue)}.notice-modal-icon{margin:.8rem 0 .4rem}.notice-list article div{min-width:0}.notice-list article p{margin:.35rem 0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.notice-list article button,.notice-modal .close,.appeal-modal .close{border:0;background:transparent;color:var(--text-secondary)}.empty{min-height:260px;display:grid;place-content:center;justify-items:center;gap:.6rem;color:var(--text-secondary)}.notice-modal,.appeal-modal{position:relative;width:min(620px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:1.5rem;background:rgba(17,17,26,.98)}.notice-modal .close,.appeal-modal .close{position:absolute;right:1rem;top:1rem}.notice-modal>p{white-space:pre-wrap;line-height:1.7;margin:1rem 0}.notice-modal>div{display:flex;justify-content:flex-end;gap:.6rem;flex-wrap:wrap}.modal-overlay{position:fixed;inset:0;z-index:3100;display:grid;place-items:center;padding:1rem;background:rgba(3,7,18,.82);backdrop-filter:blur(8px)}.appeal-overlay{z-index:3200}.appeal-modal{display:grid;gap:1rem}.appeal-modal label{display:grid;gap:.45rem;color:var(--text-secondary)}.appeal-modal em{color:var(--danger)}.appeal-modal textarea{resize:vertical;padding:.8rem;border:1px solid rgba(255,255,255,.12);border-radius:8px;background:rgba(255,255,255,.04);color:var(--text-primary)}.appeal-modal .appeal-actions{display:flex;justify-content:flex-end;gap:.6rem;flex-wrap:wrap}.appeal-error{color:var(--danger)!important}@media(max-width:700px){.notifications-page header{align-items:start;flex-direction:column;gap:1rem}.notice-list article{grid-template-columns:8px 34px minmax(0,1fr)}.notice-list article>button{grid-column:3;justify-self:end}.appeal-modal .appeal-actions button{flex:1}}`;
