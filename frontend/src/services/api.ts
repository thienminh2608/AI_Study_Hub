export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface UserShare {
  shareId: number;
  userId: number;
  username: string;
  email: string;
  role: 'VIEWER' | 'EDITOR';
  createdAt: string;
}

export interface ShareLinkInfo {
  token?: string;
  shareUrl?: string;
  expiresAt?: string;
  isRevoked: boolean;
  hasExpiration: boolean;
  isExpired: boolean;
}

export interface ItemAccessSettings {
  itemId: number;
  itemType: 'DOCUMENT' | 'FOLDER';
  title: string;
  ownerUserId: number;
  ownerName: string;
  generalAccess: 'RESTRICTED' | 'LINK' | 'PUBLIC';
  isInherited: boolean;
  parentFolderId?: number;
  shares: UserShare[];
  shareLink?: ShareLinkInfo;
  userEffectiveRole: 'OWNER' | 'EDITOR' | 'VIEWER' | 'NONE';
}

export interface TrashItem {
  itemId: number;
  itemType: 'DOCUMENT' | 'FOLDER';
  name: string;
  fileExtension?: string;
  fileSizeMb?: number;
  deletedAt: string;
  deletedByUserId: number;
  deletedByName: string;
}

export interface DocumentVersion {
  versionId: number;
  documentId: number;
  versionNumber: number;
  cloudStorageUrl: string;
  fileExtension: string;
  fileSizeMb: number;
  changeSummary?: string;
  createdByUserId: number;
  createdByName: string;
  createdAt: string;
  isCurrent: boolean;
}

