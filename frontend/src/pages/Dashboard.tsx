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
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { FileTypeIcon } from '../components/FileTypeIcon';

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
}

export const Dashboard: React.FC = () => {
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
  });
  const [audienceDetail, setAudienceDetail] = useState<any | null>(null);

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
      alert(err.message || 'Lỗi khi tải tài nguyên.');
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
      alert(err.message || 'Không thể tạo thư mục.');
    }
  };

  const handleDeleteFolder = async (folderId: number) => {
    if (
      !window.confirm(
        'Cảnh báo: Hành động này sẽ xóa vĩnh viễn thư mục này cùng tất cả tệp tin và thư mục con bên trong! Bạn chắc chắn muốn xóa?',
      )
    ) {
      return;
    }

    try {
      await api.folder.delete(folderId);
      loadFolderContent();
    } catch (err: any) {
      alert(err.message || 'Không thể xóa thư mục.');
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
      alert(err.message || 'Lỗi tải tài liệu.');
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
      alert(err.message || 'Ghi đè thất bại.');
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
      alert(err.message || 'Lưu cả hai thất bại.');
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
      alert(err.message || 'Xóa tài liệu thất bại.');
    }
  };

  const handleAskAi = async (doc: DocumentItem) => {
    if (askingDocumentId) return;
    setAskingDocumentId(doc.documentId);
    try {
      const session = await api.chat.createSession({ sessionName: doc.title });
      navigate(`/chat?sessionId=${session.sessionId}&documentId=${doc.documentId}`);
    } catch (err: any) {
      alert(err.message || 'Không thể tạo phiên Hỏi AI.');
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

            {/* Operations */}
            <div className="actions">
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
                        <button
                          onClick={() => handleDeleteFolder(folder.folderId)}
                          className="delete-item-btn"
                          title="Xóa thư mục"
                        >
                          <Trash2 size={16} />
                        </button>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Files Header & Grid */}
              {documents.length > 0 && (
                <div className="section-block" style={{ marginTop: '1.5rem' }}>
                  <h4>Tài liệu ({documents.length})</h4>
                  <div className="grid-layout">
                    {documents.map((doc) => (
                      <div key={doc.documentId} className="item-card doc-card glass-card">
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
                            <span className="item-title">
                              {doc.title}.{doc.fileExtension}
                            </span>
                            <span className="doc-size">
                              {doc.subject || 'Khác'} • {doc.fileSizeMb.toFixed(2)} MB •{' '}
                              {doc.aiParsingStatus}
                            </span>
                          </div>
                        </div>
                        <div className="card-actions">
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

        @media(max-width:768px){.analytics-stats{grid-template-columns:repeat(2,1fr)}.analytics-heading,.audience-row{align-items:flex-start;flex-direction:column}}

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
          grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
          gap: 1rem;
        }

        .item-card {
          display: flex;
          justify-content: space-between;
          align-items: center;
          padding: 0.75rem 1rem;
          cursor: pointer;
        }

        .item-info {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          flex: 1;
          overflow: hidden;
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
        }

        .doc-size {
          font-size: 0.75rem;
          color: var(--text-muted);
          margin-top: 0.15rem;
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
