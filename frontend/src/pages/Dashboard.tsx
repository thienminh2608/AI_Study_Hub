import React, { useState, useEffect } from 'react';
import { api } from '../services/api';
import { 
  Folder, 
  FolderOpen,
  FileText, 
  Upload, 
  ChevronRight, 
  Trash2, 
  Eye, 
  AlertTriangle,
  FolderPlus,
  Loader
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';

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
  
  // Modals & Forms
  const [showCreateFolder, setShowCreateFolder] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');
  
  // Upload States
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState('');
  
  // Collision Modal
  const [showCollisionModal, setShowCollisionModal] = useState(false);
  const [pendingDoc, setPendingDoc] = useState<any>(null);
  const [duplicateDocId, setDuplicateDocId] = useState<number | null>(null);
  
  // Loading
  const [loading, setLoading] = useState(true);

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

      // 3. Build Breadcrumbs
      if (currentFolderId) {
        const chain: FolderItem[] = [];
        let curId: number | undefined = currentFolderId;
        while (curId) {
          const folderObj = allFoldersData.find(f => f.folderId === curId);
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
        parentFolderId: currentFolderId
      });
      setNewFolderName('');
      setShowCreateFolder(false);
      loadFolderContent();
    } catch (err: any) {
      alert(err.message || 'Không thể tạo thư mục.');
    }
  };

  const handleDeleteFolder = async (folderId: number) => {
    if (!window.confirm('Cảnh báo: Hành động này sẽ xóa vĩnh viễn thư mục này cùng tất cả tệp tin và thư mục con bên trong! Bạn chắc chắn muốn xóa?')) {
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
  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setUploadProgress('Đang tải file lên máy chủ...');
    try {
      // 1. Send file to backend (temp upload)
      const fileExt = file.name.split('.').pop() || '';
      const response = await api.document.upload(file, currentFolderId);
      
      // 2. Local duplicate check in frontend
      const titleWithoutExt = file.name.substring(0, file.name.lastIndexOf('.')) || file.name;
      const duplicate = documents.find(d => d.title.toLowerCase() === titleWithoutExt.toLowerCase() && d.fileExtension.toLowerCase() === fileExt.toLowerCase());

      if (duplicate) {
        // Name collision detected! Open collision modal
        setPendingDoc({
          pendingDocId: response.documentId,
          title: titleWithoutExt,
          sharingPermission: 'PRIVATE',
          folderId: currentFolderId
        });
        setDuplicateDocId(duplicate.documentId);
        setShowCollisionModal(true);
        setUploading(false);
      } else {
        // No duplicate, proceed to confirm automatically
        setUploadProgress('Đang xử lý nội dung văn bản...');
        await api.document.confirm(response.documentId, titleWithoutExt, 'PRIVATE', currentFolderId);
        loadFolderContent();
        setUploading(false);
      }
    } catch (err: any) {
      alert(err.message || 'Lỗi upload.');
      setUploading(false);
    }
  };

  // Collision Actions
  const handleCollisionReplace = async () => {
    if (!pendingDoc || !duplicateDocId) return;
    setUploading(true);
    setShowCollisionModal(false);
    try {
      await api.document.replace(pendingDoc.pendingDocId, duplicateDocId, pendingDoc.title, pendingDoc.sharingPermission, pendingDoc.folderId);
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
      await api.document.keepBoth(pendingDoc.pendingDocId, pendingDoc.title, pendingDoc.sharingPermission, pendingDoc.folderId);
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
    if (!window.confirm('Bạn có chắc chắn muốn xóa tài liệu này?')) return;
    try {
      await api.document.delete(id);
      loadFolderContent();
    } catch (err: any) {
      alert(err.message || 'Xóa tài liệu thất bại.');
    }
  };

  // Folder tree builder helper
  const renderFolderTree = (parentId: number | undefined = undefined, depth = 0) => {
    const list = allFolders.filter(f => f.parentFolderId === parentId);
    return list.map(f => (
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
      <div className="explorer-layout">
        
        {/* Left Tree Explorer Bar */}
        <aside className="tree-explorer glass-panel">
          <h3>Thư mục của tôi</h3>
          <button className="tree-node root-node" onClick={() => setCurrentFolderId(undefined)}>
            <FolderOpen size={16} />
            <span>Root /</span>
          </button>
          <div className="tree-scroll">
            {renderFolderTree(undefined, 0)}
          </div>
        </aside>

        {/* Right Main explorer pane */}
        <div className="explorer-pane glass-panel">
          
          {/* Action Row */}
          <div className="action-header">
            {/* Breadcrumbs */}
            <div className="breadcrumbs">
              <span onClick={() => setCurrentFolderId(undefined)} className="crumb">Root</span>
              {breadcrumbs.map(crumb => (
                <React.Fragment key={crumb.folderId}>
                  <ChevronRight size={14} className="crumb-arrow" />
                  <span onClick={() => setCurrentFolderId(crumb.folderId)} className="crumb">{crumb.folderName}</span>
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
                  onChange={handleFileUpload} 
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
              {subFolders.length > 0 && (
                <div className="section-block">
                  <h4>Thư mục ({subFolders.length})</h4>
                  <div className="grid-layout">
                    {subFolders.map(folder => (
                      <div key={folder.folderId} className="item-card folder-card glass-card">
                        <div onClick={() => setCurrentFolderId(folder.folderId)} className="item-info">
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
                    {documents.map(doc => (
                      <div key={doc.documentId} className="item-card doc-card glass-card">
                        <div onClick={() => navigate(`/document/${doc.documentId}`)} className="item-info">
                          <FileText size={28} className="doc-icon" />
                          <div className="doc-metadata">
                            <span className="item-title">{doc.title}.{doc.fileExtension}</span>
                            <span className="doc-size">{doc.fileSizeMb.toFixed(2)} MB • {doc.aiParsingStatus}</span>
                          </div>
                        </div>
                        <div className="card-actions">
                          <button onClick={() => navigate(`/document/${doc.documentId}`)} className="action-btn" title="Xem chi tiết">
                            <Eye size={16} />
                          </button>
                          <button onClick={() => handleDeleteDocument(doc.documentId)} className="delete-item-btn" title="Xóa tài liệu">
                            <Trash2 size={16} />
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {subFolders.length === 0 && documents.length === 0 && (
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
                <button type="button" onClick={() => setShowCreateFolder(false)} className="btn-secondary">Hủy</button>
                <button type="submit" className="btn-primary">Tạo mới</button>
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
              <h3>Phát hiện trùng tên file</h3>
            </div>
            <p>
              Tệp tin <strong>{pendingDoc?.title}</strong> đã tồn tại trong thư mục hiện tại. Bạn muốn thực hiện hành động nào?
            </p>
            <div className="collision-actions">
              <button onClick={handleCollisionReplace} className="btn-secondary danger-hover">
                Ghi đè (Replace)
              </button>
              <button onClick={handleCollisionKeepBoth} className="btn-secondary success-hover">
                Giữ cả hai (Keep Both)
              </button>
              <button onClick={handleCollisionCancel} className="btn-primary">
                Hủy tải lên (Cancel)
              </button>
            </div>
          </div>
        </div>
      )}

      <style>{`
        .dashboard-container {
          min-height: 80vh;
        }

        .explorer-layout {
          display: flex;
          gap: 1.5rem;
          height: calc(100vh - 6rem);
        }

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
          color: var(--accent-blue);
          flex-shrink: 0;
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
