import React, { useState, useEffect } from 'react';
import { api } from '../services/api';
import { 
  Users, 
  FileText, 
  AlertOctagon, 
  Loader, 
  Check, 
  X, 
  Edit, 
  Trash2, 
  DollarSign 
} from 'lucide-react';

interface UserItem {
  userId: number;
  username: string;
  email: string;
  role: string;
  tierId: number;
  tierName: string;
  balance: number;
  status: string; // "ACTIVE" | "SUSPENDED"
  createdAt?: string;
}

interface TransactionItem {
  transactionId: number;
  userId: number;
  username: string;
  amount: number;
  type: string; // "DEPOSIT" | "WITHDRAW"
  status: string; // "PENDING" | "SUCCESS" | "CANCELLED"
  startedAt?: string;
}

interface ReportItem {
  reportId: number;
  documentId: number;
  documentTitle: string;
  reporterId: number;
  reporterName: string;
  reasonCode: string;
  additionalDetails?: string;
  status: string; // "PENDING" | "RESOLVED"
  createdAt?: string;
}

export const AdminDashboard: React.FC = () => {
  const [adminTab, setAdminTab] = useState<'overview' | 'users' | 'transactions' | 'reports'>('overview');
  
  // States
  const [stats, setStats] = useState<any>({ totalUsers: 0, totalTransactions: 0, totalDocuments: 0, totalReports: 0 });
  const [users, setUsers] = useState<UserItem[]>([]);
  const [transactions, setTransactions] = useState<TransactionItem[]>([]);
  const [reports, setReports] = useState<ReportItem[]>([]);
  const [loading, setLoading] = useState(true);

  // Edit User Modal
  const [showEditUserModal, setShowEditUserModal] = useState(false);
  const [editingUser, setEditingUser] = useState<UserItem | null>(null);
  const [editUsername, setEditUsername] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editRole, setEditRole] = useState('STUDENT');
  const [editStatus, setEditStatus] = useState('ACTIVE');
  const [editBalance, setEditBalance] = useState(0);
  const [editTierId, setEditTierId] = useState(2);
  const [updatingUser, setUpdatingUser] = useState(false);

  const loadDashboardData = async () => {
    setLoading(true);
    try {
      if (adminTab === 'overview') {
        const data = await api.admin.getDashboard();
        setStats(data);
      } else if (adminTab === 'users') {
        const data = await api.admin.getUsers();
        setUsers(data as UserItem[]);
      } else if (adminTab === 'transactions') {
        const data = await api.admin.getTransactions();
        setTransactions(data as TransactionItem[]);
      } else if (adminTab === 'reports') {
        const data = await api.admin.getReports();
        setReports(data as ReportItem[]);
      }
    } catch (err: any) {
      console.error('Error loading admin page data:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadDashboardData();
  }, [adminTab]);

  // Edit User Actions
  const handleOpenEditUser = (u: UserItem) => {
    setEditingUser(u);
    setEditUsername(u.username);
    setEditEmail(u.email);
    setEditRole(u.role);
    setEditStatus(u.status);
    setEditBalance(u.balance);
    setEditTierId(u.tierId);
    setShowEditUserModal(true);
  };

  const handleUpdateUserSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingUser) return;

    setUpdatingUser(true);
    try {
      await api.admin.updateUser(editingUser.userId, {
        username: editUsername.trim(),
        email: editEmail.trim(),
        role: editRole,
        status: editStatus,
        balance: editBalance,
        tierId: editTierId
      });
      setShowEditUserModal(false);
      loadDashboardData();
      alert('Cập nhật người dùng thành công.');
    } catch (err: any) {
      alert(err.message || 'Cập nhật thất bại.');
    } finally {
      setUpdatingUser(false);
      setEditingUser(null);
    }
  };

  const handleDeleteUser = async (userId: number) => {
    if (!window.confirm('Bạn có chắc chắn muốn xóa vĩnh viễn sinh viên này cùng mọi dữ liệu liên quan?')) return;
    try {
      await api.admin.deleteUser(userId);
      loadDashboardData();
      alert('Đã xóa người dùng thành công.');
    } catch (err: any) {
      alert(err.message || 'Xóa người dùng thất bại.');
    }
  };

  // Transaction Approval Actions
  const handleApproveTransaction = async (txId: number, status: 'SUCCESS' | 'CANCELLED') => {
    const actionText = status === 'SUCCESS' ? 'Duyệt thành công' : 'Hủy bỏ';
    if (!window.confirm(`Xác nhận ${actionText} giao dịch này?`)) return;

    try {
      await api.admin.updateTransaction(txId, status);
      loadDashboardData();
      alert('Giao dịch đã được cập nhật.');
    } catch (err: any) {
      alert(err.message || 'Thao tác giao dịch thất bại.');
    }
  };

  // Report Resolution Actions
  const handleResolveReport = async (reportId: number, action: 'DELETE' | 'PRIVATE' | 'DISMISS') => {
    const actionText = action === 'DELETE' ? 'Xóa tài liệu' : action === 'PRIVATE' ? 'Chuyển về Riêng tư' : 'Bỏ qua báo cáo';
    if (!window.confirm(`Xác nhận xử lý báo cáo: ${actionText}?`)) return;

    try {
      await api.admin.resolveReport(reportId, action);
      loadDashboardData();
      alert('Đã giải quyết báo cáo.');
    } catch (err: any) {
      alert(err.message || 'Xử lý báo cáo thất bại.');
    }
  };

  return (
    <div className="admin-container">
      {/* Admin Title */}
      <div className="admin-header">
        <h1>Bảng Điều Khiển Admin</h1>
        <p>Quản trị hệ thống, phê duyệt thanh toán ví và kiểm duyệt tài liệu vi phạm.</p>
      </div>

      <div className="admin-layout-grid">
        
        {/* Navigation Sidebar */}
        <aside className="admin-nav glass-panel">
          <button onClick={() => setAdminTab('overview')} className={`admin-nav-item ${adminTab === 'overview' ? 'active' : ''}`}>
            Tổng quan
          </button>
          <button onClick={() => setAdminTab('users')} className={`admin-nav-item ${adminTab === 'users' ? 'active' : ''}`}>
            Quản lý Sinh viên
          </button>
          <button onClick={() => setAdminTab('transactions')} className={`admin-nav-item ${adminTab === 'transactions' ? 'active' : ''}`}>
            Duyệt Giao dịch
          </button>
          <button onClick={() => setAdminTab('reports')} className={`admin-nav-item ${adminTab === 'reports' ? 'active' : ''}`}>
            Báo cáo Vi phạm
          </button>
        </aside>

        {/* Content Panel */}
        <div className="admin-content-pane glass-panel">
          {loading ? (
            <div className="admin-loader">
              <Loader className="spin" size={32} />
              <p>Đang tải dữ liệu quản trị...</p>
            </div>
          ) : adminTab === 'overview' ? (
            <div className="overview-pane animate-fade-in">
              <h3>Tổng quan hệ thống</h3>
              
              <div className="stats-grid">
                <div className="stat-box glass-card">
                  <Users size={28} className="stat-icon purple" />
                  <div className="stat-details">
                    <span className="stat-label">Tổng Sinh viên</span>
                    <span className="stat-value">{stats.totalUsers}</span>
                  </div>
                </div>
                <div className="stat-box glass-card">
                  <DollarSign size={28} className="stat-icon green" />
                  <div className="stat-details">
                    <span className="stat-label">Yêu cầu Giao dịch</span>
                    <span className="stat-value">{stats.totalTransactions}</span>
                  </div>
                </div>
                <div className="stat-box glass-card">
                  <FileText size={28} className="stat-icon blue" />
                  <div className="stat-details">
                    <span className="stat-label">Tài liệu tải lên</span>
                    <span className="stat-value">{stats.totalDocuments}</span>
                  </div>
                </div>
                <div className="stat-box glass-card">
                  <AlertOctagon size={28} className="stat-icon red" />
                  <div className="stat-details">
                    <span className="stat-label">Báo cáo vi phạm</span>
                    <span className="stat-value">{stats.totalReports}</span>
                  </div>
                </div>
              </div>
            </div>
          ) : adminTab === 'users' ? (
            <div className="users-pane animate-fade-in">
              <h3>Quản lý Sinh viên</h3>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>Sinh viên</th>
                      <th>Email</th>
                      <th>Vai trò</th>
                      <th>Membership</th>
                      <th>Số dư</th>
                      <th>Trạng thái</th>
                      <th>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {users.map(u => (
                      <tr key={u.userId}>
                        <td className="monospace-text">#{u.userId}</td>
                        <td className="bold-text">{u.username}</td>
                        <td>{u.email}</td>
                        <td>
                          <span className={`role-badge ${u.role}`}>
                            {u.role}
                          </span>
                        </td>
                        <td>{u.tierName}</td>
                        <td className="balance-text">{u.balance.toLocaleString()}đ</td>
                        <td>
                          <span className={`status-badge ${u.status}`}>
                            {u.status === 'ACTIVE' ? 'Hoạt động' : 'Đang khóa'}
                          </span>
                        </td>
                        <td>
                          <div className="table-actions">
                            <button onClick={() => handleOpenEditUser(u)} className="action-btn edit" title="Sửa thông tin">
                              <Edit size={14} />
                            </button>
                            <button onClick={() => handleDeleteUser(u.userId)} className="action-btn delete" title="Xóa người dùng">
                              <Trash2 size={14} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : adminTab === 'transactions' ? (
            <div className="txs-pane animate-fade-in">
              <h3>Phê duyệt Giao dịch</h3>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>Sinh viên</th>
                      <th>Loại</th>
                      <th>Số tiền</th>
                      <th>Trạng thái</th>
                      <th>Thời gian tạo</th>
                      <th>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {transactions.map(tx => {
                      const isPending = tx.status === 'PENDING';
                      return (
                        <tr key={tx.transactionId}>
                          <td className="monospace-text">#{tx.transactionId}</td>
                          <td className="bold-text">{tx.username}</td>
                          <td>
                            <span className={`tx-type-badge ${tx.type}`}>
                              {tx.type === 'DEPOSIT' ? 'Nạp tiền' : 'Rút tiền'}
                            </span>
                          </td>
                          <td className={`tx-value ${tx.amount > 0 ? 'positive' : 'negative'}`}>
                            {tx.amount > 0 ? '+' : ''}{tx.amount.toLocaleString()}đ
                          </td>
                          <td>
                            <span className={`tx-status-badge ${tx.status}`}>
                              {tx.status}
                            </span>
                          </td>
                          <td>{tx.startedAt ? new Date(tx.startedAt).toLocaleString() : 'N/A'}</td>
                          <td>
                            {isPending ? (
                              <div className="table-actions">
                                <button 
                                  onClick={() => handleApproveTransaction(tx.transactionId, 'SUCCESS')} 
                                  className="action-btn approve" 
                                  title="Duyệt giao dịch"
                                >
                                  <Check size={14} />
                                </button>
                                <button 
                                  onClick={() => handleApproveTransaction(tx.transactionId, 'CANCELLED')} 
                                  className="action-btn reject" 
                                  title="Từ chối giao dịch"
                                >
                                  <X size={14} />
                                </button>
                              </div>
                            ) : (
                              <span className="text-muted">Đã xử lý</span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          ) : (
            <div className="reports-pane animate-fade-in">
              <h3>Báo cáo tài liệu vi phạm</h3>
              
              {reports.length === 0 ? (
                <div className="empty-reports">
                  <AlertOctagon size={48} className="empty-icon" />
                  <p>Không có báo cáo vi phạm nào đang chờ xử lý.</p>
                </div>
              ) : (
                <div className="reports-list">
                  {reports.map(r => (
                    <div key={r.reportId} className="report-card glass-card">
                      <div className="report-info-header">
                        <div>
                          <h4>Tài liệu: <strong>{r.documentTitle}</strong> (ID #{r.documentId})</h4>
                          <p className="reporter-desc">Người báo cáo: {r.reporterName} (ID #{r.reporterId})</p>
                        </div>
                        <span className={`reason-badge ${r.reasonCode}`}>
                          {r.reasonCode}
                        </span>
                      </div>
                      
                      {r.additionalDetails && (
                        <div className="report-details-box">
                          <strong>Mô tả chi tiết:</strong>
                          <p>{r.additionalDetails}</p>
                        </div>
                      )}

                      <div className="report-card-actions">
                        <button 
                          onClick={() => handleResolveReport(r.reportId, 'DELETE')} 
                          className="btn-secondary danger-hover"
                        >
                          Xóa tài liệu (Delete)
                        </button>
                        <button 
                          onClick={() => handleResolveReport(r.reportId, 'PRIVATE')} 
                          className="btn-secondary"
                        >
                          Chuyển Riêng tư (Private)
                        </button>
                        <button 
                          onClick={() => handleResolveReport(r.reportId, 'DISMISS')} 
                          className="btn-primary"
                        >
                          Bỏ qua (Dismiss)
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Modal: Edit User */}
      {showEditUserModal && (
        <div className="modal-overlay">
          <div className="modal-box glass-panel animate-slide-up">
            <h3>Sửa thông tin người dùng</h3>
            <form onSubmit={handleUpdateUserSubmit} className="admin-form">
              <div className="form-group">
                <label>Tên người dùng</label>
                <input
                  type="text"
                  value={editUsername}
                  onChange={(e) => setEditUsername(e.target.value)}
                  className="input-control"
                  required
                />
              </div>

              <div className="form-group">
                <label>Địa chỉ Email</label>
                <input
                  type="email"
                  value={editEmail}
                  onChange={(e) => setEditEmail(e.target.value)}
                  className="input-control"
                  required
                />
              </div>

              <div className="form-group">
                <label>Số dư ví (VNĐ)</label>
                <input
                  type="number"
                  value={editBalance}
                  onChange={(e) => setEditBalance(parseInt(e.target.value) || 0)}
                  className="input-control"
                  required
                />
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Vai trò</label>
                  <select value={editRole} onChange={(e) => setEditRole(e.target.value)} className="input-control">
                    <option value="STUDENT">STUDENT</option>
                    <option value="ADMIN">ADMIN</option>
                  </select>
                </div>

                <div className="form-group">
                  <label>Membership</label>
                  <select value={editTierId} onChange={(e) => setEditTierId(parseInt(e.target.value))} className="input-control">
                    <option value={2}>Free Tier</option>
                    <option value={3}>Premium Tier</option>
                    <option value={1}>Guest Tier</option>
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>Trạng thái hoạt động</label>
                <select value={editStatus} onChange={(e) => setEditStatus(e.target.value)} className="input-control">
                  <option value="ACTIVE">ACTIVE (Hoạt động)</option>
                  <option value="SUSPENDED">SUSPENDED (Tạm khóa)</option>
                </select>
              </div>

              <div className="modal-actions">
                <button type="button" onClick={() => setShowEditUserModal(false)} className="btn-secondary">Hủy</button>
                <button type="submit" className="btn-primary" disabled={updatingUser}>
                  {updatingUser ? <Loader className="spin" size={16} /> : 'Lưu lại'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <style>{`
        .admin-container {
          min-height: 80vh;
        }

        .admin-header {
          margin-bottom: 2rem;
        }

        .admin-header h1 {
          font-size: 2rem;
          margin-bottom: 0.25rem;
          background: var(--accent-glow);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }

        .admin-header p {
          color: var(--text-secondary);
          font-size: 0.95rem;
        }

        .admin-layout-grid {
          display: grid;
          grid-template-columns: 220px 1fr;
          gap: 1.5rem;
          height: calc(100vh - 12rem);
        }

        .admin-nav {
          display: flex;
          flex-direction: column;
          padding: 1.25rem;
          border-radius: var(--radius-md);
          gap: 0.5rem;
        }

        .admin-nav-item {
          width: 100%;
          text-align: left;
          background: transparent;
          border: none;
          color: var(--text-secondary);
          cursor: pointer;
          font-weight: 600;
          font-size: 0.95rem;
          padding: 0.75rem 1rem;
          border-radius: var(--radius-sm);
          transition: var(--transition-fast);
        }

        .admin-nav-item:hover {
          color: var(--text-primary);
          background: rgba(255, 255, 255, 0.03);
        }

        .admin-nav-item.active {
          color: var(--accent-blue);
          background: rgba(0, 180, 216, 0.08);
          border-left: 3px solid var(--accent-blue);
        }

        .admin-content-pane {
          flex: 1;
          display: flex;
          flex-direction: column;
          padding: 1.5rem;
          border-radius: var(--radius-md);
          overflow: hidden;
        }

        .admin-content-pane h3 {
          font-size: 1.1rem;
          margin-bottom: 1.25rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          padding-bottom: 0.5rem;
        }

        .admin-loader {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 100%;
          color: var(--text-muted);
          gap: 0.75rem;
        }

        /* Overview Page */
        .stats-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
          gap: 1.25rem;
        }

        .stat-box {
          display: flex;
          align-items: center;
          gap: 1rem;
          padding: 1.5rem;
        }

        .stat-icon {
          flex-shrink: 0;
          padding: 0.4rem;
          background: rgba(255, 255, 255, 0.03);
          border-radius: var(--radius-sm);
        }

        .stat-icon.purple {
          color: var(--accent-purple);
          border: 1px solid rgba(157, 78, 221, 0.2);
        }

        .stat-icon.green {
          color: var(--success);
          border: 1px solid rgba(16, 185, 129, 0.2);
        }

        .stat-icon.blue {
          color: var(--accent-blue);
          border: 1px solid rgba(0, 180, 216, 0.2);
        }

        .stat-icon.red {
          color: var(--danger);
          border: 1px solid rgba(239, 68, 68, 0.2);
        }

        .stat-details {
          display: flex;
          flex-direction: column;
          gap: 0.15rem;
        }

        .stat-label {
          font-size: 0.8rem;
          color: var(--text-muted);
          font-weight: 500;
        }

        .stat-value {
          font-size: 1.5rem;
          font-weight: 700;
          color: var(--text-primary);
        }

        /* Tables & Lists */
        .table-scroll {
          width: 100%;
          overflow-y: auto;
          flex: 1;
        }

        .admin-table {
          width: 100%;
          border-collapse: collapse;
          text-align: left;
        }

        .admin-table th {
          padding: 0.75rem 1rem;
          font-size: 0.8rem;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--text-muted);
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
        }

        .admin-table td {
          padding: 0.85rem 1rem;
          font-size: 0.85rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.03);
          color: var(--text-primary);
        }

        .monospace-text {
          font-family: monospace;
          color: var(--accent-blue);
        }

        .bold-text {
          font-weight: 600;
        }

        .balance-text {
          color: var(--success);
          font-weight: 600;
        }

        .role-badge, .tx-type-badge {
          font-size: 0.7rem;
          padding: 0.15rem 0.4rem;
          border-radius: 4px;
          font-weight: 700;
        }

        .role-badge.ADMIN {
          background: rgba(0, 180, 216, 0.15);
          color: var(--accent-blue);
        }

        .role-badge.STUDENT {
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-secondary);
        }

        .tx-type-badge.DEPOSIT {
          background: rgba(16, 185, 129, 0.15);
          color: var(--success);
        }

        .tx-type-badge.WITHDRAW {
          background: rgba(239, 68, 68, 0.15);
          color: var(--danger);
        }

        .status-badge, .tx-status-badge {
          font-size: 0.75rem;
          font-weight: 600;
        }

        .status-badge.ACTIVE, .tx-status-badge.SUCCESS {
          color: var(--success);
        }

        .status-badge.SUSPENDED, .tx-status-badge.CANCELLED {
          color: var(--danger);
        }

        .tx-status-badge.PENDING {
          color: var(--warning);
        }

        .tx-value.positive {
          color: var(--success);
          font-weight: 600;
        }

        .tx-value.negative {
          color: var(--danger);
          font-weight: 600;
        }

        .table-actions {
          display: flex;
          gap: 0.4rem;
        }

        .action-btn {
          background: transparent;
          border: 1px solid rgba(255, 255, 255, 0.1);
          color: var(--text-muted);
          cursor: pointer;
          width: 28px;
          height: 28px;
          border-radius: 4px;
          display: flex;
          justify-content: center;
          align-items: center;
          transition: var(--transition-fast);
        }

        .action-btn.edit:hover {
          color: var(--accent-blue);
          border-color: rgba(0, 180, 216, 0.3);
          background: rgba(0, 180, 216, 0.05);
        }

        .action-btn.delete:hover {
          color: var(--danger);
          border-color: rgba(239, 68, 68, 0.3);
          background: rgba(239, 68, 68, 0.05);
        }

        .action-btn.approve:hover {
          color: var(--success);
          border-color: rgba(16, 185, 129, 0.3);
          background: rgba(16, 185, 129, 0.05);
        }

        .action-btn.reject:hover {
          color: var(--danger);
          border-color: rgba(239, 68, 68, 0.3);
          background: rgba(239, 68, 68, 0.05);
        }

        /* Abuse Reports List */
        .reports-list {
          display: flex;
          flex-direction: column;
          gap: 1rem;
          flex: 1;
          overflow-y: auto;
        }

        .empty-reports {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 250px;
          color: var(--text-muted);
          gap: 0.5rem;
        }

        .report-card {
          display: flex;
          flex-direction: column;
          gap: 1rem;
          padding: 1.25rem 1.5rem;
        }

        .report-info-header {
          display: flex;
          justify-content: space-between;
          align-items: flex-start;
        }

        .report-info-header h4 {
          font-size: 1rem;
          color: var(--text-primary);
        }

        .reporter-desc {
          font-size: 0.8rem;
          color: var(--text-muted);
          margin-top: 0.15rem;
        }

        .reason-badge {
          font-size: 0.75rem;
          font-weight: 700;
          padding: 0.2rem 0.5rem;
          border-radius: 4px;
          text-transform: uppercase;
        }

        .reason-badge.COPYRIGHT {
          background: rgba(245, 158, 11, 0.15);
          color: var(--warning);
          border: 1px solid rgba(245, 158, 11, 0.25);
        }

        .reason-badge.INAPPROPRIATE {
          background: rgba(239, 68, 68, 0.15);
          color: var(--danger);
          border: 1px solid rgba(239, 68, 68, 0.25);
        }

        .reason-badge.SPAM {
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-secondary);
        }

        .report-details-box {
          background: rgba(0, 0, 0, 0.15);
          border: 1px solid rgba(255, 255, 255, 0.03);
          border-radius: var(--radius-sm);
          padding: 0.75rem 1rem;
          font-size: 0.85rem;
        }

        .report-details-box p {
          color: var(--text-secondary);
          margin-top: 0.25rem;
          line-height: 1.4;
        }

        .report-card-actions {
          display: flex;
          justify-content: flex-end;
          gap: 0.5rem;
          border-top: 1px solid rgba(255, 255, 255, 0.05);
          padding-top: 0.75rem;
        }

        .admin-form {
          display: flex;
          flex-direction: column;
          gap: 1rem;
        }

        .form-row {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 1rem;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        @media (max-width: 768px) {
          .admin-layout-grid {
            grid-template-columns: 1fr;
            height: auto;
          }
        }
      `}</style>
    </div>
  );
};
