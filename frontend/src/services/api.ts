const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5065/api';

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = localStorage.getItem('token');
  const headers = new Headers(options.headers || {});

  // Add authorization header if token exists
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  // Set default Content-Type to JSON unless we are uploading FormData (multipart/form-data)
  if (!(options.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  });

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
      window.dispatchEvent(new Event('auth-status-changed'));
    }

    throw new Error(errorMessage);
  }

  // Handle empty or 204 No Content responses
  const text = await response.text();
  return text ? (JSON.parse(text) as T) : ({} as T);
}

export const api = {
  // Authentication
  auth: {
    login: (dto: any) => request<any>('/auth/login', { method: 'POST', body: JSON.stringify(dto) }),
    register: (dto: any) => request<any>('/auth/register', { method: 'POST', body: JSON.stringify(dto) }),
    forgotPassword: (email: string) => request<any>('/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email }) }),
    verifyOtp: (dto: any) => request<any>('/auth/verify-otp', { method: 'POST', body: JSON.stringify(dto) }),
    resetPassword: (dto: any) => request<any>('/auth/reset-password', { method: 'POST', body: JSON.stringify(dto) }),
    getMe: () => request<any>('/auth/me', { method: 'GET' }),
  },

  // Documents
  document: {
    upload: (file: File, folderId?: number) => {
      const formData = new FormData();
      formData.append('file', file);
      const url = folderId ? `/document/upload?folderId=${folderId}` : '/document/upload';
      return request<any>(url, { method: 'POST', body: formData });
    },
    confirm: (documentId: number, title: string, sharingPermission: string, folderId?: number) => {
      const folderParam = folderId ? `&folderId=${folderId}` : '';
      return request<any>(`/document/confirm?documentId=${documentId}&title=${encodeURIComponent(title)}&sharingPermission=${sharingPermission}${folderParam}`, { method: 'POST' });
    },
    replace: (pendingDocId: number, duplicateDocId: number, title: string, sharingPermission: string, folderId?: number) => {
      const folderParam = folderId ? `&folderId=${folderId}` : '';
      return request<any>(`/document/replace?pendingDocId=${pendingDocId}&duplicateDocId=${duplicateDocId}&title=${encodeURIComponent(title)}&sharingPermission=${sharingPermission}${folderParam}`, { method: 'POST' });
    },
    keepBoth: (pendingDocId: number, title: string, sharingPermission: string, folderId?: number) => {
      const folderParam = folderId ? `&folderId=${folderId}` : '';
      return request<any>(`/document/keep-both?pendingDocId=${pendingDocId}&title=${encodeURIComponent(title)}&sharingPermission=${sharingPermission}${folderParam}`, { method: 'POST' });
    },
    cancel: (pendingDocId: number) => request<any>(`/document/cancel?pendingDocId=${pendingDocId}`, { method: 'POST' }),
    getUserDocuments: (folderId?: number) => {
      const url = folderId ? `/document?folderId=${folderId}` : '/document';
      return request<any[]>(url, { method: 'GET' });
    },
    getPublicDocuments: () => request<any[]>('/document/public', { method: 'GET' }),
    getById: (id: number) => request<any>(`/document/${id}`, { method: 'GET' }),
    delete: (id: number) => request<any>(`/document/${id}`, { method: 'DELETE' }),
    getText: (id: number) => request<any>(`/document/${id}/text`, { method: 'GET' }),
    report: (dto: any) => request<any>('/document/report', { method: 'POST', body: JSON.stringify(dto) }),
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
    update: (id: number, dto: any) => request<any>(`/folder/${id}`, { method: 'PUT', body: JSON.stringify(dto) }),
    delete: (id: number) => request<any>(`/folder/${id}`, { method: 'DELETE' }),
  },

  // Chat
  chat: {
    getSessions: () => request<any[]>('/chat/sessions', { method: 'GET' }),
    createSession: (dto: any) => request<any>('/chat/sessions', { method: 'POST', body: JSON.stringify(dto) }),
    pinSession: (sessionId: number, pin: boolean) => request<any>(`/chat/sessions/${sessionId}/pin?pin=${pin}`, { method: 'POST' }),
    deleteSession: (sessionId: number) => request<any>(`/chat/sessions/${sessionId}`, { method: 'DELETE' }),
    getMessages: (sessionId: number) => request<any[]>(`/chat/sessions/${sessionId}/messages`, { method: 'GET' }),
    askQuestion: (sessionId: number, dto: any) => request<any>(`/chat/sessions/${sessionId}/ask`, { method: 'POST', body: JSON.stringify(dto) }),
  },

  // Friendship
  friendship: {
    sendRequest: (addresseeId: number) => request<any>('/friendship/request', { method: 'POST', body: JSON.stringify({ addresseeId }) }),
    respond: (targetUserId: number, status: string) => request<any>(`/friendship/respond?targetUserId=${targetUserId}&status=${status}`, { method: 'POST' }),
    delete: (targetUserId: number) => request<any>(`/friendship?targetUserId=${targetUserId}`, { method: 'DELETE' }),
    getFriends: () => request<any[]>('/friendship/friends', { method: 'GET' }),
    getPending: () => request<any[]>('/friendship/pending', { method: 'GET' }),
    getBlocked: () => request<any[]>('/friendship/blocked', { method: 'GET' }),
    find: (email: string) => request<any>(`/friendship/find?email=${encodeURIComponent(email)}`, { method: 'GET' }),
  },

  // Transactions
  transaction: {
    create: (dto: any) => request<any>('/transaction', { method: 'POST', body: JSON.stringify(dto) }),
    getUserTransactions: () => request<any[]>('/transaction', { method: 'GET' }),
    buyPremium: () => request<any>('/transaction/buy-premium', { method: 'POST' }),
    getTiers: () => request<any[]>('/transaction/tiers', { method: 'GET' }),
  },

  // Admin
  admin: {
    getDashboard: () => request<any>('/admin/dashboard', { method: 'GET' }),
    getUsers: () => request<any[]>('/admin/users', { method: 'GET' }),
    createUser: (dto: any, role: string, tierType: string) => request<any>(`/admin/users?role=${role}&tierType=${tierType}`, { method: 'POST', body: JSON.stringify(dto) }),
    updateUser: (userId: number, dto: any) => request<any>(`/admin/users/${userId}`, { method: 'PUT', body: JSON.stringify(dto) }),
    deleteUser: (userId: number) => request<any>(`/admin/users/${userId}`, { method: 'DELETE' }),
    getTransactions: () => request<any[]>('/admin/transactions', { method: 'GET' }),
    updateTransaction: (transactionId: number, status: string) => request<any>(`/admin/transactions/${transactionId}`, { method: 'PUT', body: JSON.stringify({ status }) }),
    getReports: () => request<any[]>('/admin/reports', { method: 'GET' }),
    resolveReport: (reportId: number, action: string) => request<any>(`/admin/reports/${reportId}/resolve?action=${action}`, { method: 'POST' }),
  },
};
