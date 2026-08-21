import React, { useState, useEffect } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { ModerationNoticePopup } from './ModerationNoticePopup';
import { NotificationBell } from './NotificationBell';
import {
  FolderOpen,
  Bot,
  Users,
  User as UserIcon,
  LogOut,
  Coins,
  Globe2,
  LayoutDashboard,
  UserCog,
  ReceiptText,
  ShieldAlert,
  SlidersHorizontal,
  Trash2,
  HardDrive,
  Sparkles,
  Cpu,
  ChevronRight,
} from 'lucide-react';

export const SidebarLayout: React.FC = () => {
  const { user, logout } = useAuth();
  const isAdmin = user?.role?.trim().toUpperCase() === 'ADMIN';
  const isModerator = user?.role?.trim().toUpperCase() === 'MODERATOR';
  const location = useLocation();

  const isCurrentAdminTab = (targetTab: string) => {
    if (location.pathname !== '/admin') return false;
    const search = new URLSearchParams(location.search);
    const currentTab = search.get('tab') || 'overview';
    if (targetTab === 'reports') {
      return currentTab === 'reports' || currentTab === 'documents';
    }
    if (targetTab === 'report-config') {
      return (
        currentTab === 'report-config' ||
        currentTab === 'system-config' ||
        currentTab === 'transfer-config'
      );
    }
    return currentTab === targetTab;
  };

  const [quota, setQuota] = useState<{
    usedStorageMb: number;
    maxStorageMb: number;
    tierName: string;
    aiPromptsToday: number;
    aiPromptLimitPerDay: number;
  } | null>(null);

  useEffect(() => {
    if (user && !isAdmin && !isModerator) {
      api.documentExtra
        .getStorageQuota()
        .then((res) => setQuota(res))
        .catch((err) => console.error('Failed to fetch storage quota:', err));
    }
  }, [user, location.pathname, isAdmin, isModerator]);

  return (
    <div className="layout-container">
      {user && !isAdmin && !isModerator && location.pathname !== '/notifications' && (
        <ModerationNoticePopup />
      )}
      {/* Sidebar Navigation */}
      <aside className="sidebar glass-panel">
        <div className="logo-section">
          <h2>AI Study Hub</h2>
          <span className="logo-glow"></span>
        </div>

        {/* User Quick Stats Card */}
        {user && (
          <div className="user-stats-card">
            <NavLink to="/profile" className="account-summary" title="Nhấn để xem hồ sơ">
              <div className="user-info">
                <p className="username">
                  <UserIcon size={18} />
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
              {isAdmin && (
                <div className="admin-status-indicator">
                  <span className="admin-status-dot"></span>
                  <span className="admin-role-text">Quản trị viên hệ thống</span>
                </div>
              )}
            </NavLink>
            {!isAdmin && !isModerator && (
              <NavLink to="/wallet" className="user-balance-card" title="Nhấn để xem số dư, nạp tiền và lịch sử giao dịch">
                <div className="balance-card-main">
                  <div className="user-balance">
                    <Coins size={16} className="coin-icon" />
                    <span>{user.balance.toLocaleString()}đ</span>
                  </div>
                  <ChevronRight size={14} className="balance-arrow" />
                </div>
                <span className="balance-hint-text">Xem chi tiết</span>
              </NavLink>
            )}
            {!isAdmin && !isModerator && (
              <div className="storage-widget">
                <div className="storage-widget-header">
                  <div className="storage-label">
                    <HardDrive size={14} className="storage-icon" />
                    <span>Dung lượng lưu trữ</span>
                  </div>
                  <span className="storage-percent-badge">
                    {quota
                      ? `${Math.min(100, Math.round(((quota.usedStorageMb || 0) / (quota.maxStorageMb || 50)) * 100))}%`
                      : '0%'}
                  </span>
                </div>

                <div className="storage-bar-bg">
                  <div
                    className="storage-bar-fill"
                    style={{
                      width: `${quota ? Math.min(100, ((quota.usedStorageMb || 0) / (quota.maxStorageMb || 50)) * 100) : 0}%`,
                      backgroundColor:
                        quota && ((quota.usedStorageMb || 0) / (quota.maxStorageMb || 50)) > 0.9
                          ? '#ef4444'
                          : quota && ((quota.usedStorageMb || 0) / (quota.maxStorageMb || 50)) > 0.7
                          ? '#f59e0b'
                          : '#10b981',
                    }}
                  />
                </div>

                <div className="storage-widget-footer">
                  <span className="storage-used-text">
                    {quota ? quota.usedStorageMb.toFixed(2) : '0.00'} MB / {quota ? quota.maxStorageMb : 50} MB
                  </span>
                  {quota && quota.maxStorageMb < 500 && (
                    <NavLink to="/premium" className="storage-upgrade-link">
                      <Sparkles size={11} /> Nâng cấp
                    </NavLink>
                  )}
                </div>
              </div>
            )}
          </div>
        )}
        <nav className="nav-links">
          {isAdmin ? (
            <>
              <NavLink
                to="/admin?tab=overview"
                className={() => `nav-item ${isCurrentAdminTab('overview') ? 'active' : ''}`}
              >
                <LayoutDashboard size={20} />
                <span>Tổng quan dashboard</span>
              </NavLink>
              <NavLink
                to="/admin?tab=users"
                className={() => `nav-item ${isCurrentAdminTab('users') ? 'active' : ''}`}
              >
                <UserCog size={20} />
                <span>Quản lý tài khoản</span>
              </NavLink>
              <NavLink
                to="/admin?tab=transactions"
                className={() => `nav-item ${isCurrentAdminTab('transactions') ? 'active' : ''}`}
              >
                <ReceiptText size={20} />
                <span>Duyệt giao dịch</span>
              </NavLink>
              <NavLink
                to="/admin?tab=reports"
                className={() =>
                  `nav-item ${
                    isCurrentAdminTab('reports') ||
                    isCurrentAdminTab('documents') ||
                    isCurrentAdminTab('audit-log')
                      ? 'active'
                      : ''
                  }`
                }
              >
                <ShieldAlert size={20} />
                <span>Kiểm duyệt nội dung</span>
              </NavLink>
              <NavLink
                to="/admin?tab=report-config"
                className={() => `nav-item ${isCurrentAdminTab('report-config') ? 'active' : ''}`}
              >
                <SlidersHorizontal size={20} />
                <span>Cấu hình hệ thống</span>
              </NavLink>
              <NavLink
                to="/admin?tab=ai-observability"
                className={() => `nav-item ${isCurrentAdminTab('ai-observability') ? 'active' : ''}`}
              >
                <Cpu size={20} />
                <span>Giám sát AI (Observability)</span>
              </NavLink>
            </>
          ) : isModerator ? (
            <>
              <NavLink
                to="/moderator"
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <ShieldAlert size={20} />
                <span>Kiểm duyệt nội dung</span>
              </NavLink>
              <NavLink
                to="/public-documents"
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <Globe2 size={20} />
                <span>Tài liệu công khai</span>
              </NavLink>
            </>
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
              <NavLink
                to="/trash"
                className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
              >
                <Trash2 size={20} />
                <span>Thùng rác</span>
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
        {location.pathname !== '/notifications' && <NotificationBell />}
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
          padding: 1.25rem 0.85rem;
          z-index: 100;
          border-radius: var(--radius-lg);
          border: 1px solid var(--border-neon);
          box-sizing: border-box;
          overflow: hidden;
        }

        .logo-section {
          position: relative;
          padding-bottom: 1.25rem;
          margin-bottom: 1.25rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.06);
          flex-shrink: 0;
        }

        .logo-section h2 {
          font-size: 1.35rem;
          font-weight: 700;
          background: linear-gradient(135deg, #00d2ff 0%, #3a7bd5 100%);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
          margin: 0;
          letter-spacing: -0.01em;
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
          background: rgba(255, 255, 255, 0.035);
          border: 1px solid rgba(255, 255, 255, 0.07);
          border-radius: var(--radius-md);
          padding: 0.85rem 0.95rem;
          margin-bottom: 1.25rem;
          flex-shrink: 0;
          transition: var(--transition-fast);
        }

        .user-info {
          display: flex;
          justify-content: space-between;
          align-items: center;
        }
        .account-summary { display:block; color:inherit; text-decoration:none; }

        .username {
          display:flex;
          align-items:center;
          gap:.45rem;
          font-weight: 700;
          font-size: 0.98rem;
          min-width: 0;
          flex: 1 1 auto;
          overflow: hidden;
          color: var(--text-primary);
        }

        .username span {
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .admin-status-indicator {
          display: flex;
          align-items: center;
          gap: 0.45rem;
          margin-top: 0.5rem;
          padding-top: 0.5rem;
          border-top: 1px solid rgba(255, 255, 255, 0.06);
        }

        .admin-status-dot {
          width: 7px;
          height: 7px;
          border-radius: 50%;
          background: #10b981;
          box-shadow: 0 0 8px rgba(16, 185, 129, 0.7);
        }

        .admin-role-text {
          font-size: 0.76rem;
          color: var(--text-muted);
          font-weight: 500;
        }

        .badge {
          font-size: 0.7rem;
          padding: 0.2rem 0.55rem;
          border-radius: 6px;
          font-weight: 800;
          letter-spacing: 0.04em;
          flex-shrink: 0;
          white-space: nowrap;
        }

        .badge.free {
          background: rgba(255, 255, 255, 0.08);
          color: var(--text-secondary);
          border: 1px solid rgba(255, 255, 255, 0.12);
        }

        .badge.premium {
          background: rgba(157, 78, 221, 0.2);
          color: #c084fc;
          border: 1px solid rgba(157, 78, 221, 0.35);
        }

        .badge.admin {
          background: rgba(0, 180, 216, 0.2);
          color: #38bdf8;
          border: 1px solid rgba(0, 180, 216, 0.35);
        }

        .badge.moderator {
          background: rgba(245, 158, 11, 0.18);
          color: #fbbf24;
          border: 1px solid rgba(245, 158, 11, 0.35);
        }

        .user-balance {
          display: flex;
          align-items: center;
          gap: 0.4rem;
          font-size: 0.9rem;
          color: var(--success);
          font-weight: 600;
        }
        .user-balance-card {
          display: flex;
          flex-direction: column;
          gap: 0.25rem;
          padding: 0.52rem 0.65rem;
          border-radius: 8px;
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid rgba(255, 255, 255, 0.07);
          text-decoration: none;
          transition: var(--transition-fast);
          cursor: pointer;
        }
        .user-balance-card:hover, .user-balance-card.active {
          background: rgba(16, 185, 129, 0.08);
          border-color: rgba(16, 185, 129, 0.25);
          transform: translateY(-1px);
        }
        .balance-card-main {
          display: flex;
          align-items: center;
          justify-content: space-between;
        }
        .balance-card-main .user-balance {
          display: flex;
          align-items: center;
          gap: 0.45rem;
          font-weight: 700;
          font-size: 0.95rem;
          color: var(--success);
        }
        .balance-card-main .coin-icon {
          color: var(--success);
        }
        .balance-arrow {
          color: var(--text-muted);
          transition: transform 0.2s ease, color 0.2s ease;
        }
        .user-balance-card:hover .balance-arrow {
          color: var(--success);
          transform: translateX(2px);
        }
        .balance-hint-text {
          font-size: 0.68rem;
          color: var(--text-muted);
          line-height: 1.25;
        }
        .user-balance-card:hover .balance-hint-text {
          color: rgba(255, 255, 255, 0.7);
        }

        .storage-widget {
          margin-top: 0.65rem;
          padding-top: 0.65rem;
          border-top: 1px solid rgba(255, 255, 255, 0.07);
          display: flex;
          flex-direction: column;
          gap: 0.4rem;
        }

        .storage-widget-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          font-size: 0.75rem;
        }

        .storage-label {
          display: flex;
          align-items: center;
          gap: 0.35rem;
          color: var(--text-secondary);
          font-weight: 500;
        }

        .storage-icon {
          color: var(--accent-purple);
        }

        .storage-percent-badge {
          font-weight: 700;
          font-size: 0.72rem;
          color: var(--accent-purple);
        }

        .storage-bar-bg {
          width: 100%;
          height: 6px;
          background: rgba(255, 255, 255, 0.08);
          border-radius: 999px;
          overflow: hidden;
        }

        .storage-bar-fill {
          height: 100%;
          border-radius: 999px;
          transition: width 0.4s ease, background-color 0.3s ease;
        }

        .storage-widget-footer {
          display: flex;
          justify-content: space-between;
          align-items: center;
          font-size: 0.7rem;
        }

        .storage-used-text {
          color: var(--text-muted);
        }

        .storage-upgrade-link {
          display: inline-flex;
          align-items: center;
          gap: 0.2rem;
          color: var(--accent-purple);
          font-weight: 600;
          text-decoration: none;
          transition: var(--transition-fast);
        }

        .storage-upgrade-link:hover {
          color: #a5b4fc;
          text-decoration: underline;
        }

        .user-stats-card {
          background: rgba(255, 255, 255, 0.035);
          border: 1px solid rgba(255, 255, 255, 0.07);
          border-radius: var(--radius-md);
          padding: 0.85rem 0.95rem;
          margin-bottom: 1.25rem;
          flex-shrink: 0;
          transition: all 0.25s ease;
          cursor: pointer;
        }

        .user-stats-card:hover {
          background: rgba(255, 255, 255, 0.06);
          border-color: rgba(0, 180, 216, 0.25);
          transform: translateY(-2px);
          box-shadow: 0 6px 20px rgba(0, 0, 0, 0.25);
        }

        .coin-icon {
          color: var(--success);
        }

        .nav-links {
          display: flex;
          flex-direction: column;
          gap: 0.45rem;
          flex: 1;
          min-height: 0;
          overflow-y: auto;
          padding-right: 4px;
        }

        .nav-item {
          display: flex;
          align-items: center;
          gap: 0.8rem;
          padding: 0.75rem 1rem;
          color: #94a3b8;
          border-radius: var(--radius-sm);
          font-weight: 500;
          font-size: 0.92rem;
          border: 1px solid transparent;
          border-left: 3.5px solid transparent;
          transition: all 0.22s cubic-bezier(0.4, 0, 0.2, 1);
          flex-shrink: 0;
          text-decoration: none;
          position: relative;
        }

        .nav-item svg {
          color: inherit;
          transition: transform 0.22s ease, color 0.22s ease, filter 0.22s ease;
          flex-shrink: 0;
        }

        .nav-item:hover {
          color: #ffffff;
          background: rgba(157, 78, 221, 0.1);
          border-color: rgba(157, 78, 221, 0.25);
          border-left-color: rgba(192, 132, 252, 0.5);
          transform: translateX(5px);
          box-shadow: 0 4px 14px rgba(0, 0, 0, 0.2);
        }

        .nav-item:hover svg {
          color: #c084fc;
          transform: scale(1.12);
          filter: drop-shadow(0 0 6px rgba(192, 132, 252, 0.4));
        }

        .nav-item.active {
          color: #ffffff;
          font-weight: 600;
          background: rgba(157, 78, 221, 0.18);
          border-color: rgba(157, 78, 221, 0.3);
          border-left: 3.5px solid #c084fc;
          box-shadow: inset 8px 0 16px rgba(157, 78, 221, 0.1), 0 4px 16px rgba(157, 78, 221, 0.12);
        }

        .nav-item.active svg {
          color: #c084fc;
          filter: drop-shadow(0 0 6px rgba(192, 132, 252, 0.5));
        }

        .logout-btn {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          padding: 0.75rem 1.1rem;
          color: #f87171;
          background: rgba(239, 68, 68, 0.08);
          border: 1px solid rgba(239, 68, 68, 0.22);
          cursor: pointer;
          font-weight: 600;
          font-size: 0.92rem;
          border-radius: 10px;
          transition: all 0.22s cubic-bezier(0.4, 0, 0.2, 1);
          margin-top: 0.85rem;
          flex-shrink: 0;
        }

        .logout-btn svg {
          transition: transform 0.22s ease, filter 0.22s ease;
        }

        .logout-btn:hover {
          background: rgba(239, 68, 68, 0.2);
          border-color: rgba(239, 68, 68, 0.45);
          color: #ffffff;
          transform: translateX(5px);
          box-shadow: 0 4px 16px rgba(239, 68, 68, 0.2);
        }

        .logout-btn:hover svg {
          transform: scale(1.12) translateX(2px);
          filter: drop-shadow(0 0 6px rgba(239, 68, 68, 0.6));
        }

        .main-content {
          margin-left: calc(var(--sidebar-width) + 2rem);
          flex: 1;
          padding: 2rem;
          min-height: 100vh;
          position: relative;
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
