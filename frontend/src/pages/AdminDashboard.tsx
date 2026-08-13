import React, { useState, useEffect, useMemo } from 'react';
import { NavLink, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../services/api';
import { FileTypeIcon } from '../components/FileTypeIcon';
import { AdminConfiguration } from './AdminConfiguration';
import { useUiFeedback } from '../context/UiFeedbackContext';
import {
  Users,
  FileText,
  Eye,
  AlertOctagon,
  Loader,
  Check,
  X,
  Edit,
  DollarSign,
  Plus,
  Lock,
  Unlock,
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

const Pagination: React.FC<{
  page: number;
  totalPages: number;
  total: number;
  setPage: (page: number) => void;
}> = ({ page, totalPages, total, setPage }) => (
  <div className="admin-pagination">
    <small>{total} kết quả</small>
    <div>
      <button className="btn-secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>
        Trước
      </button>
      <span>
        {page}/{totalPages}
      </span>
      <button
        className="btn-secondary"
        disabled={page >= totalPages}
        onClick={() => setPage(page + 1)}
      >
        Sau
      </button>
    </div>
  </div>
);

export const AdminDashboard: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const requestedTab = searchParams.get('tab');
  const adminTab =
    requestedTab === 'users' ||
    requestedTab === 'transactions' ||
    requestedTab === 'reports' ||
    requestedTab === 'documents' ||
    requestedTab === 'report-config' ||
    requestedTab === 'system-config' ||
    requestedTab === 'transfer-config'
      ? requestedTab
      : 'overview';

  // States
  const [stats, setStats] = useState<any>({
    totalUsers: 0,
    totalTransactions: 0,
    totalDocuments: 0,
    totalReports: 0,
    recentTransactions: [],
    recentReports: [],
  });
  const [users, setUsers] = useState<UserItem[]>([]);
  const [transactions, setTransactions] = useState<TransactionItem[]>([]);
  const [reports, setReports] = useState<ReportItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchText, setSearchText] = useState('');
  const [statusFilter, setStatusFilter] = useState('ALL');
  const [sortKey, setSortKey] = useState('createdAt');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const [page, setPage] = useState(1);
  const pageSize = 8;

  const [showCreateUserModal, setShowCreateUserModal] = useState(false);
  const [creatingUser, setCreatingUser] = useState(false);
  const [newUser, setNewUser] = useState({
    username: '',
    email: '',
    password: '',
    role: 'STUDENT',
    tierType: 'Free',
  });

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
  const [selectedReport, setSelectedReport] = useState<ReportItem | null>(null);
  const [selectedReportDocument, setSelectedReportDocument] = useState<any | null>(null);
  const [reportPreviewLoading, setReportPreviewLoading] = useState(false);

  const toggleSort = (key: string) => {
    setPage(1);
    if (sortKey === key) setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
    else {
      setSortKey(key);
      setSortDirection('asc');
    }
  };
  const sortHeader = (key: string, label: string) => (
    <button
      className={`sortable-header ${sortKey === key ? 'active' : ''}`}
      onClick={() => toggleSort(key)}
      aria-label={`Sắp xếp theo ${label}`}
    >
      {label}
      <span aria-hidden="true">
        {sortKey === key ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : ' ↕'}
      </span>
    </button>
  );

  const loadDashboardData = async () => {
    setLoading(true);
    try {
      if (
        adminTab === 'documents' ||
        adminTab === 'report-config' ||
        adminTab === 'system-config' ||
        adminTab === 'transfer-config'
      )
        return;
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
    setPage(1);
    setSearchText(searchParams.get('q') ?? '');
    setStatusFilter(searchParams.get('status') ?? 'ALL');
    setSortKey(
      adminTab === 'users' ? 'userId' : adminTab === 'transactions' ? 'transactionId' : 'reportId',
    );
    setSortDirection('desc');
    setShowCreateUserModal(false);
    setShowEditUserModal(false);
    setEditingUser(null);
    setUpdatingUser(false);
    setSelectedReport(null);
    setSelectedReportDocument(null);
    setReportPreviewLoading(false);
    loadDashboardData();
  }, [adminTab]);

  const goToAdminTab = (tab: string, options?: { query?: string; status?: string }) => {
    const params = new URLSearchParams({ tab });
    if (options?.query) params.set('q', options.query);
    if (options?.status) params.set('status', options.status);
    navigate(`/admin?${params.toString()}`);
  };

  const closeEditUserModal = () => {
    if (updatingUser) return;
    setShowEditUserModal(false);
    setEditingUser(null);
  };

  const openReportPreview = async (report: ReportItem) => {
    setSelectedReport(report);
    setSelectedReportDocument(null);
    setReportPreviewLoading(true);
    try {
      setSelectedReportDocument(await api.admin.getDocumentDetail(report.documentId));
    } catch (err: any) {
      notify(err.message || 'Không thể tải chi tiết tài liệu.', 'error');
      setSelectedReport(null);
    } finally {
      setReportPreviewLoading(false);
    }
  };

  const closeReportPreview = () => {
    setSelectedReport(null);
    setSelectedReportDocument(null);
    setReportPreviewLoading(false);
  };

  const filteredUsers = useMemo(
    () =>
      users.filter((user) => {
        const keyword = searchText.trim().toLowerCase();
        return (
          (!keyword ||
            user.username.toLowerCase().includes(keyword) ||
            user.email.toLowerCase().includes(keyword)) &&
          (statusFilter === 'ALL' || user.status === statusFilter)
        );
      }),
    [users, searchText, statusFilter],
  );
  const filteredTransactions = useMemo(
    () =>
      transactions.filter((transaction) => {
        const keyword = searchText.trim().toLowerCase();
        return (
          (!keyword ||
            transaction.username.toLowerCase().includes(keyword) ||
            String(transaction.transactionId).includes(keyword)) &&
          (statusFilter === 'ALL' || transaction.status === statusFilter)
        );
      }),
    [transactions, searchText, statusFilter],
  );
  const filteredReports = useMemo(
    () =>
      reports.filter((report) => {
        const keyword = searchText.trim().toLowerCase();
        const matchesStatus =
          statusFilter === 'ALL' ||
          (statusFilter === 'RESOLVED'
            ? report.status !== 'PENDING'
            : report.status === statusFilter);
        return (
          (!keyword ||
            report.documentTitle.toLowerCase().includes(keyword) ||
            report.reporterName.toLowerCase().includes(keyword)) &&
          matchesStatus
        );
      }),
    [reports, searchText, statusFilter],
  );
  const reportStatistics = useMemo(() => {
    const count = (status: string) => reports.filter((report) => report.status === status).length;
    return [
      ['Tổng báo cáo', reports.length],
      ['Chờ xử lý', count('PENDING')],
      ['Đang xem xét', count('IN_REVIEW')],
      ['Đã hạn chế', count('RESTRICTED')],
      ['Không vi phạm', count('NO_VIOLATION')],
      ['Xác nhận vi phạm', count('VIOLATION_CONFIRMED')],
    ] as const;
  }, [reports]);
  const overviewTransactions = useMemo(
    () =>
      [...(stats.recentTransactions ?? [])]
        .filter(
          (item: any) =>
            !searchText.trim() ||
            item.username?.toLowerCase().includes(searchText.trim().toLowerCase()) ||
            String(item.transactionId).includes(searchText.trim()),
        )
        .sort(
          (a: any, b: any) =>
            (sortDirection === 'asc' ? 1 : -1) *
            (new Date(a.startedAt ?? 0).getTime() - new Date(b.startedAt ?? 0).getTime()),
        ),
    [stats, searchText, sortDirection],
  );
  const overviewReports = useMemo(
    () =>
      [...(stats.recentReports ?? [])]
        .filter(
          (item: any) =>
            !searchText.trim() ||
            item.title?.toLowerCase().includes(searchText.trim().toLowerCase()) ||
            item.reporterName?.toLowerCase().includes(searchText.trim().toLowerCase()),
        )
        .sort(
          (a: any, b: any) =>
            (sortDirection === 'asc' ? 1 : -1) *
            (new Date(a.createdAt ?? 0).getTime() - new Date(b.createdAt ?? 0).getTime()),
        ),
    [stats, searchText, sortDirection],
  );
  const rawRows: any[] =
    adminTab === 'users'
      ? filteredUsers
      : adminTab === 'transactions'
        ? filteredTransactions
        : filteredReports;
  const activeRows = [...rawRows].sort((left, right) => {
    const a = left[sortKey] ?? '';
    const b = right[sortKey] ?? '';
    const value =
      typeof a === 'number' && typeof b === 'number'
        ? a - b
        : String(a).localeCompare(String(b), 'vi');
    return sortDirection === 'asc' ? value : -value;
  });
  const totalPages = Math.max(1, Math.ceil(activeRows.length / pageSize));
  const pagedRows = activeRows.slice((page - 1) * pageSize, page * pageSize);

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

  const handleCreateUser = async (event: React.FormEvent) => {
    event.preventDefault();
    setCreatingUser(true);
    try {
      await api.admin.createUser(
        {
          username: newUser.username.trim(),
          email: newUser.email.trim(),
          password: newUser.password,
        },
        newUser.role,
        newUser.tierType,
      );
      setShowCreateUserModal(false);
      setNewUser({ username: '', email: '', password: '', role: 'STUDENT', tierType: 'Free' });
      await loadDashboardData();
      notify('Đã tạo tài khoản thành công.', 'success');
    } catch (err: any) {
      notify(err.message || 'Không thể tạo tài khoản.', 'error');
    } finally {
      setCreatingUser(false);
    }
  };

  const handleToggleUserStatus = async (user: UserItem) => {
    const nextStatus = user.status === 'ACTIVE' ? 'SUSPENDED' : 'ACTIVE';
    if (
      !(await confirm({
        title: nextStatus === 'SUSPENDED' ? 'Khóa tài khoản' : 'Mở khóa tài khoản',
        message: `${nextStatus === 'SUSPENDED' ? 'Khóa' : 'Mở khóa'} tài khoản ${user.username}?`,
        confirmLabel: nextStatus === 'SUSPENDED' ? 'Khóa tài khoản' : 'Mở khóa',
        danger: nextStatus === 'SUSPENDED',
      }))
    )
      return;
    await api.admin.updateUser(user.userId, {
      username: user.username,
      email: user.email,
      role: user.role,
      status: nextStatus,
      balance: user.balance,
      tierId: user.tierId,
    });
    await loadDashboardData();
    notify('Đã cập nhật trạng thái tài khoản.', 'success');
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
        tierId: editTierId,
      });
      setShowEditUserModal(false);
      loadDashboardData();
      notify('Cập nhật người dùng thành công.', 'success');
    } catch (err: any) {
      notify(err.message || 'Cập nhật thất bại.', 'error');
    } finally {
      setUpdatingUser(false);
      setEditingUser(null);
    }
  };

  // Transaction Approval Actions
  const handleApproveTransaction = async (txId: number, status: 'SUCCESS' | 'CANCELLED') => {
    const actionText = status === 'SUCCESS' ? 'Duyệt thành công' : 'Hủy bỏ';
    if (
      !(await confirm({
        title: 'Xử lý giao dịch',
        message: `Xác nhận ${actionText} giao dịch này?`,
        confirmLabel: actionText,
        danger: status === 'CANCELLED',
      }))
    )
      return;

    try {
      await api.admin.updateTransaction(txId, status);
      loadDashboardData();
      notify('Giao dịch đã được cập nhật.', 'success');
    } catch (err: any) {
      notify(err.message || 'Thao tác giao dịch thất bại.', 'error');
    }
  };

  // Report Resolution Actions
  const handleResolveReport = async (reportId: number, action: 'TAKE_ACTION' | 'DISMISS') => {
    const actionText = action === 'TAKE_ACTION' ? 'Gỡ tài liệu khỏi công khai' : 'Bỏ qua báo cáo';
    if (
      !(await confirm({
        title: 'Xử lý báo cáo',
        message: `Xác nhận xử lý báo cáo: ${actionText}?`,
        confirmLabel: 'Xác nhận',
        danger: action === 'TAKE_ACTION',
      }))
    )
      return;

    try {
      await api.admin.resolveReport(reportId, action);
      if (selectedReport?.reportId === reportId) closeReportPreview();
      loadDashboardData();
      notify('Đã giải quyết báo cáo.', 'success');
    } catch (err: any) {
      notify(err.message || 'Xử lý báo cáo thất bại.', 'error');
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
        {/* Content Panel */}
        <div className="admin-content-pane glass-panel">
          {adminTab === 'documents' ||
          adminTab === 'report-config' ||
          adminTab === 'system-config' ||
          adminTab === 'transfer-config' ? (
            <AdminConfiguration tab={adminTab} />
          ) : loading ? (
            <div className="admin-loader">
              <Loader className="spin" size={32} />
              <p>Đang tải dữ liệu quản trị...</p>
            </div>
          ) : adminTab === 'overview' ? (
            <div className="overview-pane animate-fade-in">
              <h3>Tổng quan hệ thống</h3>
              <div className="admin-toolbar overview-toolbar">
                <input
                  className="input-control"
                  placeholder="Lọc hoạt động gần đây..."
                  value={searchText}
                  onChange={(e) => setSearchText(e.target.value)}
                />
                <select
                  className="input-control"
                  aria-label="Sắp xếp hoạt động"
                  value={sortDirection}
                  onChange={(e) => setSortDirection(e.target.value as 'asc' | 'desc')}
                >
                  <option value="desc">Mới nhất</option>
                  <option value="asc">Cũ nhất</option>
                </select>
              </div>

              <div className="stats-grid">
                <button
                  type="button"
                  className="stat-box stat-link glass-card"
                  onClick={() => goToAdminTab('users')}
                >
                  <Users size={28} className="stat-icon purple" />
                  <div className="stat-details">
                    <span className="stat-label">Tổng tài khoản</span>
                    <span className="stat-value">{stats.totalUsers}</span>
                    <small>
                      {stats.studentUsers ?? 0} Student · {stats.freeUsers ?? 0} Free ·{' '}
                      {stats.premiumUsers ?? 0} Premium
                    </small>
                  </div>
                </button>
                <button
                  type="button"
                  className="stat-box stat-link glass-card"
                  onClick={() => goToAdminTab('transactions', { status: 'PENDING' })}
                >
                  <DollarSign size={28} className="stat-icon green" />
                  <div className="stat-details">
                    <span className="stat-label">Giao dịch chờ duyệt</span>
                    <span className="stat-value">{stats.pendingTransactions ?? 0}</span>
                    <small>{stats.totalTransactions} giao dịch tổng cộng</small>
                  </div>
                </button>
                <button
                  type="button"
                  className="stat-box stat-link glass-card"
                  onClick={() => goToAdminTab('documents')}
                >
                  <FileText size={28} className="stat-icon blue" />
                  <div className="stat-details">
                    <span className="stat-label">Tài liệu tải lên</span>
                    <span className="stat-value">{stats.totalDocuments}</span>
                    <small>
                      {stats.publicDocuments ?? 0} công khai · {stats.flaggedDocuments ?? 0} gắn cờ
                    </small>
                  </div>
                </button>
                <button
                  type="button"
                  className="stat-box stat-link glass-card"
                  onClick={() => goToAdminTab('reports', { status: 'PENDING' })}
                >
                  <AlertOctagon size={28} className="stat-icon red" />
                  <div className="stat-details">
                    <span className="stat-label">Report chờ xử lý</span>
                    <span className="stat-value">{stats.pendingReports ?? 0}</span>
                    <small>{stats.totalReports} report tổng cộng</small>
                  </div>
                </button>
              </div>
              <button
                type="button"
                className="dashboard-finance dashboard-finance-link glass-card"
                onClick={() => goToAdminTab('transactions', { status: 'SUCCESS' })}
              >
                <span>Tổng tiền nạp đã duyệt</span>
                <strong>{Number(stats.successfulDeposits ?? 0).toLocaleString('vi-VN')}đ</strong>
                <small>
                  {stats.suspendedUsers ?? 0} tài khoản bị khóa · {stats.privateDocuments ?? 0} tài
                  liệu riêng tư
                </small>
              </button>
              <div className="dashboard-activity-grid">
                <section className="activity-panel glass-card">
                  <h4>Giao dịch gần đây</h4>
                  {overviewTransactions.map((transaction: any) => (
                    <button
                      type="button"
                      className="activity-row activity-link"
                      key={transaction.transactionId}
                      onClick={() =>
                        goToAdminTab('transactions', { query: String(transaction.transactionId) })
                      }
                    >
                      <div>
                        <strong>{transaction.username}</strong>
                        <small>
                          #{transaction.transactionId} · {transaction.status}
                        </small>
                      </div>
                      <span>{Number(transaction.amount).toLocaleString('vi-VN')}đ</span>
                    </button>
                  ))}
                </section>
                <section className="activity-panel glass-card">
                  <h4>Report gần đây</h4>
                  {overviewReports.map((report: any) => (
                    <button
                      type="button"
                      className="activity-row activity-link"
                      key={report.reportId}
                      onClick={() => goToAdminTab('reports', { query: report.title })}
                    >
                      <div>
                        <strong>{report.title}</strong>
                        <small>
                          {report.reporterName} · {report.reasonCode}
                        </small>
                      </div>
                      <span className={report.status === 'PENDING' ? 'pending-text' : ''}>
                        {report.status}
                      </span>
                    </button>
                  ))}
                </section>
              </div>
            </div>
          ) : adminTab === 'users' ? (
            <div className="users-pane animate-fade-in">
              <div className="admin-section-heading">
                <h3>Quản lý tài khoản</h3>
                <button className="btn-primary" onClick={() => setShowCreateUserModal(true)}>
                  <Plus size={16} /> Thêm tài khoản
                </button>
              </div>
              <div className="admin-toolbar">
                <input
                  className="input-control"
                  placeholder="Tìm tên hoặc email..."
                  value={searchText}
                  onChange={(event) => {
                    setSearchText(event.target.value);
                    setPage(1);
                  }}
                />
                <select
                  className="input-control"
                  value={statusFilter}
                  onChange={(event) => {
                    setStatusFilter(event.target.value);
                    setPage(1);
                  }}
                >
                  <option value="ALL">Tất cả trạng thái</option>
                  <option value="ACTIVE">Hoạt động</option>
                  <option value="SUSPENDED">Đang khóa</option>
                </select>
              </div>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>{sortHeader('userId', 'ID')}</th>
                      <th>{sortHeader('username', 'Sinh viên')}</th>
                      <th>{sortHeader('email', 'Email')}</th>
                      <th>{sortHeader('role', 'Vai trò')}</th>
                      <th>{sortHeader('tierName', 'Membership')}</th>
                      <th>{sortHeader('balance', 'Số dư')}</th>
                      <th>{sortHeader('status', 'Trạng thái')}</th>
                      <th>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(pagedRows as UserItem[]).map((u) => (
                      <tr key={u.userId}>
                        <td className="monospace-text">#{u.userId}</td>
                        <td className="bold-text">{u.username}</td>
                        <td>{u.email}</td>
                        <td>
                          <span className={`role-badge ${u.role}`}>{u.role}</span>
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
                            <button
                              onClick={() => handleOpenEditUser(u)}
                              className="action-btn edit"
                              title="Sửa thông tin"
                            >
                              <Edit size={14} />
                            </button>
                            <button
                              onClick={() => handleToggleUserStatus(u)}
                              className={`action-btn ${u.status === 'ACTIVE' ? 'delete' : 'approve'}`}
                              title={u.status === 'ACTIVE' ? 'Khóa tài khoản' : 'Mở khóa tài khoản'}
                            >
                              {u.status === 'ACTIVE' ? <Lock size={14} /> : <Unlock size={14} />}
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination
                page={page}
                totalPages={totalPages}
                setPage={setPage}
                total={filteredUsers.length}
              />
            </div>
          ) : adminTab === 'transactions' ? (
            <div className="txs-pane animate-fade-in">
              <h3>Phê duyệt Giao dịch</h3>
              <div className="admin-toolbar">
                <input
                  className="input-control"
                  placeholder="Tìm người dùng hoặc mã giao dịch..."
                  value={searchText}
                  onChange={(event) => {
                    setSearchText(event.target.value);
                    setPage(1);
                  }}
                />
                <select
                  className="input-control"
                  value={statusFilter}
                  onChange={(event) => {
                    setStatusFilter(event.target.value);
                    setPage(1);
                  }}
                >
                  <option value="ALL">Tất cả trạng thái</option>
                  <option value="PENDING">Chờ duyệt</option>
                  <option value="SUCCESS">Thành công</option>
                  <option value="CANCELLED">Đã hủy</option>
                </select>
              </div>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>{sortHeader('transactionId', 'ID')}</th>
                      <th>{sortHeader('username', 'Sinh viên')}</th>
                      <th>{sortHeader('amount', 'Số tiền')}</th>
                      <th>{sortHeader('status', 'Trạng thái')}</th>
                      <th>{sortHeader('startedAt', 'Thời gian tạo')}</th>
                      <th>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(pagedRows as TransactionItem[]).map((tx) => {
                      const isPending = tx.status === 'PENDING';
                      return (
                        <tr key={tx.transactionId}>
                          <td className="monospace-text">#{tx.transactionId}</td>
                          <td className="bold-text">{tx.username}</td>
                          <td className={`tx-value ${tx.amount > 0 ? 'positive' : 'negative'}`}>
                            {tx.amount > 0 ? '+' : ''}
                            {tx.amount.toLocaleString()}đ
                          </td>
                          <td>
                            <span className={`tx-status-badge ${tx.status}`}>{tx.status}</span>
                          </td>
                          <td>{tx.startedAt ? new Date(tx.startedAt).toLocaleString() : 'N/A'}</td>
                          <td>
                            {isPending ? (
                              <div className="table-actions">
                                <button
                                  onClick={() =>
                                    handleApproveTransaction(tx.transactionId, 'SUCCESS')
                                  }
                                  className="action-btn approve"
                                  title="Duyệt giao dịch"
                                >
                                  <Check size={14} />
                                </button>
                                <button
                                  onClick={() =>
                                    handleApproveTransaction(tx.transactionId, 'CANCELLED')
                                  }
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
              <Pagination
                page={page}
                totalPages={totalPages}
                setPage={setPage}
                total={filteredTransactions.length}
              />
            </div>
          ) : (
            <div className="reports-pane animate-fade-in">
              <div className="admin-section-tabs">
                <NavLink className="active" to="/admin?tab=reports">
                  Báo cáo vi phạm
                </NavLink>
                <NavLink to="/admin?tab=documents">Tài liệu</NavLink>
              </div>
              <h3>Báo cáo tài liệu vi phạm</h3>
              <section
                className="report-statistics glass-card"
                aria-label="Thống kê báo cáo vi phạm"
              >
                <h4>Thống kê báo cáo vi phạm</h4>
                <div className="table-scroll">
                  <table className="admin-table compact-stat-table">
                    <thead>
                      <tr>
                        {reportStatistics.map(([label]) => (
                          <th key={label}>{label}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        {reportStatistics.map(([label, value]) => (
                          <td key={label}>{value}</td>
                        ))}
                      </tr>
                    </tbody>
                  </table>
                </div>
              </section>
              <div className="admin-toolbar">
                <input
                  className="input-control"
                  placeholder="Tìm tài liệu hoặc người báo cáo..."
                  value={searchText}
                  onChange={(event) => {
                    setSearchText(event.target.value);
                    setPage(1);
                  }}
                />
                <select
                  className="input-control"
                  value={statusFilter}
                  onChange={(event) => {
                    setStatusFilter(event.target.value);
                    setPage(1);
                  }}
                >
                  <option value="ALL">Tất cả trạng thái</option>
                  <option value="PENDING">Chờ xử lý</option>
                  <option value="IN_REVIEW">Đang xem xét</option>
                  <option value="RESTRICTED">Đã hạn chế</option>
                  <option value="NO_VIOLATION">Không vi phạm</option>
                  <option value="VIOLATION_CONFIRMED">Xác nhận vi phạm</option>
                  <option value="RESOLVED">Đã xử lý</option>
                </select>
              </div>

              {filteredReports.length === 0 ? (
                <div className="empty-reports">
                  <AlertOctagon size={48} className="empty-icon" />
                  <p>Không có báo cáo vi phạm nào đang chờ xử lý.</p>
                </div>
              ) : (
                <div className="reports-list">
                  {(pagedRows as ReportItem[]).map((r) => (
                    <div
                      key={r.reportId}
                      className="report-card glass-card"
                      role="button"
                      tabIndex={0}
                      onClick={() => openReportPreview(r)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          openReportPreview(r);
                        }
                      }}
                    >
                      <div className="report-info-header">
                        <div>
                          <h4>
                            Tài liệu: <strong>{r.documentTitle}</strong> (ID #{r.documentId})
                          </h4>
                          <p className="reporter-desc">
                            Người báo cáo: {r.reporterName} (ID #{r.reporterId})
                          </p>
                        </div>
                        <span className={`reason-badge ${r.reasonCode}`}>{r.reasonCode}</span>
                      </div>

                      {r.additionalDetails && (
                        <div className="report-details-box">
                          <strong>Mô tả chi tiết:</strong>
                          <p>{r.additionalDetails}</p>
                        </div>
                      )}

                      {r.status === 'PENDING' && (
                        <div className="report-card-actions">
                          <button
                            onClick={(event) => {
                              event.stopPropagation();
                              handleResolveReport(r.reportId, 'TAKE_ACTION');
                            }}
                            className="btn-secondary"
                          >
                            Gỡ khỏi công khai
                          </button>
                          <button
                            onClick={(event) => {
                              event.stopPropagation();
                              handleResolveReport(r.reportId, 'DISMISS');
                            }}
                            className="btn-primary"
                          >
                            Bỏ qua (Dismiss)
                          </button>
                        </div>
                      )}
                      <span className="report-open-hint">
                        <Eye size={15} /> Nhấn để xem tài liệu và chi tiết báo cáo
                      </span>
                    </div>
                  ))}
                </div>
              )}
              <Pagination
                page={page}
                totalPages={totalPages}
                setPage={setPage}
                total={filteredReports.length}
              />
            </div>
          )}
        </div>
      </div>

      {/* Modal: Create User */}
      {showCreateUserModal && (
        <div className="modal-overlay">
          <div className="modal-box glass-panel animate-slide-up">
            <h3>Thêm tài khoản</h3>
            <form onSubmit={handleCreateUser} className="admin-form">
              <div className="form-group">
                <label>Tên người dùng</label>
                <input
                  className="input-control"
                  required
                  value={newUser.username}
                  onChange={(event) => setNewUser({ ...newUser, username: event.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Email</label>
                <input
                  className="input-control"
                  required
                  type="email"
                  value={newUser.email}
                  onChange={(event) => setNewUser({ ...newUser, email: event.target.value })}
                />
              </div>
              <div className="form-group">
                <label>Mật khẩu ban đầu</label>
                <input
                  className="input-control"
                  required
                  minLength={6}
                  type="password"
                  value={newUser.password}
                  onChange={(event) => setNewUser({ ...newUser, password: event.target.value })}
                />
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>Vai trò</label>
                  <select
                    className="input-control"
                    value={newUser.role}
                    onChange={(event) => setNewUser({ ...newUser, role: event.target.value })}
                  >
                    <option value="STUDENT">Sinh viên</option>
                    <option value="MODERATOR">Kiểm duyệt viên</option>
                    <option value="ADMIN">Quản trị viên</option>
                  </select>
                </div>
                <div className="form-group">
                  <label>Gói</label>
                  <select
                    className="input-control"
                    value={newUser.tierType}
                    onChange={(event) => setNewUser({ ...newUser, tierType: event.target.value })}
                  >
                    <option value="Free">Free</option>
                    <option value="Premium">Premium</option>
                    <option value="Guest">Guest</option>
                  </select>
                </div>
              </div>
              <div className="modal-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setShowCreateUserModal(false)}
                >
                  Hủy
                </button>
                <button type="submit" className="btn-primary" disabled={creatingUser}>
                  {creatingUser ? 'Đang tạo...' : 'Tạo tài khoản'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Modal: Edit User */}
      {showEditUserModal && editingUser && (
        <div className="modal-overlay" role="presentation" onMouseDown={closeEditUserModal}>
          <div
            key={editingUser.userId}
            className="modal-box admin-user-modal glass-panel animate-slide-up"
            role="dialog"
            aria-modal="true"
            aria-labelledby="edit-user-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="modal-title-row">
              <div>
                <h3 id="edit-user-title">Sửa hồ sơ #{editingUser.userId}</h3>
                <p>
                  {editingUser.username} · {editingUser.email}
                </p>
              </div>
              <button
                type="button"
                className="modal-close-button"
                aria-label="Đóng popup"
                onClick={closeEditUserModal}
              >
                <X size={20} />
              </button>
            </div>
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
                <input type="number" value={editBalance} className="input-control" disabled />
                <small>Số dư chỉ thay đổi thông qua giao dịch để bảo đảm lịch sử đối soát.</small>
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Vai trò</label>
                  <select
                    value={editRole}
                    onChange={(e) => setEditRole(e.target.value)}
                    className="input-control"
                  >
                    <option value="STUDENT">STUDENT</option>
                    <option value="MODERATOR">MODERATOR</option>
                    <option value="ADMIN">ADMIN</option>
                  </select>
                </div>

                <div className="form-group">
                  <label>Membership</label>
                  <select
                    value={editTierId}
                    onChange={(e) => setEditTierId(parseInt(e.target.value))}
                    className="input-control"
                  >
                    <option value={2}>Free Tier</option>
                    <option value={3}>Premium Tier</option>
                    <option value={1}>Guest Tier</option>
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>Trạng thái hoạt động</label>
                <select
                  value={editStatus}
                  onChange={(e) => setEditStatus(e.target.value)}
                  className="input-control"
                >
                  <option value="ACTIVE">ACTIVE (Hoạt động)</option>
                  <option value="SUSPENDED">SUSPENDED (Tạm khóa)</option>
                </select>
              </div>

              <div className="modal-actions">
                <button type="button" onClick={closeEditUserModal} className="btn-secondary">
                  Hủy
                </button>
                <button type="submit" className="btn-primary" disabled={updatingUser}>
                  {updatingUser ? <Loader className="spin" size={16} /> : 'Lưu lại'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {selectedReport && (
        <div className="modal-overlay" role="presentation" onMouseDown={closeReportPreview}>
          <div
            className="report-preview-modal glass-panel animate-slide-up"
            role="dialog"
            aria-modal="true"
            aria-labelledby="report-preview-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="modal-title-row">
              <div>
                <h3 id="report-preview-title">Chi tiết báo cáo #{selectedReport.reportId}</h3>
                <p>{selectedReport.documentTitle}</p>
              </div>
            </div>

            <section className="report-summary-section">
              <div className="report-preview-grid">
                <span>Người báo cáo</span>
                <strong>
                  {selectedReport.reporterName} (ID #{selectedReport.reporterId})
                </strong>
                <span>Lý do</span>
                <strong>
                  <span className={`reason-badge ${selectedReport.reasonCode}`}>
                    {selectedReport.reasonCode}
                  </span>
                </strong>
                <span>Trạng thái</span>
                <strong>{selectedReport.status}</strong>
                <span>Thời gian</span>
                <strong>
                  {selectedReport.createdAt
                    ? new Date(selectedReport.createdAt).toLocaleString('vi-VN')
                    : 'Không có dữ liệu'}
                </strong>
              </div>
              <div className="report-description-full">
                <strong>Mô tả chi tiết báo cáo</strong>
                <p>
                  {selectedReport.additionalDetails || 'Người dùng không cung cấp mô tả bổ sung.'}
                </p>
              </div>
            </section>

            {reportPreviewLoading ? (
              <div className="report-preview-loading">
                <Loader className="spin" /> Đang tải tài liệu...
              </div>
            ) : (
              selectedReportDocument && (
                <section className="reported-document-section">
                  <div className="reported-document-heading">
                    <FileTypeIcon
                      extension={selectedReportDocument.document.fileExtension}
                      size={24}
                      className="reported-file-icon"
                    />
                    <div>
                      <h4>
                        {selectedReportDocument.document.title}.
                        {selectedReportDocument.document.fileExtension}
                      </h4>
                      <p>
                        Đăng bởi {selectedReportDocument.document.uploaderName} ·{' '}
                        {selectedReportDocument.document.sharingPermission}
                      </p>
                    </div>
                  </div>
                  <div className="document-metrics">
                    <span>{selectedReportDocument.document.viewCount ?? 0} lượt xem</span>
                    <span>{selectedReportDocument.document.downloadCount ?? 0} lượt tải</span>
                    <span>{selectedReportDocument.document.bookmarkCount ?? 0} lượt lưu</span>
                    <span>
                      {Number(selectedReportDocument.document.fileSizeMb ?? 0).toFixed(2)} MB
                    </span>
                  </div>
                  <div className="document-content-preview">
                    <strong>Nội dung / mô tả tài liệu</strong>
                    <p>{selectedReportDocument.description}</p>
                  </div>
                </section>
              )
            )}

            <div className="modal-actions">
              {selectedReport.status === 'PENDING' && (
                <>
                  <button
                    type="button"
                    className="btn-secondary"
                    onClick={() => handleResolveReport(selectedReport.reportId, 'TAKE_ACTION')}
                  >
                    Gỡ khỏi công khai
                  </button>
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={() => handleResolveReport(selectedReport.reportId, 'DISMISS')}
                  >
                    Bỏ qua
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      )}

      <style>{`
        .admin-container {
          min-height: 80vh;
        }

        .admin-section-heading { display:flex; align-items:center; justify-content:space-between; gap:1rem; }
        .admin-section-heading .btn-primary { display:flex; align-items:center; gap:.4rem; }
        .admin-toolbar { display:grid; grid-template-columns:minmax(220px,1fr) 190px minmax(250px,auto); gap:.75rem; margin:1rem 0; }
        .sort-controls{display:grid;grid-template-columns:1fr 110px;gap:.5rem}
        .admin-section-tabs { display:flex; gap:.5rem; border-bottom:1px solid rgba(255,255,255,.08); margin-bottom:1rem; }
        .admin-section-tabs a { padding:.7rem 1rem; color:var(--text-muted); border-bottom:2px solid transparent; }
        .admin-section-tabs a.active { color:var(--text-primary); border-color:var(--accent-purple); }
        .admin-pagination { display:flex; align-items:center; justify-content:space-between; gap:1rem; margin-top:1rem; color:var(--text-muted); }
        .admin-pagination div { display:flex; align-items:center; gap:.65rem; }
        .admin-pagination button:disabled { opacity:.4; cursor:not-allowed; }

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
          display: block;
          height: calc(100vh - 12rem);
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

        .stat-link { width:100%; border:1px solid rgba(255,255,255,.08); color:inherit; text-align:left; cursor:pointer; font:inherit; }
        .stat-link:hover,.stat-link:focus-visible { transform:translateY(-2px); border-color:var(--accent-blue); outline:none; }

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

        .stat-details small { color: var(--text-muted); font-size: .72rem; margin-top: .2rem; }
        .dashboard-finance { margin-top: 1rem; padding: 1.2rem; display: grid; grid-template-columns: 1fr auto; gap: .35rem 1rem; align-items: center; }
        .dashboard-finance span { color: var(--text-secondary); }
        .dashboard-finance strong { grid-row: span 2; font-size: 1.65rem; color: var(--success); }
        .dashboard-finance small { color: var(--text-muted); }
        .dashboard-finance-link { width:100%; border:1px solid rgba(255,255,255,.08); color:inherit; text-align:left; cursor:pointer; font:inherit; }
        .dashboard-activity-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-top: 1rem; }
        .activity-panel { padding: 1.2rem; }
        .activity-panel h4 { margin-bottom: .7rem; }
        .activity-row { display: flex; justify-content: space-between; align-items: center; gap: 1rem; padding: .7rem 0; border-bottom: 1px solid rgba(255,255,255,.05); }
        .activity-row div { min-width: 0; display: flex; flex-direction: column; gap: .2rem; }
        .activity-row strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .85rem; }
        .activity-row small { color: var(--text-muted); }
        .activity-row > span { flex-shrink: 0; color: var(--text-secondary); font-size: .8rem; }
        .activity-row > span.pending-text { color: var(--warning); }
        .activity-link { width:100%; border:0; background:transparent; color:inherit; text-align:left; cursor:pointer; font:inherit; }
        .activity-link:hover,.activity-link:focus-visible { background:rgba(255,255,255,.04); outline:none; }

        .modal-overlay { position:fixed; inset:0; z-index:2000; display:grid; place-items:center; padding:1rem; background:rgba(3,7,18,.78); backdrop-filter:blur(8px); }
        .modal-box { width:min(560px,calc(100vw - 2rem)); max-height:calc(100vh - 2rem); overflow:auto; padding:1.5rem; border-radius:var(--radius-md); }
        .admin-user-modal { position:relative; }
        .modal-title-row { display:flex; align-items:flex-start; justify-content:space-between; gap:1rem; padding-bottom:1rem; margin-bottom:1rem; border-bottom:1px solid rgba(255,255,255,.08); }
        .modal-title-row h3 { margin:0 0 .3rem; padding:0; border:0; }
        .modal-title-row p { color:var(--text-muted); font-size:.85rem; overflow-wrap:anywhere; }
        .modal-close-button { display:grid; place-items:center; flex:0 0 auto; border:0; background:transparent; color:var(--text-secondary); cursor:pointer; padding:.25rem; }

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
          cursor: pointer;
          transition: var(--transition-fast);
        }
        .report-card:hover,.report-card:focus-visible { border-color:var(--accent-blue); transform:translateY(-1px); outline:none; }
        .report-open-hint { display:inline-flex;align-items:center;gap:.35rem;color:var(--accent-blue);font-size:.78rem; }
        .report-preview-modal { width:min(850px,calc(100vw - 2rem));max-height:calc(100vh - 2rem);overflow:auto;padding:1.5rem;border-radius:var(--radius-md); }
        .report-preview-grid { display:grid;grid-template-columns:140px 1fr;gap:.75rem;margin:1rem 0; }
        .report-preview-grid>span { color:var(--text-muted); }
        .report-description-full,.document-content-preview { padding:1rem;border-radius:var(--radius-sm);background:rgba(255,255,255,.04); }
        .report-description-full p,.document-content-preview p { margin-top:.5rem;color:var(--text-secondary);white-space:pre-wrap;line-height:1.55; }
        .reported-document-section { margin-top:1rem;padding-top:1rem;border-top:1px solid rgba(255,255,255,.08); }
        .reported-document-heading { display:flex;align-items:center;gap:.75rem; }
        .reported-document-heading svg { color:var(--accent-blue);flex-shrink:0; }
        .reported-file-icon { width:42px;height:42px;border-radius:10px;display:grid;place-items:center;flex:0 0 auto; }
        .reported-document-heading p { color:var(--text-muted);font-size:.85rem; }
        .document-metrics { display:flex;flex-wrap:wrap;gap:.5rem;margin:1rem 0; }
        .document-metrics span { padding:.35rem .65rem;border-radius:999px;background:rgba(255,255,255,.05);color:var(--text-secondary);font-size:.78rem; }
        .document-content-preview { max-height:260px;overflow:auto; }
        .report-preview-loading { min-height:180px;display:flex;align-items:center;justify-content:center;gap:.6rem;color:var(--text-muted); }

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
          .admin-toolbar { grid-template-columns:1fr; }
          .report-preview-grid { grid-template-columns:1fr;gap:.35rem; }
          .report-preview-grid>strong { margin-bottom:.5rem; }
          .admin-layout-grid {
            grid-template-columns: 1fr;
            height: auto;
          }
        }
      `}</style>
    </div>
  );
};
