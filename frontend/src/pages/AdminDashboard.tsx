import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { NavLink, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../services/api';
import { FileTypeIcon } from '../components/FileTypeIcon';
import { AdminConfiguration } from './AdminConfiguration';
import { useUiFeedback } from '../context/UiFeedbackContext';
import { formatDateTime } from '../utils/dateTime';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
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
  Bot,
  Cpu,
  Zap,
  Download,
  Bookmark,
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
  completedAt?: string;
  referenceCode?: string;
  bankId?: string;
  approverId?: number;
  approverName?: string;
  failureReason?: string;
}

type TransactionAction = 'APPROVE' | 'REJECT' | 'REFUND' | 'REVERSE_DEPOSIT';

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

interface ReportedAccountAnalytics {
  userId: number;
  username: string;
  email?: string;
  status: string;
  reportedDocumentCount: number;
  totalReports: number;
  pendingReports: number;
  confirmedReports: number;
}

interface DocumentEngagementAnalytics {
  documentId: number;
  title: string;
  ownerUserId: number;
  ownerUsername: string;
  fileExtension?: string;
  sharingPermission: string;
  uniqueDownloads: number;
  uniqueBookmarks: number;
  viewCount: number;
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
    requestedTab === 'transfer-config' ||
    requestedTab === 'audit-log' ||
    requestedTab === 'ai-observability' ||
    requestedTab === 'account-analytics' ||
    requestedTab === 'document-analytics'
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
  const [auditLogs, setAuditLogs] = useState<any[]>([]);
  const [auditTotalPages, setAuditTotalPages] = useState(1);
  const [auditTotalCount, setAuditTotalCount] = useState(0);
  const [aiSummary, setAiSummary] = useState<any>(null);
  const [aiUsages, setAiUsages] = useState<any[]>([]);
  const [communitySummary, setCommunitySummary] = useState<any>(null);
  const [communityLoadFailed, setCommunityLoadFailed] = useState(false);
  const [reportedAccounts, setReportedAccounts] = useState<ReportedAccountAnalytics[]>([]);
  const [documentEngagement, setDocumentEngagement] = useState<DocumentEngagementAnalytics[]>([]);
  const [loading, setLoading] = useState(true);
  const analyticsMetric: 'downloads' | 'bookmarks' =
    searchParams.get('metric') === 'bookmarks' ? 'bookmarks' : 'downloads';
  const [filtersReadyTab, setFiltersReadyTab] = useState(() =>
    adminTab === 'account-analytics' || adminTab === 'document-analytics' ? '' : adminTab,
  );

  // Date states for Overview (Thuần Việt)
  const now = new Date();
  const [selectedMonth, setSelectedMonth] = useState<number>(now.getMonth() + 1);
  const [selectedYear, setSelectedYear] = useState<number>(now.getFullYear());
  const dashboardMonth = `${selectedYear}-${String(selectedMonth).padStart(2, '0')}`;
  const dashboardStartDate = `${dashboardMonth}-01`;
  const dashboardEndDate = `${dashboardMonth}-${String(new Date(selectedYear, selectedMonth, 0).getDate()).padStart(2, '0')}`;

  const [searchText, setSearchText] = useState('');
  const debouncedSearchText = useDebouncedValue(searchText, 1000);
  const [statusFilter, setStatusFilter] = useState('ALL');
  const [typeFilter, setTypeFilter] = useState('ALL');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [sortKey, setSortKey] = useState('createdAt');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');

  // Pagination
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
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
  const [transactionAction, setTransactionAction] = useState<{
    transaction: TransactionItem;
    action: TransactionAction;
  } | null>(null);
  const [transactionActionReason, setTransactionActionReason] = useState('');
  const [transactionActionError, setTransactionActionError] = useState('');
  const [processingTransaction, setProcessingTransaction] = useState(false);
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

  const loadDashboardData = useCallback(async () => {
    try {
      if (
        adminTab === 'documents' ||
        adminTab === 'report-config' ||
        adminTab === 'system-config' ||
        adminTab === 'transfer-config'
      )
        return;
      if (
        (adminTab === 'account-analytics' || adminTab === 'document-analytics') &&
        filtersReadyTab !== adminTab
      )
        return;
      if (adminTab === 'overview') {
        const mStart = dashboardStartDate;
        const mEnd = dashboardEndDate;
        const [dashboardResult, communityResult] = await Promise.allSettled([
          api.admin.getDashboard(mStart, mEnd),
          api.admin.getCommunityAnalyticsSummary(mStart, mEnd),
        ]);
        if (dashboardResult.status === 'rejected') throw dashboardResult.reason;
        setStats(dashboardResult.value);
        if (communityResult.status === 'fulfilled') {
          setCommunitySummary(communityResult.value);
          setCommunityLoadFailed(false);
        } else {
          console.error('Không thể tải thống kê cộng đồng:', communityResult.reason);
          setCommunitySummary(null);
          setCommunityLoadFailed(true);
        }
      } else if (adminTab === 'users') {
        const data = await api.admin.getUsers(page, pageSize, debouncedSearchText, statusFilter);
        setUsers(data.items || []);
        setTotalCount(data.totalCount || 0);
      } else if (adminTab === 'transactions') {
        const data = await api.admin.getTransactions(
          page,
          pageSize,
          debouncedSearchText,
          statusFilter,
          typeFilter,
          startDate,
          endDate,
        );
        setTransactions(data.items || []);
        setTotalCount(data.totalCount || 0);
      } else if (adminTab === 'reports') {
        const data = await api.admin.getReports(page, pageSize, debouncedSearchText, statusFilter);
        setReports(data.items || []);
        setTotalCount(data.totalCount || 0);
      } else if (adminTab === 'account-analytics') {
        const data = await api.admin.getReportedAccounts(
          page,
          pageSize,
          debouncedSearchText,
          startDate,
          endDate,
          sortKey,
          sortDirection,
        );
        setReportedAccounts(data.items || []);
        setTotalCount(data.totalCount || 0);
      } else if (adminTab === 'document-analytics') {
        const data = await api.admin.getDocumentEngagementRanking(
          analyticsMetric,
          page,
          pageSize,
          debouncedSearchText,
          startDate,
          endDate,
          sortKey,
          sortDirection,
        );
        setDocumentEngagement(data.items || []);
        setTotalCount(data.totalCount || 0);
      } else if (adminTab === 'audit-log') {
        const res = await api.admin.getAuditLogs(page, pageSize);
        setAuditLogs(res.items || res.data || (Array.isArray(res) ? res : []));
        setAuditTotalPages(res.totalPages || 1);
        setAuditTotalCount(res.totalCount || (Array.isArray(res) ? res.length : 0));
      } else if (adminTab === 'ai-observability') {
        const [summary, usages] = await Promise.all([
          api.admin.getAiObservabilitySummary(),
          api.admin.getAiObservabilityUsages(page, pageSize),
        ]);
        setAiSummary(summary);
        setAiUsages(usages.items || usages.data || []);
        setTotalCount(usages.totalCount || 0);
      }
    } catch (err: any) {
      console.error('Error loading admin page data:', err);
    } finally {
      setLoading(false);
    }
  }, [
    adminTab,
    dashboardStartDate,
    dashboardEndDate,
    page,
    pageSize,
    debouncedSearchText,
    statusFilter,
    typeFilter,
    startDate,
    endDate,
    analyticsMetric,
    sortKey,
    sortDirection,
    filtersReadyTab,
  ]);

  useEffect(() => {
    setLoading(true);
    setPage(1);
    setSearchText(searchParams.get('q') || '');
    setStatusFilter(searchParams.get('status') || 'ALL');
    setTypeFilter('ALL');
    setStartDate(searchParams.get('startDate') || '');
    setEndDate(searchParams.get('endDate') || '');
    setFiltersReadyTab(adminTab);
    setSortKey(
      adminTab === 'users'
        ? 'userId'
        : adminTab === 'transactions'
          ? 'transactionId'
          : adminTab === 'account-analytics'
            ? 'totalReports'
            : adminTab === 'document-analytics'
              ? analyticsMetric === 'downloads' ? 'uniqueDownloads' : 'uniqueBookmarks'
              : 'reportId',
    );
    setSortDirection('desc');
    setShowCreateUserModal(false);
    setShowEditUserModal(false);
    setEditingUser(null);
    setUpdatingUser(false);
    setSelectedReport(null);
    setSelectedReportDocument(null);
    setReportPreviewLoading(false);
  }, [adminTab, searchParams, analyticsMetric]);

  useEffect(() => {
    loadDashboardData();
  }, [loadDashboardData]);

  const goToAdminTab = (
    tab: string,
    options?: {
      query?: string;
      status?: string;
      metric?: 'downloads' | 'bookmarks';
      startDate?: string;
      endDate?: string;
    },
  ) => {
    const params = new URLSearchParams({ tab });
    if (options?.query) params.set('q', options.query);
    if (options?.status) params.set('status', options.status);
    if (options?.metric) params.set('metric', options.metric);
    if (options?.startDate) params.set('startDate', options.startDate);
    if (options?.endDate) params.set('endDate', options.endDate);
    navigate(`/admin?${params.toString()}`);
  };

