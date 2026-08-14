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
  },

  // Admin
  admin: {
    getDashboard: () => request<any>('/admin/dashboard', { method: 'GET' }),
    getUsers: () => request<any[]>('/admin/users', { method: 'GET' }),
    createUser: (dto: any, role: string, tierType: string) =>
      request<any>(`/admin/users?role=${role}&tierType=${tierType}`, {
        method: 'POST',
        body: JSON.stringify(dto),
      }),
    updateUser: (userId: number, dto: any) =>
      request<any>(`/admin/users/${userId}`, { method: 'PUT', body: JSON.stringify(dto) }),
    deleteUser: (userId: number) => request<any>(`/admin/users/${userId}`, { method: 'DELETE' }),
    getTransactions: () => request<any[]>('/admin/transactions', { method: 'GET' }),
    updateTransaction: (transactionId: number, status: string) =>
      request<any>(`/admin/transactions/${transactionId}`, {
        method: 'PUT',
        body: JSON.stringify({ status }),
      }),
    getReports: () => request<any[]>('/admin/reports', { method: 'GET' }),
    resolveReport: (reportId: number, action: string) =>
      request<any>(`/admin/reports/${reportId}/resolve?action=${action}`, { method: 'POST' }),
    getDocuments: (query = '') =>
      request<any[]>(`/admin/documents${query ? `?query=${encodeURIComponent(query)}` : ''}`, {
        method: 'GET',
      }),
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
  },
};
