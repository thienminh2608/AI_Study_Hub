import React from 'react';
import { NavLink, Outlet, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { ModerationNoticePopup } from './ModerationNoticePopup';
import {
  FolderOpen,
  Bot,
  Users,
  Award,
  User as UserIcon,
  LogOut,
  Coins,
  Globe2,
  LayoutDashboard,
  UserCog,
  ReceiptText,
  ShieldAlert,
  SlidersHorizontal,
  Bell,
} from 'lucide-react';

export const SidebarLayout: React.FC = () => {
  const { user, logout } = useAuth();
  const isAdmin = user?.role?.trim().toUpperCase() === 'ADMIN';
  const isModerator = user?.role?.trim().toUpperCase() === 'MODERATOR';
  const [searchParams] = useSearchParams();
  const adminTab = searchParams.get('tab') || 'overview';

  return (
    <div className="layout-container">
      {user && !isAdmin && !isModerator && <ModerationNoticePopup />}
      {/* Sidebar Navigation */}
      <aside className="sidebar glass-panel">
        <div className="logo-section">
          <h2>AI Study Hub</h2>
          <span className="logo-glow"></span>
        </div>

        {/* User Quick Stats Card */}
        {user && (
          <div className="user-stats-card">
            <NavLink to="/profile" className="account-summary">
              <div className="user-info">
                <p className="username">
                  <UserIcon size={17} />
                  <span>{user.username}</span>
                </p>
                <span
                  className={`badge ${isAdmin ? 'admin' : isModerator ? 'moderator' : user.tierId === 3 ? 'premium' : 'free'}`}
                >
                  {isAdmin
                    ? 'ADMIN'
                    : isModerator
                      ? 'MODERATOR'
                      : user.tierId === 3
                        ? 'PREMIUM'
                        : 'FREE'}
                </span>
              </div>
            </NavLink>
            <div className="balance-actions">
              <div className="user-balance">
                <Coins size={16} className="coin-icon" />
                <span>{user.balance.toLocaleString()}đ</span>
              </div>
              {!isAdmin && !isModerator && (
                <NavLink to="/wallet?deposit=1" className="quick-deposit-link">
                  Nạp tiền
                </NavLink>
              )}
            </div>
            {!isAdmin && !isModerator && (
              <div className="account-widget-links">
                <NavLink to="/wallet">
                  <ReceiptText size={15} />
                  <span>Lịch sử giao dịch</span>
                </NavLink>
                <NavLink to="/premium">
                  <Award size={15} />
                  <span>Premium</span>
                </NavLink>
              </div>
            )}
            {isAdmin && (
              <NavLink to="/profile" className="admin-profile-link">
                Hồ sơ & bảo mật
              </NavLink>
            )}
          </div>
        )}
        <NavLink to="/notifications" className="nav-item">
          <Bell size={19} />
          <span>Thông báo</span>
        </NavLink>

        <nav className="nav-links">
          {isAdmin ? (
            <>
              <NavLink
                to="/admin?tab=overview"
                className={`nav-item ${adminTab === 'overview' ? 'active' : ''}`}
              >
                <LayoutDashboard size={20} />
                <span>Tổng quan dashboard</span>
              </NavLink>
              <NavLink
                to="/admin?tab=users"
                className={`nav-item ${adminTab === 'users' ? 'active' : ''}`}
              >
                <UserCog size={20} />
                <span>Quản lý tài khoản</span>
              </NavLink>
              <NavLink
                to="/admin?tab=transactions"
                className={`nav-item ${adminTab === 'transactions' ? 'active' : ''}`}
              >
                <ReceiptText size={20} />
                <span>Duyệt giao dịch</span>
              </NavLink>
              <NavLink
                to="/admin?tab=reports"
                className={`nav-item ${adminTab === 'reports' || adminTab === 'documents' ? 'active' : ''}`}
              >
                <ShieldAlert size={20} />
                <span>Kiểm duyệt nội dung</span>
              </NavLink>
              <NavLink
                to="/admin?tab=report-config"
                className={`nav-item ${adminTab === 'report-config' || adminTab === 'system-config' || adminTab === 'transfer-config' ? 'active' : ''}`}
              >
                <SlidersHorizontal size={20} />
                <span>Cấu hình hệ thống</span>
              </NavLink>
            </>
          ) : isModerator ? (
            <NavLink
              to="/moderator"
              className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
            >
              <ShieldAlert size={20} />
              <span>Kiểm duyệt nội dung</span>
            </NavLink>
          ) : (
            <>
              <NavLink
                to="/"
                end
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <FolderOpen size={20} />
                <span>Tài liệu</span>
              </NavLink>
              <NavLink
                to="/public-documents"
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <Globe2 size={20} />
                <span>Tài liệu công khai</span>
              </NavLink>
              <NavLink
                to="/chat"
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <Bot size={20} />
                <span>Trợ lý AI</span>
              </NavLink>
              <NavLink
                to="/friends"
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <Users size={20} />
                <span>Bạn bè</span>
              </NavLink>
            </>
          )}
        </nav>

        <button onClick={logout} className="logout-btn">
          <LogOut size={20} />
          <span>Đăng xuất</span>
        </button>
      </aside>

      {/* Main Content Area */}
      <main className="main-content">
        <Outlet />
      </main>

      <style>{`
        .layout-container {
          display: flex;
          min-height: 100vh;
          background-color: var(--bg-primary);
        }

        .sidebar {
          width: var(--sidebar-width);
          position: fixed;
          top: 1rem;
          bottom: 1rem;
          left: 1rem;
          display: flex;
          flex-direction: column;
          padding: 1.5rem 1rem;
          z-index: 100;
          border-radius: var(--radius-lg);
          border: 1px solid var(--border-neon);
        }

        .logo-section {
          position: relative;
          padding-bottom: 1.5rem;
          margin-bottom: 1.5rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
        }

        .logo-section h2 {
          font-size: 1.3rem;
          background: var(--accent-glow);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }

        .logo-glow {
          position: absolute;
          width: 60px;
          height: 10px;
          background: var(--accent-blue);
          filter: blur(15px);
          top: 0;
          left: 0;
        }

        .user-stats-card {
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid rgba(255, 255, 255, 0.05);
          border-radius: var(--radius-md);
          padding: 0.75rem 1rem;
          margin-bottom: 1.5rem;
        }

        .user-info {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 0.5rem;
        }
        .account-summary { display:block; color:inherit; text-decoration:none; }

        .username {
          display:flex;
          align-items:center;
          gap:.4rem;
          font-weight: 600;
          font-size: 0.95rem;
          max-width: 120px;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .badge {
          font-size: 0.7rem;
          padding: 0.2rem 0.5rem;
          border-radius: 4px;
          font-weight: 700;
        }

        .badge.free {
          background: rgba(255, 255, 255, 0.1);
          color: var(--text-secondary);
        }

        .badge.premium {
          background: rgba(157, 78, 221, 0.2);
          color: var(--accent-purple);
          border: 1px solid rgba(157, 78, 221, 0.3);
        }

        .badge.admin {
          background: rgba(0, 180, 216, 0.2);
          color: var(--accent-blue);
          border: 1px solid rgba(0, 180, 216, 0.3);
        }

        .badge.moderator {
          background: rgba(245, 158, 11, 0.16);
          color: #fbbf24;
          border: 1px solid rgba(245, 158, 11, 0.32);
        }

        .user-balance {
          display: flex;
          align-items: center;
          gap: 0.4rem;
          font-size: 0.9rem;
          color: var(--success);
          font-weight: 600;
        }
        .balance-actions { display:flex; align-items:center; justify-content:space-between; gap:.55rem; }
        .quick-deposit-link { flex:0 0 auto; padding:.32rem .55rem; border:1px solid rgba(16,185,129,.35); border-radius:7px; background:rgba(16,185,129,.1); color:var(--success); font-size:.72rem; font-weight:700; text-decoration:none; white-space:nowrap; transition:var(--transition-fast); }
        .quick-deposit-link:hover,.quick-deposit-link.active { background:rgba(16,185,129,.2); border-color:var(--success); transform:translateY(-1px); }
        .account-widget-links { display:grid; grid-template-columns:1fr 1fr; gap:.4rem; margin-top:.7rem; padding-top:.65rem; border-top:1px solid rgba(255,255,255,.07); }
        .account-widget-links a { display:flex; align-items:center; justify-content:center; gap:.35rem; padding:.42rem .3rem; border-radius:7px; color:var(--text-secondary); font-size:.72rem; text-align:center; }
        .account-widget-links a:hover,.account-widget-links a.active { background:rgba(157,78,221,.12); color:var(--accent-purple); }

        .admin-profile-link { display:inline-block; margin-top:.55rem; font-size:.78rem; color:var(--accent-blue); }

        .coin-icon {
          color: var(--success);
        }

        .nav-links {
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
          flex: 1;
        }

        .nav-item {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          padding: 0.75rem 1rem;
          color: var(--text-secondary);
          border-radius: var(--radius-sm);
          font-weight: 500;
          transition: var(--transition-fast);
        }

        .nav-item:hover {
          color: var(--text-primary);
          background: rgba(255, 255, 255, 0.03);
          transform: translateX(4px);
        }

        .nav-item.active {
          color: var(--text-primary);
          background: rgba(157, 78, 221, 0.12);
          border-left: 3px solid var(--accent-purple);
          box-shadow: inset 5px 0 10px rgba(157, 78, 221, 0.05);
        }

        .logout-btn {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          padding: 0.75rem 1rem;
          color: var(--danger);
          background: transparent;
          border: none;
          cursor: pointer;
          font-weight: 500;
          font-size: 1rem;
          border-radius: var(--radius-sm);
          transition: var(--transition-fast);
          margin-top: 1rem;
        }

        .logout-btn:hover {
          background: rgba(239, 68, 68, 0.08);
          transform: translateX(4px);
        }

        .main-content {
          margin-left: calc(var(--sidebar-width) + 2rem);
          flex: 1;
          padding: 2rem;
          min-height: 100vh;
        }

        @media (max-width: 768px) {
          .layout-container {
            flex-direction: column;
          }
          .sidebar {
            width: calc(100% - 2rem);
            position: relative;
            top: 1rem;
            bottom: auto;
            left: 1rem;
            margin-bottom: 1rem;
          }
          .main-content {
            margin-left: 0;
            padding: 1rem;
          }
        }
      `}</style>
    </div>
  );
};