  const showCommunityAnalyticsAllTime = () => {
    setPage(1);
    setStartDate('');
    setEndDate('');
    goToAdminTab(
      adminTab,
      adminTab === 'document-analytics' ? { metric: analyticsMetric } : undefined,
    );
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

  const filteredUsers = users;
  const filteredTransactions = transactions;
  const filteredReports = reports;

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
  const activeRows = useMemo(() => {
    const list = [...rawRows];
    if (!sortKey) return list;
    return list.sort((a, b) => {
      let av = a[sortKey] ?? '';
      let bv = b[sortKey] ?? '';
      if (typeof av === 'number' && typeof bv === 'number') {
        return sortDirection === 'asc' ? av - bv : bv - av;
      }
      const keyLower = sortKey.toLowerCase();
      if (keyLower.includes('at') || keyLower.includes('date') || keyLower.includes('time')) {
        const at = new Date(av || 0).getTime();
        const bt = new Date(bv || 0).getTime();
        return sortDirection === 'asc' ? at - bt : bt - at;
      }
      return sortDirection === 'asc'
        ? String(av).localeCompare(String(bv), 'vi')
        : String(bv).localeCompare(String(av), 'vi');
    });
  }, [rawRows, sortKey, sortDirection]);
  const sortRows = useCallback(
    (rows: any[]) =>
      [...rows].sort((a, b) => {
        const value = (item: any) => {
          if (sortKey === 'logId') return item.logId || item.id || 0;
          if (sortKey === 'actor') return item.actorName || item.username || item.actorUserId || '';
          if (sortKey === 'username') return item.username || item.userId || '';
          if (sortKey === 'target') return `${item.targetType || ''} ${item.targetId || ''}`;
          if (sortKey === 'modelOperation') return `${item.model || ''} ${item.operation || ''}`;
          return item[sortKey] ?? '';
        };
        const av = value(a);
        const bv = value(b);
        let result: number;
        if (typeof av === 'number' && typeof bv === 'number') result = av - bv;
        else if (/at$|time|timestamp/i.test(sortKey)) {
          result = new Date(av || 0).getTime() - new Date(bv || 0).getTime();
        } else result = String(av).localeCompare(String(bv), 'vi', { numeric: true });
        return sortDirection === 'asc' ? result : -result;
      }),
    [sortKey, sortDirection],
  );
  const sortedAuditLogs = useMemo(() => sortRows(auditLogs), [auditLogs, sortRows]);
  const sortedAiUsages = useMemo(() => sortRows(aiUsages), [aiUsages, sortRows]);
  const sortedAiModels = useMemo(() => sortRows(aiSummary?.byModel || []), [aiSummary, sortRows]);
  const sortedAiOperations = useMemo(
    () => sortRows(aiSummary?.byOperation || []),
    [aiSummary, sortRows],
  );
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const pagedRows = activeRows;

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
  const openTransactionAction = (transaction: TransactionItem, action: TransactionAction) => {
    setTransactionAction({ transaction, action });
    setTransactionActionReason('');
    setTransactionActionError('');
  };

  const closeTransactionAction = () => {
    if (processingTransaction) return;
    setTransactionAction(null);
    setTransactionActionReason('');
    setTransactionActionError('');
  };

  const handleTransactionActionSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!transactionAction) return;

    const { transaction, action } = transactionAction;
    const reason = transactionActionReason.trim();
    if (action !== 'APPROVE' && !reason) {
      setTransactionActionError('Vui lòng nhập lý do để lưu vào lịch sử đối soát.');
      return;
    }