export interface StorageQuota {
  userId: number;
  tierName: string;
  usedStorageMb: number;
  maxStorageMb: number;
  usagePercentage: number;
  isQuotaExceeded: boolean;
  aiPromptsToday: number;
  aiPromptLimitPerDay: number;
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5065/api';

const getAccessToken = () => localStorage.getItem('token') || sessionStorage.getItem('token');
let refreshPromise: Promise<string | null> | null = null;

const renewAccessToken = async (): Promise<string | null> => {
  const refreshToken = localStorage.getItem('refreshToken');
  if (!refreshToken) return null;
  if (!refreshPromise) {
    refreshPromise = fetch(`${API_BASE_URL}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
      .then(async (response) => {
        if (!response.ok) return null;
        const data = await response.json();
        localStorage.setItem('token', data.token);
        if (data.refreshToken) localStorage.setItem('refreshToken', data.refreshToken);
        return data.token as string;
      })
      .catch(() => null)
      .finally(() => {
        refreshPromise = null;
      });
  }
  return refreshPromise;
};

async function request<T>(path: string, options: RequestInit = {}, retried = false): Promise<T> {
  const token = getAccessToken();
  const headers = new Headers(options.headers || {});

  // Add authorization header if token exists
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  // Set default Content-Type to JSON unless we are uploading FormData (multipart/form-data)
  if (!(options.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  let response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  });

  if (response.status === 401 && !retried && path !== '/auth/refresh') {
    const renewedToken = await renewAccessToken();
    if (renewedToken) {
      headers.set('Authorization', `Bearer ${renewedToken}`);
      response = await fetch(`${API_BASE_URL}${path}`, { ...options, headers });
    }
  }

  if (!response.ok) {
    let errorMessage = 'Đã xảy ra lỗi kết nối.';
    try {
      const errorJson = await response.json();
      errorMessage = errorJson.message || errorMessage;
    } catch {
      // Ignored
    }

    if (response.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('refreshToken');
      sessionStorage.removeItem('token');
      window.dispatchEvent(new Event('auth-status-changed'));
    }

    throw new Error(errorMessage);
  }

  // Handle empty or 204 No Content responses
  const text = await response.text();
  return text ? (JSON.parse(text) as T) : ({} as T);
}

async function download(path: string): Promise<Blob> {
  let token = getAccessToken();
  let response = await fetch(`${API_BASE_URL}${path}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (response.status === 401) {
    token = await renewAccessToken();
    if (token)
      response = await fetch(`${API_BASE_URL}${path}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
  }
  if (!response.ok) throw new Error('Không thể tải file.');
  return response.blob();
}

export const api = {
  // Authentication
  auth: {
    login: (dto: any) => request<any>('/auth/login', { method: 'POST', body: JSON.stringify(dto) }),
    register: (dto: any) =>
      request<any>('/auth/register', { method: 'POST', body: JSON.stringify(dto) }),
    forgotPassword: (email: string) =>
      request<any>('/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email }) }),
    verifyOtp: (dto: any) =>
      request<any>('/auth/verify-otp', { method: 'POST', body: JSON.stringify(dto) }),
    resetPassword: (dto: any) =>
      request<any>('/auth/reset-password', { method: 'POST', body: JSON.stringify(dto) }),
    getMe: () => request<any>('/auth/me', { method: 'GET' }),
    updateUsername: (username: string) =>
      request<any>('/auth/profile/username', { method: 'PUT', body: JSON.stringify({ username }) }),
    toggleAutoRenew: () =>
      request<any>('/auth/subscription/toggle-autorenew', { method: 'POST' }),
  },

  // Documents
  document: {
    upload: (file: File, folderId?: number) => {
      const formData = new FormData();
      formData.append('file', file);
      const url = folderId ? `/document/upload?folderId=${folderId}` : '/document/upload';
      return request<any>(url, { method: 'POST', body: formData });
    },
    confirm: (
      documentId: number,
      title: string,
      subject: string,
      sharingPermission: string,
      folderId?: number,
    ) => {
      const folderParam = folderId ? `&folderId=${folderId}` : '';
      return request<any>(
        `/document/confirm?documentId=${documentId}&title=${encodeURIComponent(title)}&subject=${encodeURIComponent(subject)}&sharingPermission=${sharingPermission}${folderParam}`,
        { method: 'POST' },
      );
    },
    replace: (
      pendingDocId: number,
      duplicateDocId: number,
      title: string,
      subject: string,
      sharingPermission: string,
      folderId?: number,
    ) => {
      const folderParam = folderId ? `&folderId=${folderId}` : '';
      return request<any>(
        `/document/replace?pendingDocId=${pendingDocId}&duplicateDocId=${duplicateDocId}&title=${encodeURIComponent(title)}&subject=${encodeURIComponent(subject)}&sharingPermission=${sharingPermission}${folderParam}`,
        { method: 'POST' },
      );
    },
    keepBoth: (
      pendingDocId: number,
      title: string,
      subject: string,
      sharingPermission: string,
      folderId?: number,
    ) => {
      const folderParam = folderId ? `&folderId=${folderId}` : '';
      return request<any>(
        `/document/keep-both?pendingDocId=${pendingDocId}&title=${encodeURIComponent(title)}&subject=${encodeURIComponent(subject)}&sharingPermission=${sharingPermission}${folderParam}`,
        { method: 'POST' },
      );
    },
    cancel: (pendingDocId: number) =>
      request<any>(`/document/cancel?pendingDocId=${pendingDocId}`, { method: 'POST' }),
    getUserDocuments: (folderId?: number) => {
      const url = folderId ? `/document?folderId=${folderId}` : '/document';
      return request<any[]>(url, { method: 'GET' });
    },
    getPublicDocuments: () => request<any[]>('/document/public', { method: 'GET' }),
    getSharedWithMe: () => request<any[]>('/document/shared-with-me', { method: 'GET' }),
    getShares: (id: number) => request<any[]>(`/document/${id}/shares`, { method: 'GET' }),
    shareWithFriend: (id: number, friendUserId: number) =>
      request<any>(`/document/${id}/shares/${friendUserId}`, { method: 'POST' }),
    removeShare: (id: number, friendUserId: number) =>
      request<any>(`/document/${id}/shares/${friendUserId}`, { method: 'DELETE' }),
    getAnalytics: () => request<any>('/document/analytics', { method: 'GET' }),
    getAudience: (id: number) => request<any>(`/document/${id}/audience`, { method: 'GET' }),
    getById: (id: number) => request<any>(`/document/${id}`, { method: 'GET' }),
    delete: (id: number) => request<any>(`/document/${id}`, { method: 'DELETE' }),
    getText: (id: number) => request<any>(`/document/${id}/text`, { method: 'GET' }),
    preview: (id: number) => `${API_BASE_URL}/document/${id}/preview`,
    download: (id: number) => download(`/document/${id}/download`),
    report: (dto: any) =>
      request<any>('/document/report', { method: 'POST', body: JSON.stringify(dto) }),
    appeal: (reportId: number, dto: any) =>
      request<any>(`/document/reports/${reportId}/appeal`, {
        method: 'POST',
        body: JSON.stringify(dto),
      }),
    getAppealableReport: (documentId: number) =>
      request<{ reportId?: number }>(`/document/${documentId}/appealable-report`, {
        method: 'GET',
      }),
    getReportReasons: () => request<any[]>('/document/report-reasons', { method: 'GET' }),
    getModerationNotices: (unreadOnly = false) =>
      request<any[]>(`/document/moderation-notices?unreadOnly=${unreadOnly}`, { method: 'GET' }),
    readModerationNotice: (noticeId: number) =>
      request<any>(`/document/moderation-notices/${noticeId}/read`, { method: 'POST' }),
    readAllModerationNotices: () =>
      request<any>('/document/moderation-notices/read-all', { method: 'POST' }),
    deleteModerationNotice: (noticeId: number) =>
      request<any>(`/document/moderation-notices/${noticeId}`, { method: 'DELETE' }),
    getBookmarks: () => request<number[]>('/document/bookmarks', { method: 'GET' }),
    addBookmark: (id: number) => request<any>(`/document/${id}/bookmark`, { method: 'POST' }),
    removeBookmark: (id: number) => request<any>(`/document/${id}/bookmark`, { method: 'DELETE' }),
  },

  moderation: {
    getSummary: () => request<any>('/moderation/summary', { method: 'GET' }),
    getQueue: () => request<any[]>('/moderation/queue', { method: 'GET' }),
    getDocument: (id: number) => request<any>(`/moderation/documents/${id}`, { method: 'GET' }),
    reviewDocument: (id: number, action: string, note = '') =>
      request<any>(`/moderation/documents/${id}/${action}`, {
        method: 'POST',
        body: JSON.stringify({ note }),
      }),
    getReports: () => request<any[]>('/moderation/reports', { method: 'GET' }),
    assignReport: (id: number) =>
      request<any>(`/moderation/reports/${id}/assign`, { method: 'POST' }),
    resolveReport: (id: number, action: string, note = '') =>
      request<any>(`/moderation/reports/${id}/${action}`, {
        method: 'POST',
        body: JSON.stringify({ note }),
      }),
    getAppeals: () => request<any[]>('/moderation/appeals', { method: 'GET' }),
    resolveAppeal: (id: number, action: string, note = '') =>
      request<any>(`/moderation/appeals/${id}/${action}`, {
        method: 'POST',
        body: JSON.stringify({ note }),
      }),
    getHistory: () => request<any[]>('/moderation/history', { method: 'GET' }),
  },

  // Folders
  folder: {
    getChildFolders: (parentFolderId?: number) => {
      const url = parentFolderId ? `/folder?parentFolderId=${parentFolderId}` : '/folder';
      return request<any[]>(url, { method: 'GET' });
    },
    getAllFolders: () => request<any[]>('/folder/all', { method: 'GET' }),
    getById: (id: number) => request<any>(`/folder/${id}`, { method: 'GET' }),
    create: (dto: any) => request<any>('/folder', { method: 'POST', body: JSON.stringify(dto) }),
    update: (id: number, dto: any) =>
      request<any>(`/folder/${id}`, { method: 'PUT', body: JSON.stringify(dto) }),
    delete: (id: number) => request<any>(`/folder/${id}`, { method: 'DELETE' }),
  },

  // Chat
  chat: {
    getSessions: () => request<any[]>('/chat/sessions', { method: 'GET' }),
    createSession: (dto: any) =>
      request<any>('/chat/sessions', { method: 'POST', body: JSON.stringify(dto) }),
    pinSession: (sessionId: number, pin: boolean) =>
      request<any>(`/chat/sessions/${sessionId}/pin?pin=${pin}`, { method: 'POST' }),
    deleteSession: (sessionId: number) =>
      request<any>(`/chat/sessions/${sessionId}`, { method: 'DELETE' }),
    setDocument: (sessionId: number, documentId: number | null) =>
      request<any>(`/chat/sessions/${sessionId}/document`, {
        method: 'PUT',
        body: JSON.stringify({ documentId }),
      }),
    getMessages: (sessionId: number) =>
      request<any[]>(`/chat/sessions/${sessionId}/messages`, { method: 'GET' }),
    askQuestion: (sessionId: number, dto: any) =>
      request<any>(`/chat/sessions/${sessionId}/ask`, {
        method: 'POST',
        body: JSON.stringify(dto),
      }),
  },

  // Friendship
  friendship: {
    sendRequest: (addresseeId: number) =>
      request<any>('/friendship/request', {
        method: 'POST',
        body: JSON.stringify({ addresseeId }),
      }),
    respond: (targetUserId: number, status: string) =>
      request<any>(`/friendship/respond?targetUserId=${targetUserId}&status=${status}`, {
        method: 'POST',
      }),
    delete: (targetUserId: number) =>
      request<any>(`/friendship?targetUserId=${targetUserId}`, { method: 'DELETE' }),
    getFriends: () => request<any[]>('/friendship/friends', { method: 'GET' }),
    getPending: () => request<any[]>('/friendship/pending', { method: 'GET' }),
    getBlocked: () => request<any[]>('/friendship/blocked', { method: 'GET' }),
    find: (email: string) =>
      request<any>(`/friendship/find?email=${encodeURIComponent(email)}`, { method: 'GET' }),
  },

  // Transactions
  transaction: {
    create: (dto: any) =>
      request<any>('/transaction', { method: 'POST', body: JSON.stringify(dto) }),
    getUserTransactions: () => request<any[]>('/transaction', { method: 'GET' }),
    buyPremium: () => request<any>('/transaction/buy-premium', { method: 'POST' }),
    getTiers: () => request<any[]>('/transaction/tiers', { method: 'GET' }),
    getTransferConfig: () => request<any>('/transaction/transfer-config', { method: 'GET' }),
    getInvoice: (transactionId: number) => request<any>(`/transaction/${transactionId}/invoice`, { method: 'GET' }),
  },

  admin: {
    getDashboard: (startDate?: string, endDate?: string) => {
      const params = new URLSearchParams();
      if (startDate) params.set('startDate', startDate);
      if (endDate) params.set('endDate', endDate);
      const query = params.toString();
      return request<any>(`/admin/dashboard${query ? `?${query}` : ''}`, { method: 'GET' });
    },
    getUsers: (page = 1, pageSize = 8, search = '', status = '') => {
      const params = new URLSearchParams();
      params.set('pageNumber', String(page));
      params.set('pageSize', String(pageSize));
      if (search) params.set('search', search);
      if (status) params.set('status', status);
      return request<any>(`/admin/users?${params.toString()}`, { method: 'GET' });
    },
    createUser: (dto: any, role: string, tierType: string) =>
      request<any>(`/admin/users?role=${role}&tierType=${tierType}`, {
        method: 'POST',
        body: JSON.stringify(dto),
      }),
    updateUser: (userId: number, dto: any) =>
      request<any>(`/admin/users/${userId}`, { method: 'PUT', body: JSON.stringify(dto) }),
    deleteUser: (userId: number) => request<any>(`/admin/users/${userId}`, { method: 'DELETE' }),
    getTransactions: (page = 1, pageSize = 8, search = '', status = '', type = '') => {
      const params = new URLSearchParams();
      params.set('pageNumber', String(page));
      params.set('pageSize', String(pageSize));
      if (search) params.set('search', search);
      if (status) params.set('status', status);
      if (type) params.set('type', type);
      return request<any>(`/admin/transactions?${params.toString()}`, { method: 'GET' });
    },
    updateTransaction: (transactionId: number, status: string, failureReason?: string) =>
      request<any>(`/admin/transactions/${transactionId}`, {
        method: 'PUT',
        body: JSON.stringify({ status, failureReason }),
      }),
    refundTransaction: (transactionId: number, reason: string) =>
      request<any>(`/admin/transactions/${transactionId}/refund`, {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }),
    getReports: (page = 1, pageSize = 8, search = '', status = '') => {
      const params = new URLSearchParams();
      params.set('pageNumber', String(page));
      params.set('pageSize', String(pageSize));
      if (search) params.set('search', search);
      if (status) params.set('status', status);
      return request<any>(`/admin/reports?${params.toString()}`, { method: 'GET' });
    },
    resolveReport: (reportId: number, action: string) =>
      request<any>(`/admin/reports/${reportId}/resolve?action=${action}`, { method: 'POST' }),
    getDocuments: (page = 1, pageSize = 8, query = '', status = '') => {
      const params = new URLSearchParams();
      params.set('pageNumber', String(page));
      params.set('pageSize', String(pageSize));
      if (query) params.set('query', query);
      if (status) params.set('status', status);
      return request<any>(`/admin/documents?${params.toString()}`, { method: 'GET' });
    },
    getDocumentDetail: (documentId: number) =>
      request<any>(`/admin/documents/${documentId}/detail`, { method: 'GET' }),
    updateDocumentVisibility: (documentId: number, sharingPermission: string) =>
      request<any>(`/admin/documents/${documentId}/visibility`, {
        method: 'PUT',
        body: JSON.stringify({ sharingPermission }),
      }),
    deleteDocument: (documentId: number) =>
      request<any>(`/admin/documents/${documentId}`, { method: 'DELETE' }),
    getReportReasons: () => request<any[]>('/admin/report-reasons', { method: 'GET' }),
    createReportReason: (dto: any) =>
      request<any>('/admin/report-reasons', { method: 'POST', body: JSON.stringify(dto) }),
    updateReportReason: (reasonCode: string, dto: any) =>
      request<any>(`/admin/report-reasons/${encodeURIComponent(reasonCode)}`, {
        method: 'PUT',
        body: JSON.stringify(dto),
      }),
    deleteReportReason: (reasonCode: string) =>
      request<any>(`/admin/report-reasons/${encodeURIComponent(reasonCode)}`, { method: 'DELETE' }),
    getSubscriptions: () => request<any[]>('/admin/subscriptions', { method: 'GET' }),
    updateSubscription: (tierId: number, dto: any) =>
      request<any>(`/admin/subscriptions/${tierId}`, { method: 'PUT', body: JSON.stringify(dto) }),
    getTransferConfig: () => request<any>('/admin/transfer-config', { method: 'GET' }),
    updateTransferConfig: (dto: any) =>
      request<any>('/admin/transfer-config', { method: 'PUT', body: JSON.stringify(dto) }),
    getAuditLogs: (page = 1, pageSize = 20) =>
      request<any>(`/admin/audit-logs?page=${page}&pageSize=${pageSize}`, { method: 'GET' }),
  },
  access: {
    getAccessSettings: (type: 'document' | 'folder', id: number) =>
      request<any>(`/access/${type}/${id}`, { method: 'GET' }),
    updateGeneralAccess: (type: 'document' | 'folder', id: number, generalAccess: string) =>
      request<any>(`/access/${type}/${id}/general`, {
        method: 'PUT',
        body: JSON.stringify({ generalAccess }),
      }),
    addUserShare: (type: 'document' | 'folder', id: number, email: string, role: string) =>
      request<any>(`/access/${type}/${id}/share`, {
        method: 'POST',
        body: JSON.stringify({ email, role }),
      }),
    removeUserShare: (type: 'document' | 'folder', id: number, targetUserId: number) =>
      request<any>(`/access/${type}/${id}/share/${targetUserId}`, { method: 'DELETE' }),
    rotateShareLink: (id: number) =>
      request<any>(`/access/document/${id}/link/rotate`, { method: 'POST' }),
    revokeShareLink: (id: number) =>
      request<any>(`/access/document/${id}/link/revoke`, { method: 'POST' }),
  },
  trash: {
    getTrashItems: (page = 1, pageSize = 12) =>
      request<any>(`/trash?page=${page}&pageSize=${pageSize}`, { method: 'GET' }),
    moveDocumentToTrash: (id: number) => request<any>(`/trash/document/${id}`, { method: 'POST' }),
    moveFolderToTrash: (id: number) => request<any>(`/trash/folder/${id}`, { method: 'POST' }),
    restoreDocument: (id: number) =>
      request<any>(`/trash/restore/document/${id}`, { method: 'POST' }),
    restoreFolder: (id: number) =>
      request<any>(`/trash/restore/folder/${id}`, { method: 'POST' }),
    permanentDeleteDocument: (id: number) =>
      request<any>(`/trash/permanent/document/${id}`, { method: 'DELETE' }),
    permanentDeleteFolder: (id: number) =>
      request<any>(`/trash/permanent/folder/${id}`, { method: 'DELETE' }),
    emptyTrash: () => request<any>('/trash/empty', { method: 'POST' }),
  },
  versions: {
    getVersionHistory: (documentId: number) =>
      request<any[]>(`/documents/${documentId}/versions`, { method: 'GET' }),
    uploadNewVersion: (documentId: number, file: File, changeSummary?: string) => {
      const formData = new FormData();
      formData.append('file', file);
      if (changeSummary) formData.append('changeSummary', changeSummary);
      return request<any>(`/documents/${documentId}/versions`, {
        method: 'POST',
        body: formData,
      });
    },
    restoreVersion: (documentId: number, versionId: number) =>
      request<any>(`/documents/${documentId}/versions/${versionId}/restore`, { method: 'POST' }),
  },
  documentExtra: {
    getStorageQuota: () => request<any>('/document/storage-quota', { method: 'GET' }),
    retryExtraction: (documentId: number) =>
      request<any>(`/document/${documentId}/retry-extraction`, { method: 'POST' }),
    getMyDocumentsPaged: (
      folderId?: number | null,
      page = 1,
      pageSize = 12,
      search = '',
      subject = ''
    ) => {
      let url = `/document/my-documents/paged?page=${page}&pageSize=${pageSize}`;
      if (folderId) url += `&folderId=${folderId}`;
      if (search) url += `&search=${encodeURIComponent(search)}`;
      if (subject) url += `&subject=${encodeURIComponent(subject)}`;
      return request<any>(url, { method: 'GET' });
    },
    getSharedWithMePaged: (page = 1, pageSize = 12) =>
      request<any>(`/document/shared-with-me/paged?page=${page}&pageSize=${pageSize}`, {
        method: 'GET',
      }),
    getBookmarksPaged: (page = 1, pageSize = 12) =>
      request<any>(`/document/bookmarks/paged?page=${page}&pageSize=${pageSize}`, {
        method: 'GET',
      }),
    bulkDelete: (documentIds: number[]) =>
      request<any>('/document/bulk-delete', {
        method: 'POST',
        body: JSON.stringify(documentIds),
      }),
    bulkMove: (documentIds: number[], targetFolderId?: number | null) =>
      request<any>('/document/bulk-move', {
        method: 'POST',
        body: JSON.stringify({ documentIds, targetFolderId }),
      }),
  },
  friendshipExtra: {
    getFriendsPaged: (page = 1, pageSize = 10) =>
      request<any>(`/friendship/friends/paged?page=${page}&pageSize=${pageSize}`, {
        method: 'GET',
      }),
    getPendingRequestsPaged: (page = 1, pageSize = 10) =>
      request<any>(`/friendship/pending/paged?page=${page}&pageSize=${pageSize}`, {
        method: 'GET',
      }),
    getBlockedUsersPaged: (page = 1, pageSize = 10) =>
      request<any>(`/friendship/blocked/paged?page=${page}&pageSize=${pageSize}`, {
        method: 'GET',
      }),
  },
};
