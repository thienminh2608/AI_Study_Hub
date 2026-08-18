import React, { useState, useEffect } from 'react';
import { api } from '../services/api';
import {
  Folder,
  FolderOpen,
  FileText,
  Upload,
  ChevronRight,
  Trash2,
  Bot,
  AlertTriangle,
  FolderPlus,
  Loader,
  Clock3,
  X,
  Share2,
  Star,
  ArrowUpDown,
  History,
  Shield,
  LayoutGrid,
  List,
  CheckSquare,
  Square,
  Download,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { FileTypeIcon } from '../components/FileTypeIcon';
import { useUiFeedback } from '../context/UiFeedbackContext';
import { formatDateTime } from '../utils/dateTime';
import { ManageAccessModal } from '../components/ManageAccessModal';
import { DocumentVersionHistoryModal } from '../components/DocumentVersionHistoryModal';

interface FolderItem {
  folderId: number;
  folderName: string;
  parentFolderId?: number;
  sharingPermission: string;
  createdAt?: string;
}

interface DocumentItem {
  documentId: number;
  title: string;
  fileExtension: string;
  fileSizeMb: number;
  sharingPermission: string;
  createdAt?: string;
  aiParsingStatus: string;
  cloudStorageUrl: string;
  downloadCount?: number;
  viewCount?: number;
  bookmarkCount?: number;
  subject?: string;
  requiresAppeal?: boolean;
  publicReviewBlocked?: boolean;
  appealStatus?: string;
}

const getCleanTitle = (title: string, ext?: string) => {
  if (!title) return '';
  let clean = title;
  const lastSlash = Math.max(clean.lastIndexOf('/'), clean.lastIndexOf('\\'));
  if (lastSlash >= 0) {
    clean = clean.substring(lastSlash + 1);
  }
  if (ext) {
    const lowerExt = `.${ext.toLowerCase()}`;
    if (clean.toLowerCase().endsWith(lowerExt)) {
      clean = clean.substring(0, clean.length - lowerExt.length);
    }
  }
  return clean;
};

export const Dashboard: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const navigate = useNavigate();

  // Navigation & Hierarchy
  const [currentFolderId, setCurrentFolderId] = useState<number | undefined>(undefined);
  const [breadcrumbs, setBreadcrumbs] = useState<FolderItem[]>([]);

  // Lists
  const [allFolders, setAllFolders] = useState<FolderItem[]>([]); // For tree
  const [subFolders, setSubFolders] = useState<FolderItem[]>([]); // Current folder children
  const [documents, setDocuments] = useState<DocumentItem[]>([]); // Current folder files
  const [analytics, setAnalytics] = useState<any>({
    totalDocuments: 0,
    publicDocuments: 0,
    privateDocuments: 0,
    totalDownloads: 0,
    totalViews: 0,
    totalBookmarks: 0,
    documents: [],
    pendingReviewCount: 0,
    pendingReviewDocuments: [],
  });
  const [audienceDetail, setAudienceDetail] = useState<any | null>(null);
  const [showPendingReviews, setShowPendingReviews] = useState(false);
  const [pendingNameDirection, setPendingNameDirection] = useState<'asc' | 'desc'>('asc');
  const [shareTarget, setShareTarget] = useState<DocumentItem | null>(null);
  const [friends, setFriends] = useState<any[]>([]);
  const [sharedUserIds, setSharedUserIds] = useState<Set<number>>(new Set());
  const [shareDraftUserIds, setShareDraftUserIds] = useState<Set<number>>(new Set());
  const [favoriteFriendIds, setFavoriteFriendIds] = useState<Set<number>>(() => {
    try {
      return new Set(JSON.parse(localStorage.getItem('favorite-share-friends') ?? '[]'));
    } catch {
      return new Set();
    }
  });
  const [friendNameDirection, setFriendNameDirection] = useState<'asc' | 'desc'>('asc');
  const [savingShares, setSavingShares] = useState(false);
  const [sharedWithMe, setSharedWithMe] = useState<DocumentItem[]>([]);

  // Modals & Forms
  const [showCreateFolder, setShowCreateFolder] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');

  // Upload States
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState('');
  const [uploadDraft, setUploadDraft] = useState<{
    file: File;
    title: string;
    subject: string;
    sharingPermission: 'PUBLIC' | 'PRIVATE';
  } | null>(null);

  // Collision Modal
  const [showCollisionModal, setShowCollisionModal] = useState(false);
  const [pendingDoc, setPendingDoc] = useState<any>(null);
  const [duplicateDocId, setDuplicateDocId] = useState<number | null>(null);

  // Modals for Access & Versioning
  const [accessModalItem, setAccessModalItem] = useState<{ type: 'document' | 'folder'; id: number } | null>(null);
  const [versionModalDocId, setVersionModalDocId] = useState<number | null>(null);

  // Task 16: List/Grid View & Bulk Actions
  const [viewMode, setViewMode] = useState<'grid' | 'list'>(() => {
    return (localStorage.getItem('dashboard-view-mode') as 'grid' | 'list') || 'grid';
  });
  const [selectedDocIds, setSelectedDocIds] = useState<Set<number>>(new Set());
  const [bulkProcessing, setBulkProcessing] = useState(false);

  const toggleViewMode = (mode: 'grid' | 'list') => {
    setViewMode(mode);
    localStorage.setItem('dashboard-view-mode', mode);
  };

  const toggleSelectDoc = (docId: number) => {
    setSelectedDocIds((prev) => {
      const next = new Set(prev);
      if (next.has(docId)) next.delete(docId);
      else next.add(docId);
      return next;
    });
  };

  const toggleSelectAllDocs = () => {
    if (selectedDocIds.size === documents.length && documents.length > 0) {
      setSelectedDocIds(new Set());
    } else {
      setSelectedDocIds(new Set(documents.map((d) => d.documentId)));
    }
  };

  const handleBulkDelete = async () => {
    if (selectedDocIds.size === 0) return;
    if (
      !(await confirm({
        title: 'Xóa hàng loạt tài liệu',
        message: `Bạn có chắc chắn muốn xóa ${selectedDocIds.size} tài liệu đã chọn không?`,
        confirmLabel: `Xóa ${selectedDocIds.size} tài liệu`,
        danger: true,
      }))
    ) {
      return;
    }

    setBulkProcessing(true);
    try {
      for (const docId of Array.from(selectedDocIds)) {
        await api.document.delete(docId);
      }
      notify(`Đã xóa ${selectedDocIds.size} tài liệu thành công.`, 'success');
      setSelectedDocIds(new Set());
      loadFolderContent();
    } catch (err: any) {
      notify(err.message || 'Lỗi khi xóa hàng loạt.', 'error');
    } finally {
      setBulkProcessing(false);
    }
  };

  const handleBulkDownload = () => {
    if (selectedDocIds.size === 0) return;
    Array.from(selectedDocIds).forEach((id) => {
      const link = document.createElement('a');
      link.href = `http://localhost:5065/api/document/${id}/download`;
      link.target = '_blank';
      link.click();
    });
    notify(`Đã bắt đầu tải về ${selectedDocIds.size} tài liệu.`, 'success');
  };

  // Loading
  const [loading, setLoading] = useState(true);
  const [askingDocumentId, setAskingDocumentId] = useState<number | null>(null);
  const [deleteDocumentTarget, setDeleteDocumentTarget] = useState<DocumentItem | null>(null);

  // Load Folder Content
  const loadFolderContent = async () => {
    setLoading(true);
    try {
      // 1. Fetch child folders and documents
      const foldersData = await api.folder.getChildFolders(currentFolderId);
      const docsData = await api.document.getUserDocuments(currentFolderId);
      setSubFolders(foldersData);
      setDocuments(docsData as any);

      // 2. Fetch all folders (for Sidebar Tree)
      const allFoldersData = await api.folder.getAllFolders();
      setAllFolders(allFoldersData);
      setAnalytics(await api.document.getAnalytics());
      if (currentFolderId === undefined) setSharedWithMe(await api.document.getSharedWithMe());

      // 3. Build Breadcrumbs
      if (currentFolderId) {
        const chain: FolderItem[] = [];
        let curId: number | undefined = currentFolderId;
        while (curId) {
          const folderObj = allFoldersData.find((f) => f.folderId === curId);
          if (folderObj) {
            chain.unshift(folderObj);
            curId = folderObj.parentFolderId;
          } else {
            break;
          }
        }
        setBreadcrumbs(chain);
      } else {
        setBreadcrumbs([]);
      }
    } catch (err: any) {
      notify(err.message || 'Lỗi khi tải tài nguyên.', 'error');
    } finally {
      setLoading(false);
    }
  };

  // Folder navigation is the sole trigger; the loader reads the latest state from this render.
  // oxlint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    loadFolderContent();
  }, [currentFolderId]);

  // Folder CRUD
  const handleCreateFolder = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newFolderName.trim()) return;

    try {
      await api.folder.create({
        folderName: newFolderName.trim(),
        parentFolderId: currentFolderId,
      });
      setNewFolderName('');
      setShowCreateFolder(false);
      loadFolderContent();
    } catch (err: any) {
      notify(err.message || 'Không thể tạo thư mục.', 'error');
    }
  };

  const handleDeleteFolder = async (folderId: number) => {
    if (
      !(await confirm({
        title: 'Xóa thư mục và toàn bộ nội dung',
        message:
          'Hành động này sẽ xóa vĩnh viễn thư mục cùng tất cả tài liệu và thư mục con bên trong.',
        confirmLabel: 'Xóa vĩnh viễn',
        danger: true,
      }))
    ) {
      return;
    }

    try {
      await api.folder.delete(folderId);
      loadFolderContent();
      notify('Đã xóa thư mục.', 'success');
    } catch (err: any) {
      notify(err.message || 'Không thể xóa thư mục.', 'error');
    }
  };

  // Document Upload & Collision checks
  const handleFileSelected = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const title = file.name.substring(0, file.name.lastIndexOf('.')) || file.name;
    setUploadDraft({ file, title, subject: 'Khác', sharingPermission: 'PRIVATE' });
    e.target.value = '';
  };

  const handleConfirmUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!uploadDraft || !uploadDraft.title.trim() || !uploadDraft.subject.trim()) return;
    const { file, subject, sharingPermission } = uploadDraft;
    const finalTitle = uploadDraft.title.trim();
    setUploading(true);
    setUploadProgress('Đang tải file lên máy chủ...');
    try {
      const fileExt = file.name.split('.').pop() || '';
      const response = await api.document.upload(file, currentFolderId);
      const duplicate = documents.find(
        (d) =>
          d.title.trim().toLocaleLowerCase('vi') === finalTitle.toLocaleLowerCase('vi') &&
          d.fileExtension.toLowerCase() === fileExt.toLowerCase(),
      );
      setUploadDraft(null);

      if (duplicate) {
        setPendingDoc({
          pendingDocId: response.documentId,
          title: finalTitle,
          subject,
          sharingPermission,
          folderId: currentFolderId,
        });
        setDuplicateDocId(duplicate.documentId);
        setShowCollisionModal(true);
        setUploading(false);
      } else {
        setUploadProgress('Đang xử lý nội dung văn bản...');
        await api.document.confirm(
          response.documentId,
          finalTitle,
          subject,
          sharingPermission,
          currentFolderId,
        );
        loadFolderContent();
        setUploading(false);
      }
    } catch (err: any) {
      notify(err.message || 'Lỗi tải tài liệu.', 'error');
      setUploading(false);
    }
  };

  // Collision Actions
  const handleCollisionReplace = async () => {
    if (!pendingDoc || !duplicateDocId) return;
    setUploading(true);
    setShowCollisionModal(false);
    try {
      await api.document.replace(
        pendingDoc.pendingDocId,
        duplicateDocId,
        pendingDoc.title,
        pendingDoc.subject,
        pendingDoc.sharingPermission,
        pendingDoc.folderId,
      );
      loadFolderContent();
    } catch (err: any) {
      notify(err.message || 'Ghi đè thất bại.', 'error');
    } finally {
      setUploading(false);
      setPendingDoc(null);
      setDuplicateDocId(null);
    }
  };

  const handleCollisionKeepBoth = async () => {
    if (!pendingDoc) return;
    setUploading(true);
    setShowCollisionModal(false);
    try {
      await api.document.keepBoth(
        pendingDoc.pendingDocId,
        pendingDoc.title,
        pendingDoc.subject,
        pendingDoc.sharingPermission,
        pendingDoc.folderId,
      );
      loadFolderContent();
    } catch (err: any) {
      notify(err.message || 'Lưu cả hai thất bại.', 'error');
    } finally {
      setUploading(false);
      setPendingDoc(null);
    }
  };

  const handleCollisionCancel = async () => {
    if (!pendingDoc) return;
    setShowCollisionModal(false);
    try {
      await api.document.cancel(pendingDoc.pendingDocId);
    } catch (err: any) {
      console.error('Cancel upload err:', err);
    } finally {
      setPendingDoc(null);
    }
  };

  const handleDeleteDocument = async (id: number) => {
    try {
      await api.document.delete(id);
      setDeleteDocumentTarget(null);
      loadFolderContent();
    } catch (err: any) {
      notify(err.message || 'Xóa tài liệu thất bại.', 'error');
    }
  };

  const handleAskAi = async (doc: DocumentItem) => {
    if (askingDocumentId) return;
    setAskingDocumentId(doc.documentId);
    try {
      const session = await api.chat.createSession({ sessionName: doc.title });
      navigate(`/chat?sessionId=${session.sessionId}&documentId=${doc.documentId}`);
    } catch (err: any) {
      notify(err.message || 'Không thể tạo phiên Hỏi AI.', 'error');
    } finally {
      setAskingDocumentId(null);
    }
  };

  // Folder tree builder helper
  const renderFolderTree = (parentId: number | null = null, depth = 0) => {
    const list = allFolders.filter((f) => (f.parentFolderId ?? null) === parentId);
    return list.map((f) => (
      <div key={f.folderId} style={{ paddingLeft: `${depth * 12}px` }}>
        <button
          className={`tree-node ${currentFolderId === f.folderId ? 'active' : ''}`}
          onClick={() => setCurrentFolderId(f.folderId)}
        >
          <Folder size={16} />
          <span>{f.folderName}</span>
        </button>
        {renderFolderTree(f.folderId, depth + 1)}
      </div>
    ));
  };

  const openShare = async (document: DocumentItem) => {
    const [friendItems, shares] = await Promise.all([
      api.friendship.getFriends(),
      api.document.getShares(document.documentId),
    ]);
    setFriends(friendItems);
    const existing = new Set<number>(shares.map((item: any) => item.sharedWithUserId));
    setSharedUserIds(existing);
    setShareDraftUserIds(new Set(existing));
    setShareTarget(document);
  };

  const toggleShareDraft = (friendUserId: number) => {
    setShareDraftUserIds((current) => {
      const next = new Set(current);
      if (next.has(friendUserId)) next.delete(friendUserId);
      else next.add(friendUserId);
      return next;
    });
  };

  const toggleFavoriteFriend = (friendUserId: number) => {
    setFavoriteFriendIds((current) => {
      const next = new Set(current);
      if (next.has(friendUserId)) next.delete(friendUserId);
      else next.add(friendUserId);
      localStorage.setItem('favorite-share-friends', JSON.stringify([...next]));
      return next;
    });
  };

  const confirmShares = async () => {
    if (!shareTarget) return;
    setSavingShares(true);
    try {
      const additions = [...shareDraftUserIds].filter((id) => !sharedUserIds.has(id));
      const removals = [...sharedUserIds].filter((id) => !shareDraftUserIds.has(id));
      await Promise.all([
        ...additions.map((id) => api.document.shareWithFriend(shareTarget.documentId, id)),
        ...removals.map((id) => api.document.removeShare(shareTarget.documentId, id)),
      ]);
      notify('Đã cập nhật danh sách bạn bè được chia sẻ.', 'success');
      setShareTarget(null);
    } catch (err: any) {
      notify(err.message || 'Không thể cập nhật chia sẻ tài liệu.', 'error');
    } finally {
      setSavingShares(false);
    }
  };

  return (
    <div className="dashboard-container">
      <section className="user-analytics glass-panel">
        <div className="analytics-heading">
          <div>
            <h2>Thống kê tài liệu công khai</h2>
            <p>Lượt tương tác trên các tài liệu bạn đã chia sẻ công khai.</p>
          </div>
          <span>{analytics.totalDocuments} tài liệu</span>
        </div>
        <div className="analytics-stats">
          <button className="analytics-stat-action" onClick={() => setShowPendingReviews(true)}>
            <strong>{analytics.pendingReviewCount ?? 0}</strong>
            <span>
              <Clock3 size={14} /> Chờ xét duyệt
            </span>
          </button>
          <div>
            <strong>{analytics.publicDocuments}</strong>
            <span>Công khai</span>
          </div>
          <div>
            <strong>{analytics.totalViews}</strong>
            <span>Lượt xem</span>
          </div>
          <div>
            <strong>{analytics.totalDownloads}</strong>
            <span>Lượt tải</span>
          </div>
          <div>
            <strong>{analytics.totalBookmarks}</strong>
            <span>Lượt lưu</span>
          </div>
        </div>
        <div className="analytics-documents">
          {analytics.documents.map((doc: any) => (
            <button
              key={doc.documentId}
              onClick={async () =>
                setAudienceDetail(await api.document.getAudience(doc.documentId))
              }
            >
              <FileTypeIcon
                extension={doc.fileExtension}
                size={22}
                className="analytics-file-icon"
              />
              <span>
                <strong>
                  {doc.title}.{doc.fileExtension}
                </strong>
                <small>Công khai</small>
              </span>
              <span>
                {doc.viewCount ?? 0} xem · {doc.downloadCount ?? 0} tải
              </span>
            </button>
          ))}
        </div>
      </section>
      <div className="explorer-layout">
        {/* Left Tree Explorer Bar */}
        <aside className="tree-explorer glass-panel">
          <h3>Thư mục của tôi</h3>
          <button className="tree-node root-node" onClick={() => setCurrentFolderId(undefined)}>
            <FolderOpen size={16} />
            <span>Root /</span>
          </button>
          <div className="tree-scroll">{renderFolderTree(null, 0)}</div>
        </aside>

        {/* Right Main explorer pane */}
        <div className="explorer-pane glass-panel">
          {/* Action Row */}
          <div className="action-header">
            {/* Breadcrumbs */}
            <div className="breadcrumbs">
              <span onClick={() => setCurrentFolderId(undefined)} className="crumb">
                Root
              </span>
              {breadcrumbs.map((crumb) => (
                <React.Fragment key={crumb.folderId}>
                  <ChevronRight size={14} className="crumb-arrow" />
                  <span onClick={() => setCurrentFolderId(crumb.folderId)} className="crumb">
                    {crumb.folderName}
                  </span>
                </React.Fragment>
              ))}
            </div>

            {/* Operations & View Toggle */}
            <div className="actions" style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <div className="glass-card flex items-center p-1 rounded-lg" style={{ display: 'flex', gap: '0.2rem', padding: '0.25rem' }}>
                <button
                  onClick={() => toggleViewMode('grid')}
                  className={`action-btn ${viewMode === 'grid' ? 'active' : ''}`}
                  style={{ opacity: viewMode === 'grid' ? 1 : 0.5 }}
                  title="Chế độ Lưới (Grid View)"
                >
                  <LayoutGrid size={16} />
                </button>
                <button
                  onClick={() => toggleViewMode('list')}
                  className={`action-btn ${viewMode === 'list' ? 'active' : ''}`}
                  style={{ opacity: viewMode === 'list' ? 1 : 0.5 }}
                  title="Chế độ Danh sách (List View)"
                >
                  <List size={16} />
                </button>
              </div>

              <button onClick={() => setShowCreateFolder(true)} className="btn-secondary">
                <FolderPlus size={16} />
                <span>Thư mục mới</span>
              </button>

              <label className="btn-primary upload-label">
                <Upload size={16} />
                <span>Tải tệp lên</span>
                <input
                  type="file"
                  accept=".pdf,.docx,.txt,.xlsx,.pptx,.md"
                  onChange={handleFileSelected}
                  style={{ display: 'none' }}
                  disabled={uploading}
                />
              </label>
            </div>
          </div>

          {/* Bulk Operations Bar */}
          {selectedDocIds.size > 0 && (
            <div className="glass-card p-3 my-3 flex items-center justify-between rounded-xl" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.75rem 1rem', margin: '0.75rem 0', background: 'rgba(30, 41, 59, 0.9)', border: '1px solid rgba(255,255,255,0.15)', borderRadius: '12px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                <button
                  onClick={toggleSelectAllDocs}
                  style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', background: 'none', border: 'none', color: '#e2e8f0', cursor: 'pointer' }}
                >
                  {selectedDocIds.size === documents.length ? (
                    <CheckSquare size={18} style={{ color: '#60a5fa' }} />
                  ) : (
                    <Square size={18} />
                  )}
                  <span>
                    Đã chọn {selectedDocIds.size} / {documents.length} tài liệu
                  </span>
                </button>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <button
                  onClick={handleBulkDownload}
                  disabled={bulkProcessing}
                  className="btn-secondary"
                  style={{ padding: '0.4rem 0.8rem', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '0.3rem' }}
                >
                  <Download size={15} />
                  <span>Tải về ({selectedDocIds.size})</span>
                </button>
                <button
                  onClick={handleBulkDelete}
                  disabled={bulkProcessing}
                  className="btn-danger"
                  style={{ padding: '0.4rem 0.8rem', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '0.3rem' }}
                >
                  <Trash2 size={15} />
                  <span>Xóa ({selectedDocIds.size})</span>
                </button>
                <button
                  onClick={() => setSelectedDocIds(new Set())}
                  className="action-btn"
                  title="Hủy chọn"
                >
                  <X size={16} />
                </button>
              </div>
            </div>
          )}

          {/* Loader bar for uploads */}
          {uploading && (
            <div className="upload-loader glass-card">
              <Loader size={20} className="spin" />
              <span>{uploadProgress}</span>
            </div>
          )}

          {/* Content view */}
          {loading ? (
            <div className="loading-container">
              <div className="skeleton-row skeleton"></div>
              <div className="skeleton-row skeleton" style={{ width: '80%' }}></div>
              <div className="skeleton-row skeleton" style={{ width: '60%' }}></div>
            </div>
          ) : (
            <div className="explorer-grid">
              {/* Folder Header & Grid */}
              {currentFolderId === undefined && subFolders.length > 0 && (
                <div className="section-block">
                  <h4>Thư mục ({subFolders.length})</h4>
                  <div className="grid-layout">
                    {subFolders.map((folder) => (
                      <div key={folder.folderId} className="item-card folder-card glass-card">
                        <div
                          onClick={() => setCurrentFolderId(folder.folderId)}
                          className="item-info"
                        >
                          <Folder size={28} className="folder-icon" />
                          <span className="item-title">{folder.folderName}</span>
                        </div>
                        <div className="flex items-center space-x-1">
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setAccessModalItem({ type: 'folder', id: folder.folderId });
                            }}
                            className="action-btn"
                            title="Quản lý quyền truy cập thư mục (Manage Access)"
                          >
                            <Shield size={16} />
                          </button>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              handleDeleteFolder(folder.folderId);
                            }}
                            className="delete-item-btn"
                            title="Xóa thư mục"
                          >
                            <Trash2 size={16} />
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Files Header & List/Grid View */}
              {documents.length > 0 && (
                <div className="section-block" style={{ marginTop: '1.5rem' }}>
                  <h4>Tài liệu ({documents.length})</h4>

                  {viewMode === 'list' ? (
                    <div className="glass-card overflow-hidden my-3" style={{ borderRadius: '12px', border: '1px solid rgba(255,255,255,0.1)' }}>
                      <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '0.9rem' }}>
                        <thead>
                          <tr style={{ background: 'rgba(15, 23, 42, 0.7)', color: '#94a3b8', borderBottom: '1px solid rgba(255,255,255,0.1)', fontSize: '0.8rem', textTransform: 'uppercase' }}>
                            <th style={{ padding: '0.75rem', width: '2.5rem', textAlign: 'center' }}>
                              <button onClick={toggleSelectAllDocs} style={{ background: 'none', border: 'none', color: 'inherit', cursor: 'pointer' }}>
                                {selectedDocIds.size > 0 && selectedDocIds.size === documents.length ? (
                                  <CheckSquare size={16} style={{ color: '#60a5fa' }} />
                                ) : (
                                  <Square size={16} />
                                )}
                              </button>
                            </th>
                            <th style={{ padding: '0.75rem' }}>Tài liệu</th>
                            <th style={{ padding: '0.75rem' }}>Môn học</th>
                            <th style={{ padding: '0.75rem' }}>Dung lượng</th>
                            <th style={{ padding: '0.75rem' }}>Trạng thái AI</th>
                            <th style={{ padding: '0.75rem' }}>Ngày tạo</th>
                            <th style={{ padding: '0.75rem', textAlign: 'right' }}>Thao tác</th>
                          </tr>
                        </thead>
                        <tbody>
                          {documents.map((doc) => {
                            const isSelected = selectedDocIds.has(doc.documentId);
                            return (
                              <tr key={doc.documentId} style={{ borderBottom: '1px solid rgba(255,255,255,0.05)', background: isSelected ? 'rgba(59, 130, 246, 0.1)' : 'transparent' }}>
                                <td style={{ padding: '0.75rem', textAlign: 'center' }}>
                                  <button onClick={() => toggleSelectDoc(doc.documentId)} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'inherit' }}>
                                    {isSelected ? <CheckSquare size={16} style={{ color: '#60a5fa' }} /> : <Square size={16} style={{ color: '#64748b' }} />}
                                  </button>
                                </td>
                                <td style={{ padding: '0.75rem', fontWeight: 500 }}>
                                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', cursor: 'pointer' }} onClick={() => navigate(`/document/${doc.documentId}`)}>
                                    <FileTypeIcon extension={doc.fileExtension} size={22} />
                                    <span style={{ textDecoration: 'underline' }}>{getCleanTitle(doc.title, doc.fileExtension)}.{doc.fileExtension}</span>
                                  </div>
                                </td>
                                <td style={{ padding: '0.75rem', color: '#94a3b8' }}>{doc.subject || 'Khác'}</td>
                                <td style={{ padding: '0.75rem', color: '#94a3b8' }}>{doc.fileSizeMb.toFixed(2)} MB</td>
                                <td style={{ padding: '0.75rem' }}><span style={{ padding: '0.2rem 0.5rem', borderRadius: '4px', fontSize: '0.75rem', background: 'rgba(51, 65, 85, 0.6)', color: '#cbd5e1' }}>{doc.aiParsingStatus}</span></td>
                                <td style={{ padding: '0.75rem', color: '#94a3b8', fontSize: '0.8rem' }}>{doc.createdAt ? formatDateTime(doc.createdAt) : '-'}</td>
                                <td style={{ padding: '0.75rem', textAlign: 'right' }}>
                                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: '0.3rem' }}>
                                    <button onClick={(e) => { e.stopPropagation(); setAccessModalItem({ type: 'document', id: doc.documentId }); }} className="action-btn" title="Quản lý quyền truy cập"><Shield size={15} /></button>
                                    <button onClick={(e) => { e.stopPropagation(); setVersionModalDocId(doc.documentId); }} className="action-btn" title="Lịch sử phiên bản"><History size={15} /></button>
                                    <button onClick={() => openShare(doc)} className="action-btn" title="Chia sẻ cho bạn bè"><Share2 size={15} /></button>
                                    <button onClick={() => handleAskAi(doc)} className="action-btn ask-ai-btn" title="Hỏi AI" disabled={askingDocumentId === doc.documentId}>{askingDocumentId === doc.documentId ? <Loader className="spin" size={15} /> : <Bot size={15} />}</button>
                                    <button onClick={() => setDeleteDocumentTarget(doc)} className="delete-item-btn" title="Xóa tài liệu"><Trash2 size={15} /></button>
                                  </div>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div className="grid-layout">
                      {documents.map((doc) => {
                        const isSelected = selectedDocIds.has(doc.documentId);
                        return (
                          <div key={doc.documentId} className={`item-card doc-card glass-card ${isSelected ? 'selected-card' : ''}`} style={isSelected ? { border: '1px solid #60a5fa', background: 'rgba(59, 130, 246, 0.08)' } : {}}>
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                toggleSelectDoc(doc.documentId);
                              }}
                              style={{ position: 'absolute', top: '0.6rem', right: '0.6rem', background: 'none', border: 'none', cursor: 'pointer', zIndex: 2 }}
                              title="Chọn tài liệu"
                            >
                              {isSelected ? <CheckSquare size={18} style={{ color: '#60a5fa' }} /> : <Square size={18} style={{ color: '#64748b' }} />}
                            </button>
                            <div
                              onClick={() => navigate(`/document/${doc.documentId}`)}
                              className="item-info"
                            >
                              <FileTypeIcon
                                extension={doc.fileExtension}
                                size={28}
                                className="doc-icon"
                              />
                              <div className="doc-metadata">
                                <span className="item-title" title={`${getCleanTitle(doc.title, doc.fileExtension)}.${doc.fileExtension}`}>
                                  {getCleanTitle(doc.title, doc.fileExtension)}.{doc.fileExtension}
                                </span>
                                <span className="doc-size">
                                  {doc.subject || 'Khác'} • {doc.fileSizeMb.toFixed(2)} MB •{' '}
                                  {doc.aiParsingStatus}
                                </span>
                                {doc.requiresAppeal && (
                                  <span className="appeal-required-badge">Cần gửi giải trình</span>
                                )}
                                {!doc.requiresAppeal && doc.appealStatus === 'PENDING' && (
                                  <span className="appeal-required-badge">
                                    Giải trình đang chờ xử lý
                                  </span>
                                )}
                                {doc.publicReviewBlocked && doc.appealStatus === 'UPHELD' && (
                                  <span className="appeal-required-badge">
                                    Vi phạm vẫn còn hiệu lực
                                  </span>
                                )}
                              </div>
                            </div>
                            <div className="card-actions">
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  setAccessModalItem({ type: 'document', id: doc.documentId });
                                }}
                                className="action-btn"
                                title="Quản lý quyền truy cập (Manage Access)"
                              >
                                <Shield size={16} />
                              </button>
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  setVersionModalDocId(doc.documentId);
                                }}
                                className="action-btn"
                                title="Lịch sử phiên bản (Versioning)"
                              >
                                <History size={16} />
                              </button>
                              <button
                                onClick={() => openShare(doc)}
                                className="action-btn"
                                title="Chia sẻ cho bạn bè"
                              >
                                <Share2 size={16} />
                              </button>
                              <button
                                onClick={() => handleAskAi(doc)}
                                className="action-btn ask-ai-btn"
                                title="Hỏi AI về tài liệu"
                                disabled={askingDocumentId === doc.documentId}
                              >
                                {askingDocumentId === doc.documentId ? (
                                  <Loader className="spin" size={16} />
                                ) : (
                                  <Bot size={16} />
                                )}
                              </button>
                              <button
                                onClick={() => setDeleteDocumentTarget(doc)}
                                className="delete-item-btn"
                                title="Xóa tài liệu"
                              >
                                <Trash2 size={16} />
                              </button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}

              {currentFolderId === undefined && sharedWithMe.length > 0 && (
                <div className="section-block" style={{ marginTop: '1.5rem' }}>
                  <h4>Được bạn bè chia sẻ ({sharedWithMe.length})</h4>
                  <div className="grid-layout">
                    {sharedWithMe.map((doc) => (
                      <div
                        key={`shared-${doc.documentId}`}
                        className="item-card doc-card glass-card"
                      >
                        <div
                          onClick={() => navigate(`/document/${doc.documentId}`)}
                          className="item-info"
                        >
                          <FileTypeIcon
                            extension={doc.fileExtension}
                            size={28}
                            className="doc-icon"
                          />
                          <div className="doc-metadata">
                            <span className="item-title" title={`${getCleanTitle(doc.title, doc.fileExtension)}.${doc.fileExtension}`}>
                              {getCleanTitle(doc.title, doc.fileExtension)}.{doc.fileExtension}
                            </span>
                            <span className="doc-size">Tài liệu được chia sẻ</span>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {(currentFolderId !== undefined || subFolders.length === 0) &&
                documents.length === 0 && (
                  <div className="empty-state">
                    <FolderOpen size={48} className="empty-icon" />
                    <p>Thư mục trống. Kéo thả file hoặc nhấn "Tải tệp lên" để bắt đầu!</p>
                  </div>
                )}
            </div>
          )}
        </div>
      </div>

      {/* Modal: Create Folder */}
      {showCreateFolder && (
        <div className="modal-overlay">
          <div className="modal-box glass-panel animate-slide-up">
            <h3>Tạo thư mục mới</h3>
            <form onSubmit={handleCreateFolder}>
              <input
                type="text"
                placeholder="Nhập tên thư mục..."
                value={newFolderName}
                onChange={(e) => setNewFolderName(e.target.value)}
                className="input-control"
                autoFocus
              />
              <div className="modal-actions">
                <button
                  type="button"
                  onClick={() => setShowCreateFolder(false)}
                  className="btn-secondary"
                >
                  Hủy
                </button>
                <button type="submit" className="btn-primary">
                  Tạo mới
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {uploadDraft && (
        <div className="modal-overlay" onMouseDown={() => !uploading && setUploadDraft(null)}>
          <div
            className="modal-box upload-confirm-modal glass-panel animate-slide-up"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="upload-confirm-heading">
              <FileText size={30} />
              <div>
                <h3>Xác nhận thông tin tài liệu</h3>
                <p>
                  {uploadDraft.file.name} · {(uploadDraft.file.size / 1024 / 1024).toFixed(2)} MB
                </p>
              </div>
            </div>
            <form onSubmit={handleConfirmUpload}>
              <label>
                Tên tài liệu
                <input
                  className="input-control"
                  maxLength={255}
                  required
                  value={uploadDraft.title}
                  onChange={(e) => setUploadDraft({ ...uploadDraft, title: e.target.value })}
                />
              </label>
              <label>
                Môn học
                <select
                  className="input-control"
                  value={uploadDraft.subject}
                  onChange={(e) => setUploadDraft({ ...uploadDraft, subject: e.target.value })}
                >
                  {[
                    'Toán học',
                    'Vật lý',
                    'Hóa học',
                    'Sinh học',
                    'Ngữ văn',
                    'Tiếng Anh',
                    'Tin học',
                    'Kinh tế',
                    'Kỹ năng mềm',
                    'Khác',
                  ].map((subject) => (
                    <option key={subject}>{subject}</option>
                  ))}
                </select>
              </label>
              <fieldset className="sharing-options">
                <legend>Quyền truy cập</legend>
                <label className={uploadDraft.sharingPermission === 'PRIVATE' ? 'selected' : ''}>
                  <input
                    type="radio"
                    name="sharing"
                    value="PRIVATE"
                    checked={uploadDraft.sharingPermission === 'PRIVATE'}
                    onChange={() =>
                      setUploadDraft({ ...uploadDraft, sharingPermission: 'PRIVATE' })
                    }
                  />
                  <span>
                    <strong>Riêng tư</strong>
                    <small>Chỉ bạn có thể xem</small>
                  </span>
                </label>
                <label className={uploadDraft.sharingPermission === 'PUBLIC' ? 'selected' : ''}>
                  <input
                    type="radio"
                    name="sharing"
                    value="PUBLIC"
                    checked={uploadDraft.sharingPermission === 'PUBLIC'}
                    onChange={() => setUploadDraft({ ...uploadDraft, sharingPermission: 'PUBLIC' })}
                  />
                  <span>
                    <strong>Công khai</strong>
                    <small>Chia sẻ với cộng đồng</small>
                  </span>
                </label>
              </fieldset>
              <p className="duplicate-rule-note">
                Hệ thống sẽ kiểm tra trùng theo tên và kiểu file trong thư mục hiện tại sau khi bạn
                xác nhận.
              </p>
              <div className="modal-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  disabled={uploading}
                  onClick={() => setUploadDraft(null)}
                >
                  Hủy
                </button>
                <button className="btn-primary" disabled={uploading || !uploadDraft.title.trim()}>
                  {uploading ? <Loader className="spin" size={16} /> : 'Xác nhận tải lên'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Modal: Collision Handler */}
      {showCollisionModal && (
        <div className="modal-overlay">
          <div className="modal-box collision-box glass-panel animate-slide-up">
            <div className="collision-header">
              <AlertTriangle size={32} className="alert-icon" />
              <h3>Phát hiện tài liệu trùng</h3>
            </div>
            <p>
              Tài liệu <strong>{pendingDoc?.title}</strong> cùng kiểu file đã tồn tại trong thư mục
              hiện tại. Bạn muốn thực hiện hành động nào?
            </p>
            <div className="collision-actions">
              <button onClick={handleCollisionReplace} className="btn-secondary danger-hover">
                Thay thế file cũ
              </button>
              <button onClick={handleCollisionKeepBoth} className="btn-secondary success-hover">
                Giữ cả hai
              </button>
              <button onClick={handleCollisionCancel} className="btn-primary">
                Hủy tải lên
              </button>
            </div>
          </div>
        </div>
      )}

      {audienceDetail && (
        <div className="modal-overlay" onMouseDown={() => setAudienceDetail(null)}>
          <div
            className="modal-box audience-modal glass-panel"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <h3>{audienceDetail.document.title}</h3>
            <p>{audienceDetail.description}</p>
            <h4>Người xem và tải</h4>
            {audienceDetail.audience.length ? (
              audienceDetail.audience.map((person: any) => (
                <div className="audience-row" key={person.userId}>
                  <span>
                    <strong>{person.username}</strong>
                    <small>{person.email}</small>
                  </span>
                  <span>
                    {person.viewCount} xem · {person.downloadCount} tải
                  </span>
                </div>
              ))
            ) : (
              <p>Chưa có hoạt động.</p>
            )}
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setAudienceDetail(null)}>
                Đóng
              </button>
            </div>
          </div>
        </div>
      )}

      {showPendingReviews && (
        <div className="modal-overlay" onMouseDown={() => setShowPendingReviews(false)}>
          <div
            className="modal-box pending-review-modal glass-panel"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <button
              className="modal-close"
              aria-label="Đóng"
              onClick={() => setShowPendingReviews(false)}
            >
              <X size={20} />
            </button>
            <h3>Tài liệu đang chờ xét duyệt</h3>
            <p>{analytics.pendingReviewCount ?? 0} tài liệu đang trong quy trình xét duyệt.</p>
            <div className="pending-table-wrap">
              <table className="pending-review-table">
                <thead>
                  <tr>
                    <th>
                      <button
                        onClick={() =>
                          setPendingNameDirection((v) => (v === 'asc' ? 'desc' : 'asc'))
                        }
                      >
                        Tên tài liệu {pendingNameDirection === 'asc' ? '↑' : '↓'}
                      </button>
                    </th>
                    <th>Trạng thái</th>
                    <th>Ngày yêu cầu</th>
                    <th>Ngày xét duyệt</th>
                  </tr>
                </thead>
                <tbody>
                  {[...(analytics.pendingReviewDocuments ?? [])]
                    .sort((a: any, b: any) => {
                      const result = String(a.title).localeCompare(String(b.title), 'vi');
                      return pendingNameDirection === 'asc' ? result : -result;
                    })
                    .map((doc: any) => (
                      <tr key={doc.documentId}>
                        <td>
                          {doc.title}.{doc.fileExtension}
                        </td>
                        <td>
                          {doc.moderationStatus === 'IN_REVIEW' ? 'Đang xử lý' : 'Chờ xét duyệt'}
                        </td>
                        <td>{formatDateTime(doc.moderationSubmittedAt)}</td>
                        <td>{formatDateTime(doc.moderatedAt)}</td>
                      </tr>
                    ))}
                </tbody>
              </table>
              {!analytics.pendingReviewDocuments?.length && (
                <div className="empty-state">
                  <p>Không có tài liệu đang chờ xét duyệt.</p>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {shareTarget && (
        <div className="modal-overlay" onMouseDown={() => setShareTarget(null)}>
          <div
            className="modal-box share-modal glass-panel"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <button className="modal-close" onClick={() => setShareTarget(null)}>
              <X size={20} />
            </button>
            <h3>Chia sẻ tài liệu cho bạn bè</h3>
            <p>
              {shareTarget.title}.{shareTarget.fileExtension}
            </p>
            <div className="share-toolbar">
              <span>
                Đã chọn {shareDraftUserIds.size}/{friends.length} người
              </span>
              <button
                type="button"
                onClick={() =>
                  setFriendNameDirection((value) => (value === 'asc' ? 'desc' : 'asc'))
                }
              >
                <ArrowUpDown size={15} /> Tên {friendNameDirection === 'asc' ? 'A–Z' : 'Z–A'}
              </button>
            </div>
            <div className="share-friend-list">
              {[...friends]
                .sort((a, b) => {
                  const favoriteDiff =
                    Number(favoriteFriendIds.has(b.userId)) -
                    Number(favoriteFriendIds.has(a.userId));
                  if (favoriteDiff) return favoriteDiff;
                  const result = String(a.username).localeCompare(String(b.username), 'vi');
                  return friendNameDirection === 'asc' ? result : -result;
                })
                .map((friend) => (
                  <label key={friend.userId}>
                    <button
                      type="button"
                      className={`favorite-friend ${favoriteFriendIds.has(friend.userId) ? 'active' : ''}`}
                      onClick={(event) => {
                        event.preventDefault();
                        toggleFavoriteFriend(friend.userId);
                      }}
                      title="Ghim bạn bè để chọn nhanh lần sau"
                      aria-label={`Ghim ${friend.username}`}
                    >
                      <Star
                        size={18}
                        fill={favoriteFriendIds.has(friend.userId) ? 'currentColor' : 'none'}
                      />
                    </button>
                    <span>
                      <strong>{friend.username}</strong>
                      <small>{friend.email}</small>
                    </span>
                    <input
                      type="checkbox"
                      checked={shareDraftUserIds.has(friend.userId)}
                      onChange={() => toggleShareDraft(friend.userId)}
                    />
                  </label>
                ))}
              {!friends.length && <p>Bạn chưa có bạn bè để chia sẻ tài liệu.</p>}
            </div>
            <div className="modal-actions share-actions">
              <button
                type="button"
                className="btn-secondary"
                onClick={() => setShareTarget(null)}
                disabled={savingShares}
              >
                Hủy
              </button>
              <button
                type="button"
                className="btn-primary"
                onClick={confirmShares}
                disabled={savingShares || !friends.length}
              >
                {savingShares ? (
                  <>
                    <Loader className="spin" size={16} /> Đang gửi...
                  </>
                ) : (
                  'Xác nhận gửi'
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {deleteDocumentTarget && (
        <div className="modal-overlay">
          <div className="modal-box glass-panel">
            <h3>Xóa tài liệu?</h3>
            <p>
              Tài liệu <strong>{deleteDocumentTarget.title}</strong> sẽ bị xóa vĩnh viễn và không
              thể khôi phục.
            </p>
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setDeleteDocumentTarget(null)}>
                Hủy
              </button>
              <button
                className="btn-secondary danger-hover"
                onClick={() => handleDeleteDocument(deleteDocumentTarget.documentId)}
              >
                Xác nhận xóa
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Access Management Modal */}
      {accessModalItem && (
        <ManageAccessModal
          itemType={accessModalItem.type}
          itemId={accessModalItem.id}
          isOpen={!!accessModalItem}
          onClose={() => setAccessModalItem(null)}
        />
      )}

      {/* Version History Modal */}
      {versionModalDocId !== null && (
        <DocumentVersionHistoryModal
          documentId={versionModalDocId}
          isOpen={versionModalDocId !== null}
          onClose={() => setVersionModalDocId(null)}
          onVersionChanged={() => loadFolderContent()}
        />
      )}

      <style>{`
        .dashboard-container {
          min-height: 80vh;
          display:flex;
          flex-direction:column;
          gap:1rem;
        }

        .user-analytics{padding:1.2rem;min-width:0}.analytics-heading{display:flex;justify-content:space-between;align-items:center;gap:1rem}.analytics-heading p{color:var(--text-muted)}.analytics-heading>span{color:var(--accent-blue);font-weight:700}.analytics-stats{display:grid;grid-template-columns:repeat(4,1fr);gap:.7rem;margin:1rem 0}.analytics-stats div{padding:.8rem;border-radius:var(--radius-sm);background:rgba(255,255,255,.04);display:flex;flex-direction:column}.analytics-stats strong{font-size:1.35rem}.analytics-stats span,.analytics-documents small{color:var(--text-muted)}.analytics-documents{display:flex;gap:.6rem;overflow-x:scroll;overscroll-behavior-x:contain;padding:.15rem 0 .65rem;scrollbar-color:var(--accent-blue) rgba(255,255,255,.06)}.analytics-documents button{flex:0 0 290px;min-width:290px;border:1px solid rgba(255,255,255,.08);background:rgba(255,255,255,.03);color:var(--text-primary);padding:.7rem;border-radius:var(--radius-sm);display:flex;align-items:center;justify-content:space-between;text-align:left;gap:.75rem}.analytics-documents button span{display:flex;flex-direction:column;min-width:0}.analytics-documents strong{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:145px}.analytics-file-icon{width:38px;height:38px;border-radius:9px;display:grid;place-items:center;flex:0 0 auto}.audience-modal{max-width:650px;max-height:80vh;overflow:auto}.audience-row{display:flex;justify-content:space-between;gap:1rem;padding:.7rem 0;border-bottom:1px solid rgba(255,255,255,.06)}.audience-row span{display:flex;flex-direction:column}.audience-row small{color:var(--text-muted)}

        .explorer-layout {
          display: flex;
          gap: 1.5rem;
          height: calc(100vh - 6rem);
        }

        .analytics-stats{grid-template-columns:repeat(5,1fr)}.analytics-stat-action{padding:.8rem;border:0;border-radius:var(--radius-sm);background:rgba(255,255,255,.04);color:var(--text-primary);display:flex;flex-direction:column;text-align:left;cursor:pointer}.analytics-stat-action:hover{background:rgba(0,180,216,.12)}.analytics-stat-action span{display:flex;align-items:center;gap:.35rem}.pending-review-modal{position:relative;width:min(900px,94vw);max-height:82vh;overflow:auto}.modal-close{position:absolute;right:1rem;top:1rem;border:0;background:transparent;color:var(--text-primary);cursor:pointer}.pending-table-wrap{overflow-x:auto;margin-top:1rem}.pending-review-table{width:100%;border-collapse:collapse}.pending-review-table th,.pending-review-table td{padding:.8rem;text-align:left;border-bottom:1px solid rgba(255,255,255,.08);white-space:nowrap}.pending-review-table th button{border:0;background:transparent;color:var(--text-primary);font:inherit;font-weight:700;cursor:pointer}
        @media(max-width:768px){.analytics-stats{grid-template-columns:repeat(2,1fr)}.analytics-heading,.audience-row{align-items:flex-start;flex-direction:column}}

        .share-modal{position:relative;width:min(620px,94vw);padding:1.5rem}.share-toolbar{display:flex;align-items:center;justify-content:space-between;gap:1rem;margin-top:1rem;padding:.65rem .8rem;border-radius:9px;background:rgba(255,255,255,.035);color:var(--text-muted);font-size:.88rem}.share-toolbar button{display:flex;align-items:center;gap:.35rem;border:0;background:transparent;color:var(--accent-blue);cursor:pointer}.share-friend-list{display:grid;gap:.55rem;margin-top:.75rem;max-height:360px;overflow:auto;padding-right:.2rem}.share-friend-list label{display:flex;align-items:center;gap:.8rem;padding:.8rem;border:1px solid rgba(255,255,255,.08);border-radius:9px;background:rgba(255,255,255,.03);cursor:pointer}.share-friend-list label:hover{border-color:rgba(0,180,216,.35)}.share-friend-list label span{display:grid;flex:1;min-width:0}.share-friend-list small{color:var(--text-muted);overflow:hidden;text-overflow:ellipsis}.share-friend-list input{width:18px;height:18px;flex:0 0 auto}.favorite-friend{display:grid;place-items:center;border:0;background:transparent;color:var(--text-muted);cursor:pointer;padding:.2rem}.favorite-friend.active{color:#f6c344}.share-actions{margin-top:1rem}.share-actions .btn-primary{display:flex;align-items:center;justify-content:center;gap:.4rem;min-width:140px}@media(max-width:520px){.share-toolbar{align-items:flex-start;flex-direction:column}.share-actions{display:grid;grid-template-columns:1fr 1fr}.share-actions button{width:100%}}

        .tree-explorer {
          width: 250px;
          display: flex;
          flex-direction: column;
          padding: 1.25rem;
          border-radius: var(--radius-md);
        }

        .tree-explorer h3 {
          font-size: 1rem;
          color: var(--text-secondary);
          margin-bottom: 1rem;
          text-transform: uppercase;
          letter-spacing: 0.05em;
        }

        .tree-node {
          width: 100%;
          display: flex;
          align-items: center;
          gap: 0.5rem;
          padding: 0.5rem 0.75rem;
          background: transparent;
          border: none;
          color: var(--text-secondary);
          cursor: pointer;
          border-radius: var(--radius-sm);
          font-size: 0.9rem;
          text-align: left;
          transition: var(--transition-fast);
        }

        .tree-node:hover {
          background: rgba(255, 255, 255, 0.03);
          color: var(--text-primary);
        }

        .tree-node.active {
          color: var(--accent-blue);
          background: rgba(0, 180, 216, 0.08);
          font-weight: 600;
        }

        .root-node {
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          margin-bottom: 0.5rem;
          padding-bottom: 0.75rem;
        }

        .tree-scroll {
          flex: 1;
          overflow-y: auto;
        }

        .explorer-pane {
          flex: 1;
          display: flex;
          flex-direction: column;
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .action-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          padding-bottom: 1rem;
          margin-bottom: 1rem;
        }

        .breadcrumbs {
          display: flex;
          align-items: center;
          gap: 0.4rem;
        }

        .crumb {
          cursor: pointer;
          color: var(--text-secondary);
          font-weight: 500;
          transition: var(--transition-fast);
        }

        .crumb:hover {
          color: var(--accent-blue);
        }

        .crumb-arrow {
          color: var(--text-muted);
        }

        .actions {
          display: flex;
          gap: 0.75rem;
        }

        .upload-label {
          cursor: pointer;
        }

        .upload-loader {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          padding: 0.75rem 1.25rem;
          margin-bottom: 1rem;
          color: var(--accent-blue);
          border-color: var(--border-neon-active);
          font-weight: 600;
        }

        .upload-confirm-modal { width:min(560px,calc(100vw - 2rem)); }
        .upload-confirm-heading { display:flex;align-items:center;gap:.8rem;margin-bottom:1.2rem; }
        .upload-confirm-heading>svg { color:var(--accent-blue); }
        .upload-confirm-heading p { color:var(--text-muted);font-size:.82rem;margin-top:.2rem; }
        .upload-confirm-modal form,.upload-confirm-modal form>label { display:flex;flex-direction:column;gap:.45rem; }
        .upload-confirm-modal form { gap:1rem; }
        .sharing-options { border:0;padding:0;display:grid;grid-template-columns:1fr 1fr;gap:.7rem; }
        .sharing-options legend { grid-column:1/-1;color:var(--text-secondary);margin-bottom:.45rem; }
        .sharing-options label { display:flex;align-items:center;gap:.65rem;padding:.8rem;border:1px solid rgba(255,255,255,.09);border-radius:var(--radius-sm);cursor:pointer; }
        .sharing-options label.selected { border-color:var(--accent-blue);background:rgba(0,180,216,.08); }
        .sharing-options label span { display:flex;flex-direction:column;gap:.15rem; }
        .sharing-options small,.duplicate-rule-note { color:var(--text-muted);font-size:.78rem; }
        .duplicate-rule-note { padding:.7rem;border-radius:var(--radius-sm);background:rgba(255,255,255,.035);line-height:1.45; }

        .explorer-grid {
          flex: 1;
          overflow-y: auto;
        }

        .section-block h4 {
          font-size: 0.95rem;
          color: var(--text-secondary);
          margin-bottom: 0.75rem;
          text-transform: uppercase;
          letter-spacing: 0.05em;
        }

        .grid-layout {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
          gap: 1rem;
        }

        .item-card {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 0.75rem 1rem;
          cursor: pointer;
        }

        .doc-card {
          flex-direction: column;
          align-items: stretch;
          gap: 0.6rem;
          padding: 0.9rem 1rem;
        }

        .item-info {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          flex: 1;
          overflow: hidden;
          width: 100%;
        }

        .item-title {
          font-weight: 600;
          font-size: 0.9rem;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          color: var(--text-primary);
        }

        .folder-icon {
          color: var(--accent-purple);
          flex-shrink: 0;
        }

        .doc-icon {
          width:44px;
          height:44px;
          border-radius:10px;
          display:grid;
          place-items:center;
          flex:0 0 auto;
        }

        .doc-metadata {
          display: flex;
          flex-direction: column;
          overflow: hidden;
          flex: 1;
          min-width: 0;
        }

        .doc-size {
          font-size: 0.75rem;
          color: var(--text-muted);
          margin-top: 0.15rem;
        }

        .appeal-required-badge{display:inline-flex;align-self:flex-start;margin-top:.4rem;padding:.25rem .5rem;border:1px solid rgba(245,158,11,.35);border-radius:999px;background:rgba(245,158,11,.12);color:#fbbf24;font-size:.68rem;font-weight:800}

        .doc-card .card-actions {
          display: flex;
          justify-content: flex-end;
          align-items: center;
          gap: 0.35rem;
          border-top: 1px solid rgba(255, 255, 255, 0.07);
          padding-top: 0.5rem;
          margin-top: 0.2rem;
          width: 100%;
        }

        .delete-item-btn {
          background: transparent;
          border: none;
          color: var(--text-muted);
          cursor: pointer;
          transition: var(--transition-fast);
          padding: 0.4rem;
          border-radius: var(--radius-sm);
        }

        .delete-item-btn:hover {
          color: var(--danger);
          background: rgba(239, 68, 68, 0.08);
        }

        .card-actions {
          display: flex;
          gap: 0.25rem;
        }

        .action-btn {
          background: transparent;
          border: none;
          color: var(--text-muted);
          cursor: pointer;
          transition: var(--transition-fast);
          padding: 0.4rem;
          border-radius: var(--radius-sm);
        }

        .action-btn:hover {
          color: var(--accent-blue);
          background: rgba(0, 180, 216, 0.08);
        }

        .empty-state {
          display: flex;
          flex-direction: column;
          justify-content: center;
          align-items: center;
          height: 100%;
          color: var(--text-muted);
          text-align: center;
          gap: 1rem;
          padding: 3rem 0;
        }

        .empty-icon {
          color: rgba(255, 255, 255, 0.03);
        }

        /* Modal Overlays */
        .modal-overlay {
          position: fixed;
          top: 0;
          left: 0;
          right: 0;
          bottom: 0;
          background: rgba(0, 0, 0, 0.7);
          display: flex;
          justify-content: center;
          align-items: center;
          z-index: 1000;
        }

        .modal-box {
          width: 90%;
          max-width: 450px;
          padding: 2rem;
          border-radius: var(--radius-md);
        }

        .modal-box h3 {
          margin-bottom: 1.25rem;
        }

        .modal-actions {
          display: flex;
          justify-content: flex-end;
          gap: 0.75rem;
          margin-top: 1.5rem;
        }

        .collision-box {
          text-align: center;
          max-width: 500px;
        }

        .collision-header {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.5rem;
          margin-bottom: 1rem;
        }

        .alert-icon {
          color: var(--warning);
        }

        .collision-box p {
          color: var(--text-secondary);
          margin-bottom: 1.5rem;
        }

        .collision-actions {
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }

        .danger-hover:hover {
          border-color: var(--danger) !important;
          color: var(--danger) !important;
          background: rgba(239, 68, 68, 0.05);
        }

        .success-hover:hover {
          border-color: var(--success) !important;
          color: var(--success) !important;
          background: rgba(16, 185, 129, 0.05);
        }

        .loading-container {
          display: flex;
          flex-direction: column;
          gap: 1rem;
          padding: 2rem;
        }

        .skeleton-row {
          height: 48px;
          width: 100%;
        }

        .spin {
          animation: spin 1s linear infinite;
        }
      `}</style>
    </div>
  );
};
