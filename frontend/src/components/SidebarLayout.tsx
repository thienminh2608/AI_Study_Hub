import React from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { 
  FolderOpen, 
  Bot, 
  Users, 
  Wallet, 
  Award, 
  User as UserIcon, 
  Settings, 
  LogOut, 
  Coins 
} from 'lucide-react';

export const SidebarLayout: React.FC = () => {
  const { user, logout } = useAuth();

  return (
    <div className="layout-container">
      {/* Sidebar Navigation */}
      <aside className="sidebar glass-panel">
        <div className="logo-section">
          <h2>AI Study Hub</h2>
          <span className="logo-glow"></span>
        </div>

        {/* User Quick Stats Card */}
        {user && (
          <div className="user-stats-card">
            <div className="user-info">
              <p className="username">{user.username}</p>
              <span className={`badge ${user.role === 'ADMIN' ? 'admin' : user.tierId === 3 ? 'premium' : 'free'}`}>
                {user.role === 'ADMIN' ? 'ADMIN' : user.tierId === 3 ? 'PREMIUM' : 'FREE'}
              </span>
            </div>
            <div className="user-balance">
              <Coins size={16} className="coin-icon" />
              <span>{user.balance.toLocaleString()}đ</span>
            </div>
          </div>
        )}

        <nav className="nav-links">
          <NavLink to="/" end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <FolderOpen size={20} />
            <span>Tài liệu</span>
          </NavLink>
          <NavLink to="/chat" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Bot size={20} />
            <span>Trợ lý AI</span>
          </NavLink>
          <NavLink to="/friends" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Users size={20} />
            <span>Bạn bè</span>
          </NavLink>
          <NavLink to="/wallet" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Wallet size={20} />
            <span>Ví tiền</span>
          </NavLink>
          <NavLink to="/premium" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <Award size={20} />
            <span>Premium</span>
          </NavLink>
          <NavLink to="/profile" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            <UserIcon size={20} />
            <span>Tài khoản</span>
          </NavLink>

          {user?.role === 'ADMIN' && (
            <NavLink to="/admin" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
              <Settings size={20} />
              <span>Quản trị viên</span>
            </NavLink>
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

        .username {
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

        .user-balance {
          display: flex;
          align-items: center;
          gap: 0.4rem;
          font-size: 0.9rem;
          color: var(--success);
          font-weight: 600;
        }

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