    setProcessingTransaction(true);
    setTransactionActionError('');
    try {
      if (action === 'APPROVE') {
        await api.admin.updateTransaction(transaction.transactionId, 'SUCCESS');
      } else if (action === 'REJECT') {
        await api.admin.updateTransaction(transaction.transactionId, 'CANCELLED', reason);
      } else if (action === 'REVERSE_DEPOSIT') {
        await api.admin.reverseDeposit(transaction.transactionId, reason);
      } else {
        await api.admin.refundTransaction(transaction.transactionId, reason);
      }

      const successMessage =
        action === 'APPROVE'
          ? 'Đã duyệt giao dịch thành công.'
          : action === 'REJECT'
            ? 'Đã từ chối giao dịch.'
            : action === 'REVERSE_DEPOSIT'
              ? 'Đã thu hồi giao dịch nạp tiền.'
              : 'Đã hoàn tiền giao dịch thành công.';
      setTransactionAction(null);
      setTransactionActionReason('');
      await loadDashboardData();
      notify(successMessage, 'success');
    } catch (err: any) {
      setTransactionActionError(err.message || 'Không thể xử lý giao dịch.');
    } finally {
      setProcessingTransaction(false);
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
      {/* Admin Title & Actions */}
      <div className="admin-header glass-card">
        <div className="admin-header-text">
          <h1>Bảng Điều Khiển Quản Trị</h1>
          <p>
            Quản trị hệ thống, phê duyệt giao dịch ví, kiểm duyệt tài liệu và theo dõi vận hành nền
            tảng.
          </p>
        </div>
        <button
          type="button"
          className="admin-refresh-btn"
          onClick={loadDashboardData}
          disabled={loading}
          title="Tải lại dữ liệu trang hiện tại"
        >
          <Loader className={loading ? 'spin' : ''} size={15} />
          <span>Làm mới</span>
        </button>
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

              <section className="community-analytics-section" aria-labelledby="community-analytics-title">
                <div className="admin-section-heading community-analytics-heading">
                  <div>
                    <h3 id="community-analytics-title">Thống kê cộng đồng</h3>
                    <p className="section-subtitle">
                      Xếp hạng theo người dùng · Tháng {selectedMonth}/{selectedYear}
                    </p>
                    {communityLoadFailed && (
                      <p className="analytics-load-warning">
                        Chưa tải được thống kê cộng đồng. Hãy khởi động lại backend để áp dụng API mới.
                      </p>
                    )}
                  </div>
                </div>
                <div className="stats-grid community-stats-grid">
                  <button
                    type="button"
                    className="stat-box stat-link glass-card"
                    onClick={() =>
                      goToAdminTab('account-analytics', {
                        startDate: dashboardStartDate,
                        endDate: dashboardEndDate,
                      })
                    }
                  >
                    <AlertOctagon size={28} className="stat-icon red" />
                    <div className="stat-details">
                      <span className="stat-label">Tài khoản bị báo cáo nhiều nhất</span>
                      <span className="stat-value">
                        {communityLoadFailed ? '—' : (communitySummary?.mostReportedAccount?.totalReports ?? 0)}
                      </span>
                      <small>
                        {communitySummary?.mostReportedAccount
                          ? `${communitySummary.mostReportedAccount.username} · ${communitySummary.mostReportedAccount.reportedDocumentCount} tài liệu`
                          : 'Chưa có báo cáo trong kỳ'}
                      </small>
                    </div>
                  </button>
                  <button
                    type="button"
                    className="stat-box stat-link glass-card"
                    onClick={() =>
                      goToAdminTab('document-analytics', {
                        metric: 'downloads',
                        startDate: dashboardStartDate,
                        endDate: dashboardEndDate,
                      })
                    }
                  >
                    <Download size={28} className="stat-icon blue" />
                    <div className="stat-details">
                      <span className="stat-label">Tài liệu được tải nhiều nhất</span>
                      <span className="stat-value">
                        {communityLoadFailed ? '—' : (communitySummary?.mostDownloadedDocument?.uniqueDownloads ?? 0)}
                      </span>
                      <small>
                        {communitySummary?.mostDownloadedDocument?.title ?? 'Chưa có lượt tải trong kỳ'}
                      </small>
                    </div>
                  </button>
                  <button
                    type="button"
                    className="stat-box stat-link glass-card"
                    onClick={() =>
                      goToAdminTab('document-analytics', {
                        metric: 'bookmarks',
                        startDate: dashboardStartDate,
                        endDate: dashboardEndDate,
                      })
                    }
                  >
                    <Bookmark size={28} className="stat-icon purple" />
                    <div className="stat-details">
                      <span className="stat-label">Tài liệu được lưu nhiều nhất</span>
                      <span className="stat-value">
                        {communityLoadFailed ? '—' : (communitySummary?.mostBookmarkedDocument?.uniqueBookmarks ?? 0)}
                      </span>
                      <small>
                        {communitySummary?.mostBookmarkedDocument?.title ?? 'Chưa có lượt lưu trong kỳ'}
                      </small>
                    </div>
                  </button>
                </div>
              </section>

              <div className="overview-finance-section">
                <div className="overview-finance-header">
                  <div className="overview-finance-title">
                    <h4>Doanh thu & Biến động ví theo tháng</h4>
                    <p className="text-muted">
                      Theo dõi tổng tiền nạp và tổng giá trị các gói Premium kích hoạt
                    </p>
                  </div>
                  <div className="month-picker-wrapper">
                    <span className="month-picker-label">Thời gian:</span>
                    <select
                      className="input-control month-select"
                      value={selectedMonth}
                      onChange={(e) => setSelectedMonth(Number(e.target.value))}
                      aria-label="Chọn tháng"
                    >
                      {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                        <option key={m} value={m}>
                          Tháng {m}
                        </option>
                      ))}
                    </select>
                    <select
                      className="input-control year-select"
                      value={selectedYear}
                      onChange={(e) => setSelectedYear(Number(e.target.value))}
                      aria-label="Chọn năm"
                    >
                      {[2024, 2025, 2026, 2027, 2028].map((y) => (
                        <option key={y} value={y}>
                          Năm {y}
                        </option>
                      ))}
                    </select>
                    {(selectedMonth !== now.getMonth() + 1 ||
                      selectedYear !== now.getFullYear()) && (
                      <button
                        type="button"
                        className="btn-secondary month-reset-btn"
                        onClick={() => {
                          setSelectedMonth(now.getMonth() + 1);
                          setSelectedYear(now.getFullYear());
                        }}
                      >
                        Tháng này
                      </button>
                    )}
                  </div>
                </div>

                <div className="dashboard-finance-grid">
                  <button
                    type="button"
                    className="dashboard-finance dashboard-finance-link glass-card income-card"
                    onClick={() => goToAdminTab('transactions', { status: 'SUCCESS' })}
                  >
                    <div className="finance-header">
                      <span>Tổng tiền nạp vào ví</span>
                      <small>
                        Nạp tiền thành công (Tháng {selectedMonth}/{selectedYear})
                      </small>
                    </div>
                    <strong>
                      {Number(stats.successfulDeposits ?? 0).toLocaleString('vi-VN')}đ
                    </strong>
                  </button>
                  <button
                    type="button"
                    className="dashboard-finance dashboard-finance-link glass-card premium-revenue-card"
                    onClick={() => goToAdminTab('transactions', { status: 'SUCCESS' })}
                  >
                    <div className="finance-header">
                      <span>Tổng giá trị các gói Premium đã mua</span>
                      <small>
                        Gói Premium đã kích hoạt (Tháng {selectedMonth}/{selectedYear})
                      </small>
                    </div>
                    <strong className="premium-revenue-text">
                      {Number(Math.abs(stats.successfulWithdrawals ?? 0)).toLocaleString('vi-VN')}đ
                    </strong>
                  </button>
                </div>
              </div>

              <div className="dashboard-activity-grid">
                <section className="activity-panel glass-card">
                  <div className="activity-panel-header">
                    <h4>Giao dịch gần đây</h4>
                    <button
                      type="button"
                      className="activity-view-all-btn"
                      onClick={() => goToAdminTab('transactions')}
                    >
                      Xem tất cả →
                    </button>
                  </div>
                  <div className="activity-list-container">
                    {overviewTransactions.length === 0 ? (
                      <p className="empty-hint">Không có giao dịch nào phù hợp</p>
                    ) : (
                      overviewTransactions.map((transaction: any) => (
                        <button
                          type="button"
                          className="activity-row activity-link"
                          key={transaction.transactionId}
                          onClick={() =>
                            goToAdminTab('transactions', {
                              query: String(transaction.transactionId),
                            })
                          }
                        >
                          <div>
                            <strong>{transaction.username}</strong>
                            <small>
                              #{transaction.transactionId} · {transaction.status}
                            </small>
                          </div>
                          <span className="amount-badge">
                            {Number(transaction.amount).toLocaleString('vi-VN')}đ
                          </span>
                        </button>
                      ))
                    )}
                  </div>
                </section>
                <section className="activity-panel glass-card">
                  <div className="activity-panel-header">
                    <h4>Report gần đây</h4>
                    <button
                      type="button"
                      className="activity-view-all-btn"
                      onClick={() => goToAdminTab('reports')}
                    >
                      Xem tất cả →
                    </button>
                  </div>
                  <div className="activity-list-container">
                    {overviewReports.length === 0 ? (
                      <p className="empty-hint">Không có báo cáo nào phù hợp</p>
                    ) : (
                      overviewReports.map((report: any) => (
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
                          <span
                            className={`report-status-tag ${report.status === 'PENDING' ? 'pending' : ''}`}
                          >
                            {report.status}
                          </span>
                        </button>
                      ))
                    )}
                  </div>
                </section>
              </div>
            </div>
          ) : adminTab === 'account-analytics' ? (
            <div className="users-pane animate-fade-in">
              <div className="admin-section-heading">
                <div>
                  <h3>Tài khoản có tài liệu bị báo cáo nhiều nhất</h3>
                  <p className="section-subtitle">
                    Báo cáo chưa xác minh được hiển thị riêng; thứ hạng này không đồng nghĩa tài khoản đã vi phạm.
                  </p>
                </div>
              </div>
              <div className="admin-toolbar analytics-toolbar">
                <input
                  className="input-control"
                  placeholder="Tìm tên hoặc email chủ tài liệu..."
                  value={searchText}
                  onChange={(event) => { setSearchText(event.target.value); setPage(1); }}
                />
                <input type="date" className="input-control" value={startDate} onChange={(event) => { setStartDate(event.target.value); setPage(1); }} title="Từ ngày" />
                <input type="date" className="input-control" value={endDate} onChange={(event) => { setEndDate(event.target.value); setPage(1); }} title="Đến ngày" />
                <button
                  type="button"
                  className="btn-secondary analytics-all-time-btn"
                  onClick={showCommunityAnalyticsAllTime}
                  disabled={!startDate && !endDate}
                >
                  Toàn thời gian
                </button>
              </div>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead><tr><th>{sortHeader('rank', 'Hạng')}</th><th>{sortHeader('username', 'Tài khoản')}</th><th>{sortHeader('reportedDocumentCount', 'Tài liệu bị báo cáo')}</th><th>{sortHeader('totalReports', 'Tổng báo cáo')}</th><th>{sortHeader('pendingReports', 'Chờ xác minh')}</th><th>{sortHeader('confirmedReports', 'Đã xác nhận')}</th><th>{sortHeader('status', 'Trạng thái')}</th><th>Thao tác</th></tr></thead>
                  <tbody>
                    {reportedAccounts.length === 0 ? (
                      <tr><td colSpan={8} className="analytics-empty-cell">Không có dữ liệu phù hợp trong khoảng thời gian này.</td></tr>
                    ) : reportedAccounts.map((account, index) => (
                      <tr key={account.userId}>
                        <td className="monospace-text">#{sortKey === 'rank' && sortDirection === 'desc' ? totalCount - (page - 1) * pageSize - index : (page - 1) * pageSize + index + 1}</td>
                        <td><strong>{account.username}</strong><br /><small>{account.email || `ID #${account.userId}`}</small></td>
                        <td>{account.reportedDocumentCount}</td>
                        <td className="bold-text">{account.totalReports}</td>
                        <td>{account.pendingReports}</td>
                        <td>{account.confirmedReports}</td>
                        <td><span className={`status-badge ${account.status}`}>{account.status === 'ACTIVE' ? 'Hoạt động' : account.status}</span></td>
                        <td><button className="btn-secondary" onClick={() => goToAdminTab('users', { query: account.email || account.username })}>Xem tài khoản</button></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination page={page} totalPages={totalPages} setPage={setPage} total={totalCount} />
            </div>
          ) : adminTab === 'document-analytics' ? (
            <div className="users-pane animate-fade-in">
              <div className="admin-section-heading">
                <div>
                  <h3>{analyticsMetric === 'downloads' ? 'Tài liệu được tải nhiều nhất' : 'Tài liệu được lưu nhiều nhất'}</h3>
                  <p className="section-subtitle">Mỗi người dùng chỉ được tính một lần cho mỗi tài liệu; không tính chủ sở hữu.</p>
                </div>
                <div className="analytics-metric-switch">
                  <button className={analyticsMetric === 'downloads' ? 'btn-primary' : 'btn-secondary'} onClick={() => goToAdminTab('document-analytics', { metric: 'downloads', startDate, endDate })}>Lượt tải</button>
                  <button className={analyticsMetric === 'bookmarks' ? 'btn-primary' : 'btn-secondary'} onClick={() => goToAdminTab('document-analytics', { metric: 'bookmarks', startDate, endDate })}>Lượt lưu</button>
                </div>
              </div>
              <div className="admin-toolbar analytics-toolbar">
                <input className="input-control" placeholder="Tìm tài liệu hoặc chủ sở hữu..." value={searchText} onChange={(event) => { setSearchText(event.target.value); setPage(1); }} />
                <input type="date" className="input-control" value={startDate} onChange={(event) => { setStartDate(event.target.value); setPage(1); }} title="Từ ngày" />
                <input type="date" className="input-control" value={endDate} onChange={(event) => { setEndDate(event.target.value); setPage(1); }} title="Đến ngày" />
                <button
                  type="button"
                  className="btn-secondary analytics-all-time-btn"
                  onClick={showCommunityAnalyticsAllTime}
                  disabled={!startDate && !endDate}
                >
                  Toàn thời gian
                </button>
              </div>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead><tr><th>{sortHeader('rank', 'Hạng')}</th><th>{sortHeader('title', 'Tài liệu')}</th><th>{sortHeader('ownerUsername', 'Chủ sở hữu')}</th><th>{sortHeader('uniqueDownloads', 'Lượt tải')}</th><th>{sortHeader('uniqueBookmarks', 'Lượt lưu')}</th><th>{sortHeader('viewCount', 'Lượt xem')}</th><th>{sortHeader('sharingPermission', 'Quyền xem')}</th><th>Thao tác</th></tr></thead>
                  <tbody>
                    {documentEngagement.length === 0 ? (
                      <tr><td colSpan={8} className="analytics-empty-cell">Không có dữ liệu phù hợp trong khoảng thời gian này.</td></tr>
                    ) : documentEngagement.map((document, index) => (
                      <tr key={document.documentId}>
                        <td className="monospace-text">#{sortKey === 'rank' && sortDirection === 'desc' ? totalCount - (page - 1) * pageSize - index : (page - 1) * pageSize + index + 1}</td>
                        <td><div className="document-analytics-title"><FileTypeIcon extension={document.fileExtension || ''} size={20} /><strong>{document.title}</strong></div><small>ID #{document.documentId}</small></td>
                        <td>{document.ownerUsername}</td>
                        <td className={analyticsMetric === 'downloads' ? 'bold-text' : ''}>{document.uniqueDownloads}</td>
                        <td className={analyticsMetric === 'bookmarks' ? 'bold-text' : ''}>{document.uniqueBookmarks}</td>
                        <td>{document.viewCount}</td>
                        <td><span className="role-badge">{document.sharingPermission}</span></td>
                        <td><button className="btn-secondary" onClick={() => goToAdminTab('documents', { query: document.title })}>Xem tài liệu</button></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination page={page} totalPages={totalPages} setPage={setPage} total={totalCount} />
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
                      <th>{sortHeader('username', 'Tên')}</th>
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
                total={totalCount}
              />
            </div>
          ) : adminTab === 'transactions' ? (
            <div className="txs-pane animate-fade-in">
              <div className="admin-section-heading">
                <h3>Phê duyệt & Quản lý Giao dịch</h3>
              </div>

              <div className="transactions-toolbar-card glass-card">
                <div className="transactions-filter-grid">
                  <div className="transactions-filter-dates">
                    <span className="tx-filter-label">Thời gian:</span>
                    <input
                      type="date"
                      className="input-control"
                      value={startDate}
                      onChange={(e) => {
                        setStartDate(e.target.value);
                        setPage(1);
                      }}
                      title="Từ ngày"
                    />
                    <span className="tx-arrow">→</span>
                    <input
                      type="date"
                      className="input-control"
                      value={endDate}
                      onChange={(e) => {
                        setEndDate(e.target.value);
                        setPage(1);
                      }}
                      title="Đến ngày"
                    />
                    {(startDate || endDate) && (
                      <button
                        className="btn-secondary filter-clear-btn"
                        onClick={() => {
                          setStartDate('');
                          setEndDate('');
                          setPage(1);
                        }}
                      >
                        Xóa ngày
                      </button>
                    )}
                  </div>
                  <div className="transactions-filter-controls">
                    <input
                      className="input-control tx-search-input"
                      placeholder="Lọc theo tên, mã giao dịch, mã đối soát..."
                      value={searchText}
                      onChange={(event) => {
                        setSearchText(event.target.value);
                        setPage(1);
                      }}
                    />
                    <div className="tx-dropdowns-row">
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
                        <option value="REFUNDED">Đã hoàn tiền</option>
                      </select>
                      <select
                        className="input-control"
                        value={typeFilter}
                        onChange={(event) => {
                          setTypeFilter(event.target.value);
                          setPage(1);
                        }}
                      >
                        <option value="ALL">Tất cả loại giao dịch</option>
                        <option value="DEPOSIT">Nạp tiền (Deposit)</option>
                        <option value="WITHDRAW">Mua Premium (Withdraw)</option>
                        <option value="REFUND">Hoàn tiền (Refund)</option>
                      </select>
                      <select
                        className="input-control tx-sort-select"
                        value={sortDirection}
                        onChange={(e) => setSortDirection(e.target.value as 'asc' | 'desc')}
                      >
                        <option value="desc">Mới nhất trước</option>
                        <option value="asc">Cũ nhất trước</option>
                      </select>
                    </div>
                  </div>
                </div>
              </div>
              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>{sortHeader('transactionId', 'ID')}</th>
                      <th>{sortHeader('username', 'Tên')}</th>
                      <th>{sortHeader('type', 'Loại')}</th>
                      <th>{sortHeader('amount', 'Số tiền')}</th>
                      <th>{sortHeader('referenceCode', 'Mã đối soát')}</th>
                      <th>{sortHeader('bankId', 'Ngân hàng')}</th>
                      <th>{sortHeader('approverName', 'Người duyệt')}</th>
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
                          <td>
                            <span className={`role-badge ${tx.type}`}>
                              {tx.type === 'DEPOSIT'
                                ? 'Nạp tiền'
                                : tx.type === 'WITHDRAW'
                                  ? 'Mua Premium'
                                  : 'Hoàn tiền'}
                            </span>
                          </td>
                          <td className={`tx-value ${tx.amount > 0 ? 'positive' : 'negative'}`}>
                            {tx.amount > 0 ? '+' : ''}
                            {tx.amount.toLocaleString()}đ
                          </td>
                          <td>{tx.referenceCode || <span className="text-muted">—</span>}</td>
                          <td>{tx.bankId || <span className="text-muted">—</span>}</td>
                          <td>{tx.approverName || <span className="text-muted">—</span>}</td>
                          <td>
                            <div style={{ display: 'flex', flexDirection: 'column' }}>
                              <span className={`tx-status-badge ${tx.status}`}>{tx.status}</span>
                              {tx.failureReason && (
                                <small
                                  style={{ color: '#ef4444', fontSize: '11px', marginTop: '2px' }}
                                >
                                  Lý do: {tx.failureReason}
                                </small>
                              )}
                            </div>
                          </td>
                          <td>{formatDateTime(tx.startedAt)}</td>
                          <td>
                            {isPending ? (
                              <div className="table-actions">
                                <button
                                  onClick={() => openTransactionAction(tx, 'APPROVE')}
                                  className="action-btn approve"
                                  title="Duyệt giao dịch"
                                >
                                  <Check size={14} />
                                </button>
                                <button
                                  onClick={() => openTransactionAction(tx, 'REJECT')}
                                  className="action-btn reject"
                                  title="Từ chối giao dịch"
                                >
                                  <X size={14} />
                                </button>
                              </div>
                            ) : (
                              <div className="table-actions">
                                {tx.status === 'SUCCESS' && tx.type !== 'REFUND' && (
                                  <button
                                    onClick={() =>
                                      openTransactionAction(
                                        tx,
                                        tx.type === 'DEPOSIT' ? 'REVERSE_DEPOSIT' : 'REFUND',
                                      )
                                    }
                                    className="action-btn reject"
                                    title={
                                      tx.type === 'DEPOSIT'
                                        ? 'Thu hồi tiền nạp'
                                        : 'Hoàn tiền giao dịch'
                                    }
                                    style={{
                                      padding: '4px 8px',
                                      fontSize: '11px',
                                      height: 'auto',
                                      width: 'auto',
                                    }}
                                  >
                                    {tx.type === 'DEPOSIT' ? 'Thu hồi' : 'Hoàn tiền'}
                                  </button>
                                )}
                                {tx.status !== 'SUCCESS' && (
                                  <span className="text-muted">Đã xử lý</span>
                                )}
                                {tx.status === 'SUCCESS' && tx.type === 'REFUND' && (
                                  <span className="text-muted">Đã hoàn tiền</span>
                                )}
                              </div>
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
                total={totalCount}
              />
            </div>
          ) : adminTab === 'reports' ? (
            <div className="reports-pane animate-fade-in">
              <div className="admin-section-tabs">
                <NavLink className="active" to="/admin?tab=reports">
                  Báo cáo vi phạm
                </NavLink>
                <NavLink to="/admin?tab=documents">Tài liệu</NavLink>
                <NavLink to="/admin?tab=audit-log">Nhật ký hệ thống</NavLink>
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
                total={totalCount}
              />
            </div>
          ) : adminTab === 'audit-log' ? (
            <div
              className="reports-pane animate-fade-in"
              style={{ display: 'flex', flexDirection: 'column', height: '100%' }}
            >
              <div className="admin-section-tabs">
                <NavLink to="/admin?tab=reports">Báo cáo vi phạm</NavLink>
                <NavLink to="/admin?tab=documents">Tài liệu</NavLink>
                <NavLink className="active" to="/admin?tab=audit-log">
                  Nhật ký hệ thống
                </NavLink>
              </div>
              <div className="admin-section-heading">
                <h3>Nhật ký hoạt động hệ thống (Audit Logs)</h3>
              </div>
              <p style={{ color: 'var(--text-muted)', fontSize: '0.88rem', marginBottom: '1rem' }}>
                Theo dõi nhật ký các thay đổi quyền truy cập, chia sẻ tài liệu/thư mục và tác vụ hệ
                thống.
              </p>

              <div className="table-scroll">
                <table className="admin-table">
                  <thead>
                    <tr>
                      <th>{sortHeader('logId', 'Mã Log')}</th>
                      <th>{sortHeader('actor', 'Người thực hiện')}</th>
                      <th>{sortHeader('action', 'Hành động')}</th>
                      <th>{sortHeader('target', 'Đối tượng')}</th>
                      <th>{sortHeader('details', 'Chi tiết')}</th>
                      <th>{sortHeader('createdAt', 'Thời gian')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {auditLogs.length === 0 ? (
                      <tr>
                        <td
                          colSpan={6}
                          style={{
                            textAlign: 'center',
                            padding: '2rem',
                            color: 'var(--text-muted)',
                          }}
                        >
                          Chưa có nhật ký hoạt động nào.
                        </td>
                      </tr>
                    ) : (
                      sortedAuditLogs.map((log: any) => (
                        <tr key={log.logId || log.id}>
                          <td className="monospace-text">#{log.logId || log.id}</td>
                          <td>
                            <strong>
                              {log.actorName || log.username || `User #${log.actorUserId}`}
                            </strong>
                          </td>
                          <td>
                            <span className="role-badge ADMIN">{log.action}</span>
                          </td>
                          <td>
                            <span className="bold-text">
                              {log.targetType} #{log.targetId}
                            </span>
                          </td>
                          <td>{log.details || 'N/A'}</td>
                          <td>{formatDateTime(log.createdAt || log.timestamp)}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>

              <Pagination
                page={page}
                totalPages={auditTotalPages}
                setPage={setPage}
                total={auditTotalCount}
              />
            </div>
          ) : adminTab === 'ai-observability' ? (
            <div className="ai-observability-pane animate-fade-in">
              <div className="admin-section-heading">
                <div>
                  <h3>Giám sát Hệ thống AI (AI Observability)</h3>
                  <p className="section-subtitle">
                    Theo dõi chi phí API, độ trễ phản hồi, tổng token tiêu thụ và hiệu năng mô hình
                    AI
                  </p>
                </div>
              </div>

              {/* KPI Top Cards */}
              <div className="stats-grid ai-stats-grid">
                <div className="stat-box glass-card">
                  <Bot size={28} className="stat-icon blue" />
                  <div className="stat-details">
                    <span className="stat-label">Tổng lượt gọi AI</span>
                    <span className="stat-value">
                      {aiSummary?.totalRequests?.toLocaleString() || 0}
                    </span>
                    <small>
                      Lỗi: {aiSummary?.errorCount || 0} ({aiSummary?.errorRatePercent || 0}%)
                    </small>
                  </div>
                </div>

                <div className="stat-box glass-card">
                  <Cpu size={28} className="stat-icon purple" />
                  <div className="stat-details">
                    <span className="stat-label">Tokens Tiêu Thụ</span>
                    <span className="stat-value">
                      {aiSummary?.totalTokens?.toLocaleString() || 0}
                    </span>
                    <small>
                      In: {aiSummary?.totalPromptTokens?.toLocaleString() || 0} · Out:{' '}
                      {aiSummary?.totalCompletionTokens?.toLocaleString() || 0}
                    </small>
                  </div>
                </div>

                <div className="stat-box glass-card">
                  <DollarSign size={28} className="stat-icon green" />
                  <div className="stat-details">
                    <span className="stat-label">Tổng Chi Phí Ước Tính</span>
                    <span className="stat-value" style={{ color: '#34d399' }}>
                      ${aiSummary?.totalCostUsd || '0.0000'}
                    </span>
                    <small>
                      Cache Tokens: {aiSummary?.totalCachedTokens?.toLocaleString() || 0}
                    </small>
                  </div>
                </div>

                <div className="stat-box glass-card">
                  <Zap size={28} className="stat-icon yellow" />
                  <div className="stat-details">
                    <span className="stat-label">Độ Trễ Trung Bình</span>
                    <span className="stat-value" style={{ color: '#fbbf24' }}>
                      {aiSummary?.avgLatencyMs || 0} ms
                    </span>
                    <small>Thời gian phản hồi trung bình</small>
                  </div>
                </div>
              </div>

              {/* Breakdown Grid: Clean Model & Operation Tables */}
              <div className="ai-breakdown-grid">
                <div className="ai-breakdown-card glass-card">
                  <div className="ai-breakdown-header">
                    <div className="ai-breakdown-title">
                      <Cpu size={18} className="ai-breakdown-icon blue" />
                      <h4>Phân bổ theo Model AI</h4>
                    </div>
                    <span className="ai-chart-badge">
                      {aiSummary?.byModel?.length || 0} Mô hình
                    </span>
                  </div>
                  <div className="table-scroll">
                    <table className="admin-table mini-table">
                      <thead>
                        <tr>
                          <th>{sortHeader('model', 'Model')}</th>
                          <th>{sortHeader('count', 'Lượt gọi')}</th>
                          <th>{sortHeader('tokens', 'Tokens')}</th>
                          <th>{sortHeader('cost', 'Chi phí')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {aiSummary?.byModel && aiSummary.byModel.length > 0 ? (
                          sortedAiModels.map((m: any, idx: number) => (
                            <tr key={idx}>
                              <td>
                                <strong className="model-name">{m.model}</strong>
                              </td>
                              <td>{m.count?.toLocaleString()}</td>
                              <td>{m.tokens?.toLocaleString()}</td>
                              <td className="cost-tag">${Number(m.cost || 0).toFixed(4)}</td>
                            </tr>
                          ))
                        ) : (
                          <tr>
                            <td
                              colSpan={4}
                              style={{ textAlign: 'center', color: 'var(--text-muted)' }}
                            >
                              Chưa có dữ liệu
                            </td>
                          </tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>

                <div className="ai-breakdown-card glass-card">
                  <div className="ai-breakdown-header">
                    <div className="ai-breakdown-title">
                      <Zap size={18} className="ai-breakdown-icon yellow" />
                      <h4>Phân bổ theo Tác vụ (Operation)</h4>
                    </div>
                    <span className="ai-chart-badge">
                      {aiSummary?.byOperation?.length || 0} Tác vụ
                    </span>
                  </div>
                  <div className="table-scroll">
                    <table className="admin-table mini-table">
                      <thead>
                        <tr>
                          <th>{sortHeader('operation', 'Tác vụ')}</th>
                          <th>{sortHeader('count', 'Lượt gọi')}</th>
                          <th>{sortHeader('tokens', 'Tokens')}</th>
                          <th>{sortHeader('cost', 'Chi phí')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {aiSummary?.byOperation && aiSummary.byOperation.length > 0 ? (
                          sortedAiOperations.map((op: any, idx: number) => (
                            <tr key={idx}>
                              <td>
                                <span className="role-badge ADMIN">{op.operation}</span>
                              </td>
                              <td>{op.count?.toLocaleString()}</td>
                              <td>{op.tokens?.toLocaleString()}</td>
                              <td className="cost-tag">${Number(op.cost || 0).toFixed(4)}</td>
                            </tr>
                          ))
                        ) : (
                          <tr>
                            <td
                              colSpan={4}
                              style={{ textAlign: 'center', color: 'var(--text-muted)' }}
                            >
                              Chưa có dữ liệu
                            </td>
                          </tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>

              {/* Logs Table */}
              <div className="ai-logs-section glass-card">
                <div className="ai-logs-header">
                  <div>
                    <h4>Nhật ký gọi AI gần đây (Audit Usage Logs)</h4>
                    <p className="text-muted">
                      Chi tiết các request gọi API, độ trễ và chi phí theo thời gian thực
                    </p>
                  </div>
                </div>
                <div className="table-scroll">
                  <table className="admin-table">
                    <thead>
                      <tr>
                        <th>{sortHeader('usageId', 'ID')}</th>
                        <th>{sortHeader('username', 'Người dùng')}</th>
                        <th>{sortHeader('modelOperation', 'Model / Tác vụ')}</th>
                        <th>{sortHeader('totalTokens', 'Tokens (In / Out / Cache)')}</th>
                        <th>{sortHeader('latencyMs', 'Độ trễ')}</th>
                        <th>{sortHeader('estimatedCost', 'Chi phí')}</th>
                        <th>{sortHeader('status', 'Trạng thái')}</th>
                        <th>{sortHeader('createdAt', 'Thời gian')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {aiUsages.length === 0 ? (
                        <tr>
                          <td colSpan={8} style={{ textAlign: 'center', padding: '24px' }}>
                            Chưa có nhật ký sử dụng AI nào.
                          </td>
                        </tr>
                      ) : (
                        sortedAiUsages.map((u: any) => (
                          <tr key={u.usageId}>
                            <td className="monospace-text">#{u.usageId}</td>
                            <td className="bold-text">{u.username || `User #${u.userId}`}</td>
                            <td>
                              <div>
                                <strong>{u.model}</strong>
                              </div>
                              <small style={{ color: 'var(--text-secondary)' }}>
                                {u.operation}
                              </small>
                            </td>
                            <td>
                              <strong>{u.totalTokens?.toLocaleString()}</strong>
                              <div style={{ fontSize: '11px', color: 'var(--text-secondary)' }}>
                                In: {u.promptTokens} · Out: {u.completionTokens} · Cache:{' '}
                                {u.cachedTokens}
                              </div>
                            </td>
                            <td>
                              <span
                                className={`latency-tag ${u.latencyMs > 2000 ? 'high' : u.latencyMs > 1000 ? 'medium' : 'good'}`}
                              >
                                {u.latencyMs} ms
                              </span>
                            </td>
                            <td>
                              <span className="cost-tag">
                                ${Number(u.estimatedCost || 0).toFixed(6)}
                              </span>
                            </td>
                            <td>
                              <span
                                className={`tx-status-badge ${u.status === 'SUCCESS' ? 'SUCCESS' : 'CANCELLED'}`}
                              >
                                {u.status}
                              </span>
                            </td>
                            <td>{formatDateTime(u.createdAt)}</td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>

                <Pagination
                  page={page}
                  totalPages={Math.ceil(totalCount / pageSize) || 1}
                  setPage={setPage}
                  total={totalCount}
                />
              </div>
            </div>
          ) : null}
        </div>
      </div>

      {/* Modal: Transaction action */}
      {transactionAction &&
        (() => {
          const { transaction, action } = transactionAction;
          const requiresReason = action !== 'APPROVE';
          const title =
            action === 'APPROVE'
              ? 'Duyệt giao dịch'
              : action === 'REJECT'
                ? 'Từ chối giao dịch'
                : action === 'REVERSE_DEPOSIT'
                  ? 'Thu hồi giao dịch nạp tiền'
                  : 'Hoàn tiền giao dịch';
          const confirmLabel =
            action === 'APPROVE'
              ? 'Xác nhận duyệt'
              : action === 'REJECT'
                ? 'Xác nhận từ chối'
                : action === 'REVERSE_DEPOSIT'
                  ? 'Xác nhận thu hồi'
                  : 'Xác nhận hoàn tiền';
          const impactMessage =
            action === 'APPROVE'
              ? 'Giao dịch nạp tiền sẽ được hoàn tất và số dư người dùng được cập nhật.'
              : action === 'REJECT'
                ? 'Giao dịch sẽ bị hủy và không thể duyệt lại từ màn hình này.'
                : action === 'REVERSE_DEPOSIT'
                  ? 'Số tiền đã nạp sẽ bị trừ khỏi ví. Thao tác thất bại nếu số dư hiện tại không đủ.'
                  : 'Số tiền của giao dịch mua gói sẽ được hoàn lại vào ví người dùng.';

          return (
            <div className="modal-overlay" role="presentation" onMouseDown={closeTransactionAction}>
              <div
                className="modal-box transaction-action-modal glass-panel animate-slide-up"
                role="dialog"
                aria-modal="true"
                aria-labelledby="transaction-action-title"
                onMouseDown={(event) => event.stopPropagation()}
              >
                <div className="modal-title-row">
                  <div>
                    <h3 id="transaction-action-title">{title}</h3>
                    <p>Kiểm tra thông tin trước khi xác nhận thao tác tài chính.</p>
                  </div>
                  <button
                    type="button"
                    className="modal-close-button"
                    aria-label="Đóng popup"
                    onClick={closeTransactionAction}
                    disabled={processingTransaction}
                  >
                    <X size={20} />
                  </button>
                </div>

                <form onSubmit={handleTransactionActionSubmit} className="admin-form">
                  <div className="transaction-action-summary">
                    <div>
                      <span>Mã giao dịch</span>
                      <strong>#{transaction.transactionId}</strong>
                    </div>
                    <div>
                      <span>Người dùng</span>
                      <strong>{transaction.username}</strong>
                    </div>
                    <div>
                      <span>Loại</span>
                      <strong>{transaction.type}</strong>
                    </div>
                    <div>
                      <span>Số tiền</span>
                      <strong className="transaction-action-amount">
                        {transaction.amount.toLocaleString('vi-VN')}đ
                      </strong>
                    </div>
                    <div>
                      <span>Ngân hàng</span>
                      <strong>{transaction.bankId || '—'}</strong>
                    </div>
                    <div>
                      <span>Mã đối soát</span>
                      <strong>{transaction.referenceCode || '—'}</strong>
                    </div>
                  </div>

                  <div
                    className={`transaction-impact ${action === 'APPROVE' ? 'safe' : 'warning'}`}
                  >
                    {impactMessage}
                  </div>

                  {requiresReason && (
                    <div className="form-group">
                      <label htmlFor="transaction-action-reason">
                        {action === 'REJECT'
                          ? 'Lý do từ chối'
                          : action === 'REVERSE_DEPOSIT'
                            ? 'Lý do thu hồi'
                            : 'Lý do hoàn tiền'}
                      </label>
                      <textarea
                        id="transaction-action-reason"
                        className="input-control"
                        rows={4}
                        maxLength={500}
                        required
                        autoFocus
                        value={transactionActionReason}
                        onChange={(event) => {
                          setTransactionActionReason(event.target.value);
                          setTransactionActionError('');
                        }}
                        placeholder="Nhập lý do rõ ràng để phục vụ đối soát..."
                        disabled={processingTransaction}
                      />
                      <small>{transactionActionReason.length}/500 ký tự</small>
                    </div>
                  )}

                  {transactionActionError && (
                    <div className="error-alert">{transactionActionError}</div>
                  )}

                  <div className="modal-actions transaction-modal-actions">
                    <button
                      type="button"
                      className="btn-secondary"
                      onClick={closeTransactionAction}
                      disabled={processingTransaction}
                    >
                      Hủy
                    </button>
                    <button
                      type="submit"
                      className={action === 'APPROVE' ? 'btn-primary' : 'btn-danger'}
                      disabled={
                        processingTransaction || (requiresReason && !transactionActionReason.trim())
                      }
                    >
                      {processingTransaction ? <Loader className="spin" size={16} /> : confirmLabel}
                    </button>
                  </div>
                </form>
              </div>
            </div>
          );
        })()}

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
                    ? formatDateTime(selectedReport.createdAt)
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
          max-width: 1400px;
          margin: 0 auto;
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .admin-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 1.25rem 1.5rem;
          border-radius: var(--radius-md);
          background: rgba(255, 255, 255, 0.025);
          border: 1px solid rgba(255, 255, 255, 0.08);
          gap: 1rem;
          flex-wrap: wrap;
        }

        .admin-header-text h1 {
          font-size: 1.65rem;
          font-weight: 700;
          margin: 0 0 0.25rem;
          background: var(--accent-glow);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }

        .admin-header-text p {
          color: var(--text-secondary);
          font-size: 0.88rem;
          margin: 0;
        }

        .admin-refresh-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.45rem;
          padding: 0.5rem 1rem;
          font-size: 0.85rem;
          font-weight: 600;
          border-radius: var(--radius-sm);
          border: 1px solid rgba(255, 255, 255, 0.12);
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-primary);
          cursor: pointer;
          transition: all 0.2s ease;
        }

        .admin-refresh-btn:hover:not(:disabled) {
          background: rgba(255, 255, 255, 0.1);
          border-color: rgba(255, 255, 255, 0.25);
          transform: translateY(-1px);
        }

        .admin-refresh-btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .admin-layout-grid {
          display: block;
        }

        .admin-content-pane {
          display: flex;
          flex-direction: column;
          padding: 1.5rem 1.75rem;
          border-radius: var(--radius-lg);
          background: rgba(13, 17, 28, 0.7);
          backdrop-filter: blur(12px);
          border: 1px solid rgba(255, 255, 255, 0.08);
          min-height: calc(100vh - 12rem);
          box-sizing: border-box;
        }

        .admin-content-pane h3 {
          font-size: 1.15rem;
          font-weight: 700;
          margin: 0 0 1rem;
          padding-bottom: 0.65rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.06);
          color: var(--text-primary);
        }

        .admin-loader {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          min-height: 350px;
          color: var(--text-muted);
          gap: 0.75rem;
        }

        .admin-section-heading {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
          margin-bottom: 1rem;
        }

        .admin-section-heading h3 {
          margin: 0;
          padding: 0;
          border: none;
        }

        .admin-section-heading .btn-primary {
          display: inline-flex;
          align-items: center;
          gap: 0.4rem;
        }

        .admin-toolbar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
          margin: 0 0 1.25rem;
          flex-wrap: wrap;
        }

        .overview-finance-section {
          margin-bottom: 1.25rem;
        }

        .overview-finance-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
          margin-bottom: 0.85rem;
          flex-wrap: wrap;
        }

        .overview-finance-title h4 {
          font-size: 1rem;
          font-weight: 700;
          margin: 0 0 0.15rem;
          color: var(--text-primary);
        }

        .overview-finance-title p {
          font-size: 0.78rem;
          margin: 0;
        }

        .month-picker-wrapper {
          display: flex;
          align-items: center;
          gap: 0.6rem;
        }

        .month-picker-label {
          font-size: 0.82rem;
          color: var(--text-secondary);
          font-weight: 600;
        }

        .month-select,
        .year-select {
          padding: 0.45rem 0.85rem;
          font-size: 0.85rem;
          border-radius: var(--radius-sm);
          width: auto;
          min-width: 110px;
        }

        .month-reset-btn {
          padding: 0.45rem 0.75rem;
          font-size: 0.78rem;
          white-space: nowrap;
        }

        /* Transactions Filter Toolbar */
        .transactions-toolbar-card {
          padding: 1.25rem 1.5rem;
          margin-bottom: 1.25rem;
          background: rgba(255, 255, 255, 0.025);
          border: 1px solid rgba(255, 255, 255, 0.06);
          border-radius: var(--radius-md);
        }

        .transactions-filter-grid {
          display: grid;
          grid-template-columns: 1fr 1.3fr;
          gap: 1.5rem;
          align-items: flex-end;
        }

        .transactions-filter-dates {
          display: flex;
          flex-direction: column;
          gap: 0.45rem;
        }

        .tx-filter-label {
          font-size: 0.85rem;
          font-weight: 600;
          color: var(--text-secondary);
          margin-bottom: 0.15rem;
        }

        .tx-arrow {
          color: var(--text-muted);
          font-size: 0.85rem;
          line-height: 1;
        }

        .transactions-filter-controls {
          display: flex;
          flex-direction: column;
          gap: 0.65rem;
        }

        .tx-search-input {
          width: 100%;
        }

        .tx-dropdowns-row {
          display: grid;
          grid-template-columns: 1.2fr 1.2fr 1fr;
          gap: 0.5rem;
        }

        .tx-dropdowns-row select {
          font-size: 0.82rem;
          padding: 0.55rem 0.75rem;
        }

        .sort-controls {
          display: grid;
          grid-template-columns: 1fr 110px;
          gap: 0.5rem;
        }

        .admin-section-tabs {
          display: flex;
          gap: 0.5rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.08);
          margin-bottom: 1.25rem;
        }

        .admin-section-tabs a {
          padding: 0.65rem 1rem;
          color: var(--text-muted);
          border-bottom: 2px solid transparent;
          font-weight: 500;
          text-decoration: none;
          transition: all 0.2s ease;
        }

        .admin-section-tabs a:hover {
          color: var(--text-primary);
        }

        .admin-section-tabs a.active {
          color: var(--text-primary);
          border-color: var(--accent-purple);
          font-weight: 700;
        }

        .admin-pagination {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
          margin-top: 1.25rem;
          padding-top: 0.85rem;
          border-top: 1px solid rgba(255, 255, 255, 0.05);
          color: var(--text-muted);
          font-size: 0.85rem;
        }

        .admin-pagination div {
          display: flex;
          align-items: center;
          gap: 0.65rem;
        }

        .admin-pagination button:disabled {
          opacity: 0.4;
          cursor: not-allowed;
        }

        /* Overview Page */
        .stats-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(230px, 1fr));
          gap: 1rem;
          margin-bottom: 1.25rem;
        }

        .community-analytics-section {
          margin: 0.25rem 0 1.25rem;
        }

        .community-analytics-heading {
          align-items: flex-end;
          margin-bottom: 0.75rem;
        }

        .community-analytics-heading h3 {
          margin-bottom: 0.2rem;
        }

        .section-subtitle {
          margin: 0;
          color: var(--text-muted);
          font-size: 0.82rem;
        }

        .analytics-load-warning {
          margin: 0.35rem 0 0;
          color: #fbbf24;
          font-size: 0.78rem;
        }

        .community-stats-grid {
          grid-template-columns: repeat(3, minmax(0, 1fr));
        }

        .community-stats-grid .stat-details {
          min-width: 0;
        }

        .community-stats-grid small {
          display: block;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          max-width: 100%;
        }

        .analytics-toolbar .input-control {
          min-width: 170px;
          flex: 1;
        }

        .analytics-all-time-btn {
          white-space: nowrap;
          min-height: 42px;
        }

        .analytics-metric-switch,
        .document-analytics-title {
          display: flex;
          align-items: center;
          gap: 0.5rem;
        }

        .analytics-empty-cell {
          text-align: center;
          padding: 2rem !important;
          color: var(--text-muted);
        }

        .stat-box {
          display: flex;
          align-items: center;
          gap: 1rem;
          padding: 1.25rem;
          border-radius: var(--radius-md);
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid rgba(255, 255, 255, 0.06);
          transition: all 0.2s ease;
        }

        .stat-link {
          width: 100%;
          color: inherit;
          text-align: left;
          cursor: pointer;
          font: inherit;
        }

        .stat-link:hover,
        .stat-link:focus-visible {
          transform: translateY(-2px);
          border-color: var(--accent-blue);
          box-shadow: 0 6px 20px rgba(0, 180, 216, 0.15);
          outline: none;
        }

        .stat-icon {
          flex-shrink: 0;
          padding: 0.55rem;
          background: rgba(255, 255, 255, 0.04);
          border-radius: 10px;
        }

        .stat-icon.purple {
          color: #c084fc;
          background: rgba(192, 132, 252, 0.1);
          border: 1px solid rgba(192, 132, 252, 0.25);
        }

        .stat-icon.green {
          color: #34d399;
          background: rgba(52, 211, 153, 0.1);
          border: 1px solid rgba(52, 211, 153, 0.25);
        }

        .stat-icon.blue {
          color: #38bdf8;
          background: rgba(56, 189, 248, 0.1);
          border: 1px solid rgba(56, 189, 248, 0.25);
        }

        .stat-icon.red {
          color: #f87171;
          background: rgba(248, 113, 113, 0.1);
          border: 1px solid rgba(248, 113, 113, 0.25);
        }

        .stat-details {
          display: flex;
          flex-direction: column;
          gap: 0.15rem;
          min-width: 0;
        }

        .stat-label {
          font-size: 0.78rem;
          color: var(--text-muted);
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.04em;
        }

        .stat-value {
          font-size: 1.6rem;
          font-weight: 700;
          color: var(--text-primary);
          line-height: 1.2;
        }

        .stat-details small {
          color: var(--text-muted);
          font-size: 0.72rem;
          margin-top: 0.2rem;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }

        .dashboard-finance-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
          gap: 1rem;
          margin-bottom: 1.25rem;
        }

        .dashboard-finance {
          padding: 1.25rem 1.5rem;
          border-radius: var(--radius-md);
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 1rem;
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid rgba(255, 255, 255, 0.07);
          transition: all 0.2s ease;
        }

        .income-card {
          border-left: 4px solid var(--success);
        }

        .premium-revenue-card {
          border-left: 4px solid var(--accent-purple);
        }

        .premium-revenue-card:hover {
          border-left-color: #c084fc;
        }

        .finance-header {
          display: flex;
          flex-direction: column;
          gap: 0.2rem;
        }

        .finance-header span {
          font-size: 0.88rem;
          font-weight: 600;
          color: var(--text-secondary);
        }

        .finance-header small {
          font-size: 0.75rem;
          color: var(--text-muted);
        }

        .dashboard-finance strong {
          font-size: 1.5rem;
          color: var(--success);
          font-weight: 700;
          white-space: nowrap;
        }

        .dashboard-finance strong.premium-revenue-text {
          color: #c084fc;
        }

        .dashboard-finance-link {
          width: 100%;
          color: inherit;
          text-align: left;
          cursor: pointer;
          font: inherit;
        }

        .dashboard-finance-link:hover {
          transform: translateY(-2px);
          box-shadow: 0 4px 16px rgba(0, 0, 0, 0.25);
        }

        .dashboard-activity-grid {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 1.25rem;
        }

        .activity-panel {
          padding: 1.25rem;
          border-radius: var(--radius-md);
          background: rgba(255, 255, 255, 0.025);
          border: 1px solid rgba(255, 255, 255, 0.06);
          display: flex;
          flex-direction: column;
        }

        .activity-panel-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          margin-bottom: 0.85rem;
          padding-bottom: 0.5rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
        }

        .activity-panel-header h4 {
          margin: 0;
          font-size: 0.95rem;
          font-weight: 700;
          color: var(--text-primary);
        }

        .activity-view-all-btn {
          border: none;
          background: transparent;
          color: var(--accent-blue);
          font-size: 0.78rem;
          font-weight: 600;
          cursor: pointer;
          padding: 0.2rem 0.4rem;
          transition: all 0.15s ease;
        }

        .activity-view-all-btn:hover {
          text-decoration: underline;
        }

        .activity-list-container {
          display: flex;
          flex-direction: column;
          gap: 0.45rem;
          max-height: 320px;
          overflow-y: auto;
          padding-right: 4px;
        }

        .empty-hint {
          color: var(--text-muted);
          font-size: 0.85rem;
          text-align: center;
          padding: 2rem 1rem;
        }

        .activity-row {
          display: flex;
          justify-content: space-between;
          align-items: center;
          gap: 1rem;
          padding: 0.65rem 0.85rem;
          border-radius: var(--radius-sm);
          background: rgba(255, 255, 255, 0.02);
          border: 1px solid rgba(255, 255, 255, 0.04);
          transition: all 0.15s ease;
        }

        .activity-row div {
          min-width: 0;
          display: flex;
          flex-direction: column;
          gap: 0.15rem;
        }

        .activity-row strong {
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          font-size: 0.85rem;
          color: var(--text-primary);
        }

        .activity-row small {
          color: var(--text-muted);
          font-size: 0.75rem;
        }

        .amount-badge {
          font-size: 0.85rem;
          font-weight: 700;
          color: var(--success);
          flex-shrink: 0;
        }

        .report-status-tag {
          font-size: 0.72rem;
          font-weight: 700;
          padding: 0.2rem 0.5rem;
          border-radius: 4px;
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-secondary);
          flex-shrink: 0;
        }

        .report-status-tag.pending {
          background: rgba(245, 158, 11, 0.15);
          color: #fbbf24;
          border: 1px solid rgba(245, 158, 11, 0.3);
        }

        .activity-link {
          width: 100%;
          border: 1px solid rgba(255, 255, 255, 0.04);
          background: rgba(255, 255, 255, 0.02);
          color: inherit;
          text-align: left;
          cursor: pointer;
          font: inherit;
        }

        .activity-link:hover,
        .activity-link:focus-visible {
          background: rgba(255, 255, 255, 0.06);
          border-color: rgba(255, 255, 255, 0.12);
          transform: translateX(2px);
          outline: none;
        }

        .modal-overlay {
          position: fixed;
          inset: 0;
          z-index: 2000;
          display: grid;
          place-items: center;
          padding: 1rem;
          background: rgba(3, 7, 18, 0.78);
          backdrop-filter: blur(8px);
        }

        .modal-box {
          width: min(560px, calc(100vw - 2rem));
          max-height: calc(100vh - 2rem);
          overflow: auto;
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .admin-user-modal {
          position: relative;
        }

        .transaction-action-modal {
          position: relative;
        }

        .transaction-action-summary {
          display: grid;
          grid-template-columns: repeat(2, minmax(0, 1fr));
          gap: 0.65rem;
          padding: 1rem;
          border: 1px solid rgba(255, 255, 255, 0.08);
          border-radius: 12px;
          background: rgba(255, 255, 255, 0.035);
        }

        .transaction-action-summary > div {
          display: flex;
          flex-direction: column;
          gap: 0.2rem;
          min-width: 0;
        }

        .transaction-action-summary span {
          color: var(--text-muted);
          font-size: 0.75rem;
        }

        .transaction-action-summary strong {
          overflow-wrap: anywhere;
        }

        .transaction-action-amount {
          color: var(--success);
          font-size: 1.05rem;
        }

        .transaction-impact {
          margin: 1rem 0;
          padding: 0.8rem 0.9rem;
          border: 1px solid;
          border-radius: 10px;
          font-size: 0.88rem;
          line-height: 1.5;
        }

        .transaction-impact.safe {
          color: #86efac;
          border-color: rgba(34, 197, 94, 0.3);
          background: rgba(34, 197, 94, 0.09);
        }

        .transaction-impact.warning {
          color: #fca5a5;
          border-color: rgba(239, 68, 68, 0.3);
          background: rgba(239, 68, 68, 0.09);
        }

        .transaction-action-modal textarea.input-control {
          resize: vertical;
          min-height: 100px;
        }

        .transaction-action-modal .transaction-modal-actions {
          display: flex;
          justify-content: flex-end;
          align-items: center;
          gap: 0.75rem;
          padding-top: 0.25rem;
        }

        .transaction-action-modal .transaction-modal-actions button {
          min-width: 132px;
          min-height: 44px;
          justify-content: center;
        }

        .transaction-action-modal .btn-danger {
          display: inline-flex;
          align-items: center;
          justify-content: center;
          gap: 0.5rem;
          padding: 0.75rem 1.5rem;
          border: 1px solid rgba(248, 113, 113, 0.7);
          border-radius: var(--radius-sm);
          background: linear-gradient(135deg, #dc2626, #ef4444);
          color: #fff;
          font-weight: 700;
          cursor: pointer;
          box-shadow: 0 8px 22px rgba(220, 38, 38, 0.25);
          transition: var(--transition-normal);
        }

        .transaction-action-modal .btn-danger:hover:not(:disabled) {
          transform: translateY(-1px);
          border-color: #fca5a5;
          box-shadow: 0 10px 28px rgba(239, 68, 68, 0.4);
          filter: brightness(1.06);
        }

        .transaction-action-modal .btn-danger:active:not(:disabled) {
          transform: translateY(0);
          box-shadow: 0 4px 14px rgba(220, 38, 38, 0.28);
        }

        .transaction-action-modal .btn-danger:focus-visible {
          outline: 2px solid #fca5a5;
          outline-offset: 3px;
        }

        .transaction-action-modal .transaction-modal-actions button:disabled {
          cursor: not-allowed;
          transform: none;
          box-shadow: none;
          opacity: 0.58;
        }

        .transaction-action-modal .btn-danger:disabled {
          border-color: rgba(248, 113, 113, 0.3);
          background: rgba(239, 68, 68, 0.13);
          color: #fca5a5;
        }

        .modal-title-row {
          display: flex;
          align-items: flex-start;
          justify-content: space-between;
          gap: 1rem;
          padding-bottom: 1rem;
          margin-bottom: 1rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.08);
        }

        .modal-title-row h3 {
          margin: 0 0 0.3rem;
          padding: 0;
          border: 0;
        }

        .modal-title-row p {
          color: var(--text-muted);
          font-size: 0.85rem;
          overflow-wrap: anywhere;
        }

        .modal-close-button {
          display: grid;
          place-items: center;
          flex: 0 0 auto;
          border: 0;
          background: transparent;
          color: var(--text-secondary);
          cursor: pointer;
          padding: 0.25rem;
        }

        @media (max-width: 520px) {
          .transaction-action-summary {
            grid-template-columns: 1fr;
          }

          .transaction-action-modal .transaction-modal-actions {
            display: grid;
            grid-template-columns: 1fr 1fr;
          }

          .transaction-action-modal .transaction-modal-actions button {
            width: 100%;
            min-width: 0;
          }
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
          border-bottom: 1px solid rgba(255, 255, 255, 0.08);
          background: rgba(255, 255, 255, 0.02);
        }

        .admin-table td {
          padding: 0.85rem 1rem;
          font-size: 0.85rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.04);
          color: var(--text-primary);
        }

        .admin-table tbody tr:hover td {
          background: rgba(255, 255, 255, 0.02);
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

        .role-badge,
        .tx-type-badge {
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

        .status-badge,
        .tx-status-badge {
          font-size: 0.75rem;
          font-weight: 600;
        }

        .status-badge.ACTIVE,
        .tx-status-badge.SUCCESS {
          color: var(--success);
        }

        .status-badge.SUSPENDED,
        .tx-status-badge.CANCELLED {
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

        .report-card:hover,
        .report-card:focus-visible {
          border-color: var(--accent-blue);
          transform: translateY(-1px);
          outline: none;
        }

        .report-open-hint {
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          color: var(--accent-blue);
          font-size: 0.78rem;
        }

        .report-preview-modal {
          width: min(850px, calc(100vw - 2rem));
          max-height: calc(100vh - 2rem);
          overflow: auto;
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .report-preview-grid {
          display: grid;
          grid-template-columns: 140px 1fr;
          gap: 0.75rem;
          margin: 1rem 0;
        }

        .report-preview-grid > span {
          color: var(--text-muted);
        }

        .report-description-full,
        .document-content-preview {
          padding: 1rem;
          border-radius: var(--radius-sm);
          background: rgba(255, 255, 255, 0.04);
        }

        .report-description-full p,
        .document-content-preview p {
          margin-top: 0.5rem;
          color: var(--text-secondary);
          white-space: pre-wrap;
          line-height: 1.55;
        }

        .reported-document-section {
          margin-top: 1rem;
          padding-top: 1rem;
          border-top: 1px solid rgba(255, 255, 255, 0.08);
        }

        .reported-document-heading {
          display: flex;
          align-items: center;
          gap: 0.75rem;
        }

        .reported-document-heading svg {
          color: var(--accent-blue);
          flex-shrink: 0;
        }

        .reported-file-icon {
          width: 42px;
          height: 42px;
          border-radius: 10px;
          display: grid;
          place-items: center;
          flex: 0 0 auto;
        }

        .reported-document-heading p {
          color: var(--text-muted);
          font-size: 0.85rem;
        }

        .document-metrics {
          display: flex;
          flex-wrap: wrap;
          gap: 0.5rem;
          margin: 1rem 0;
        }

        .document-metrics span {
          padding: 0.35rem 0.65rem;
          border-radius: 999px;
          background: rgba(255, 255, 255, 0.05);
          color: var(--text-secondary);
          font-size: 0.78rem;
        }

        .document-content-preview {
          max-height: 260px;
          overflow: auto;
        }

        .report-preview-loading {
          min-height: 180px;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 0.6rem;
          color: var(--text-muted);
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
          to {
            transform: rotate(360deg);
          }
        }

        /* AI Observability Styles */
        .ai-observability-pane {
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .section-subtitle {
          color: var(--text-muted);
          font-size: 0.85rem;
          margin-top: 0.2rem;
        }

        .stat-icon.yellow {
          color: #fbbf24;
          background: rgba(251, 191, 36, 0.1);
          border: 1px solid rgba(251, 191, 36, 0.25);
        }

        .ai-breakdown-grid {
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(420px, 1fr));
          gap: 1.25rem;
        }

        .ai-breakdown-card {
          padding: 1.25rem 1.5rem;
          display: flex;
          flex-direction: column;
          gap: 0.85rem;
        }

        .ai-breakdown-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          border-bottom: 1px solid rgba(255, 255, 255, 0.06);
          padding-bottom: 0.75rem;
        }

        .ai-breakdown-title {
          display: flex;
          align-items: center;
          gap: 0.6rem;
        }

        .ai-breakdown-title h4 {
          font-size: 0.95rem;
          font-weight: 700;
          margin: 0;
          color: var(--text-primary);
        }

        .ai-breakdown-icon.blue {
          color: #38bdf8;
        }

        .ai-breakdown-icon.yellow {
          color: #fbbf24;
        }

        .mini-table th {
          padding: 0.55rem 0.75rem;
          font-size: 0.75rem;
        }

        .mini-table td {
          padding: 0.65rem 0.75rem;
          font-size: 0.82rem;
        }

        .model-name {
          color: #38bdf8;
          font-family: monospace;
          font-size: 0.85rem;
        }

        .ai-logs-section {
          padding: 1.25rem 1.5rem;
        }

        .ai-logs-header h4 {
          font-size: 1rem;
          font-weight: 700;
          margin: 0 0 0.15rem;
          color: var(--text-primary);
        }

        .ai-logs-header p {
          font-size: 0.8rem;
          margin: 0 0 1rem;
        }

        .latency-tag {
          font-size: 0.76rem;
          font-weight: 700;
          padding: 0.15rem 0.45rem;
          border-radius: 4px;
          font-family: monospace;
        }

        .latency-tag.good {
          background: rgba(52, 211, 153, 0.15);
          color: #34d399;
          border: 1px solid rgba(52, 211, 153, 0.25);
        }

        .latency-tag.medium {
          background: rgba(251, 191, 36, 0.15);
          color: #fbbf24;
          border: 1px solid rgba(251, 191, 36, 0.25);
        }

        .latency-tag.high {
          background: rgba(239, 68, 68, 0.15);
          color: #ef4444;
          border: 1px solid rgba(239, 68, 68, 0.25);
        }

        .cost-tag {
          font-family: monospace;
          font-size: 0.8rem;
          color: #34d399;
          font-weight: 600;
        }

        @media (max-width: 900px) {
          .dashboard-activity-grid,
          .ai-charts-grid,
          .community-stats-grid {
            grid-template-columns: 1fr;
          }
        }

        @media (max-width: 768px) {
          .admin-toolbar {
            flex-direction: column;
            align-items: stretch;
          }
          .search-filters {
            justify-content: stretch;
          }
          .search-input {
            width: 100%;
          }
          .report-preview-grid {
            grid-template-columns: 1fr;
            gap: 0.35rem;
          }
          .report-preview-grid > strong {
            margin-bottom: 0.5rem;
          }
          .admin-layout-grid {
            grid-template-columns: 1fr;
            height: auto;
          }
        }
      `}</style>
    </div>
  );
};
