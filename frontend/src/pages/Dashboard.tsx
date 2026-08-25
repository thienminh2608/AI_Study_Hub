import React, { useState, useEffect, useCallback, useRef } from 'react';
import { api, type SubjectTreeNode } from '../services/api';
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
  History,
  LayoutGrid,
  List,
  CheckSquare,
  Square,
  Download,
  Eye,
  Users,
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
  uploaderName?: string;
  requiresAppeal?: boolean;
  publicReviewBlocked?: boolean;
  appealStatus?: string;
}

const flattenSubjectNodes = (nodes: SubjectTreeNode[]): SubjectTreeNode[] =>
  nodes.flatMap((node) => [node, ...flattenSubjectNodes(node.children || [])]);

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
  const [pendingSortKey, setPendingSortKey] = useState('title');
  const [pendingSortDirection, setPendingSortDirection] = useState<'asc' | 'desc'>('asc');
  const [sharedWithMe, setSharedWithMe] = useState<DocumentItem[]>([]);

  // Modals & Forms
  const [showCreateFolder, setShowCreateFolder] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');

  // Upload States
  const [uploading, setUploading] = useState(false);
  const [uploadPercent, setUploadPercent] = useState(0);
  const [uploadProgress, setUploadProgress] = useState('');
  const uploadAbortControllerRef = useRef<AbortController | null>(null);
  const pollingTimersRef = useRef<Map<number, ReturnType<typeof setTimeout>>>(new Map());
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
  const [accessModalItem, setAccessModalItem] = useState<{
    type: 'document' | 'folder';
    id: number;
  } | null>(null);
  const [versionModalDocId, setVersionModalDocId] = useState<number | null>(null);

  // Subject Categories
  const [approvedSubjects, setApprovedSubjects] = useState<string[]>([
    'Toán học',
    'Vật lý',
    'Hóa học',
    'Sinh học',
    'Ngữ văn',
    'Tiếng Anh',
    'Tin học',
    'Kinh tế',
    'Kỹ năng mềm',
    'Triết học',
    'Lịch sử',
    'Địa lý',
    'Khác',
  ]);
  const [subjectTree, setSubjectTree] = useState<SubjectTreeNode[]>([]);
  const [selectedRootSubjectId, setSelectedRootSubjectId] = useState<number | null>(null);
  const [customSubjectInput, setCustomSubjectInput] = useState('');
  const selectedUploadRoot =
    subjectTree.find((node) => node.subjectId === selectedRootSubjectId) ?? null;
  const uploadChildSubjects = selectedUploadRoot
    ? flattenSubjectNodes(selectedUploadRoot.children || [])
    : [];

  // Task 16: List/Grid View & Bulk Actions
  const [viewMode, setViewMode] = useState<'grid' | 'list'>(() => {
    return (localStorage.getItem('dashboard-view-mode') as 'grid' | 'list') || 'grid';
  });
  const [selectedDocIds, setSelectedDocIds] = useState<Set<number>>(new Set());
  const [bulkProcessing, setBulkProcessing] = useState(false);
  const [draggedDocIds, setDraggedDocIds] = useState<number[]>([]);
  const [dragOverFolderId, setDragOverFolderId] = useState<number | null>(null);
  const [docSortKey, setDocSortKey] = useState<string>('createdAt');
  const [docSortDirection, setDocSortDirection] = useState<'asc' | 'desc'>('desc');
  const [sharedSortKey, setSharedSortKey] = useState<string>('title');
  const [sharedSortDirection, setSharedSortDirection] = useState<'asc' | 'desc'>('asc');

  const toggleDocSort = (key: string) => {
    if (docSortKey === key) {
      setDocSortDirection((prev) => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setDocSortKey(key);
      setDocSortDirection('asc');
    }
  };

  const sortedDocuments = React.useMemo(() => {
    const list = [...documents];
    if (!docSortKey) return list;
    return list.sort((a, b) => {
      let av: any = (a as any)[docSortKey] ?? '';
      let bv: any = (b as any)[docSortKey] ?? '';
      if (typeof av === 'number' && typeof bv === 'number') {
        return docSortDirection === 'asc' ? av - bv : bv - av;
      }
      const keyLower = docSortKey.toLowerCase();
      if (keyLower.includes('at') || keyLower.includes('date') || keyLower.includes('time')) {
        const at = new Date(av || 0).getTime();
        const bt = new Date(bv || 0).getTime();
        return docSortDirection === 'asc' ? at - bt : bt - at;
      }
      return docSortDirection === 'asc'
        ? String(av).localeCompare(String(bv), 'vi')
        : String(bv).localeCompare(String(av), 'vi');
    });
  }, [documents, docSortKey, docSortDirection]);
  const sortedSharedDocuments = React.useMemo(
    () =>
      [...sharedWithMe].sort((a, b) => {
        const av: any = (a as any)[sharedSortKey] ?? '';
        const bv: any = (b as any)[sharedSortKey] ?? '';
        const result =
          typeof av === 'number' && typeof bv === 'number'
            ? av - bv
            : String(av).localeCompare(String(bv), 'vi', { numeric: true });
        return sharedSortDirection === 'asc' ? result : -result;
      }),
    [sharedWithMe, sharedSortKey, sharedSortDirection],
  );
  const sortedPendingReviews = React.useMemo(
    () =>
      [...(analytics.pendingReviewDocuments ?? [])].sort((a: any, b: any) => {
        const value = (item: any) =>
          pendingSortKey === 'status'
            ? item.moderationStatus || item.status || ''
            : (item[pendingSortKey] ?? '');
        const av = value(a);
        const bv = value(b);
        const result = /At$/.test(pendingSortKey)
          ? new Date(av || 0).getTime() - new Date(bv || 0).getTime()
          : String(av).localeCompare(String(bv), 'vi', { numeric: true });
        return pendingSortDirection === 'asc' ? result : -result;
      }),
    [analytics.pendingReviewDocuments, pendingSortKey, pendingSortDirection],
  );

  const renderDocSortHeader = (key: string, label: string) => (
    <button
      type="button"
      onClick={() => toggleDocSort(key)}
      style={{
        background: 'none',
        border: 'none',
        color: docSortKey === key ? 'var(--accent-blue)' : 'inherit',
        fontWeight: docSortKey === key ? 700 : 'inherit',
        cursor: 'pointer',
        display: 'inline-flex',
        alignItems: 'center',
        gap: '0.25rem',
        fontSize: 'inherit',
        textTransform: 'inherit',
        padding: 0,
      }}
    >
      <span>{label}</span>
      <span>{docSortKey === key ? (docSortDirection === 'asc' ? ' ↑' : ' ↓') : ' ↕'}</span>
    </button>
  );
  const renderSharedSortHeader = (key: string, label: string) => (
    <button
      type="button"
      className="dashboard-sort-header"
      onClick={() => {
        if (sharedSortKey === key)
          setSharedSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
        else {
          setSharedSortKey(key);
          setSharedSortDirection('asc');
        }
      }}
    >
      {label} {sharedSortKey === key ? (sharedSortDirection === 'asc' ? '↑' : '↓') : '↕'}
    </button>
  );
  const renderPendingSortHeader = (key: string, label: string) => (
    <button
      type="button"
      onClick={() => {
        if (pendingSortKey === key)
          setPendingSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
        else {
          setPendingSortKey(key);
          setPendingSortDirection('asc');
        }
      }}
    >
      {label} {pendingSortKey === key ? (pendingSortDirection === 'asc' ? '↑' : '↓') : '↕'}
    </button>
  );

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

  // Background Polling for AI Processing State
  const pollDocumentStatus = useCallback(
    (docId: number) => {
      if (pollingTimersRef.current.has(docId)) return;

      let delay = 2000;
      const maxDelay = 10000;
      const startTime = Date.now();
      const maxDuration = 120000; // 2 minutes timeout

      const checkStatus = async () => {
        if (Date.now() - startTime > maxDuration) {
          pollingTimersRef.current.delete(docId);
          return;
        }

        try {
          const details = await api.document.getById(docId);
          const status = details.aiParsingStatus;

          if (status === 'READY') {
            setDocuments((prev) =>
              prev.map((d) => (d.documentId === docId ? { ...d, aiParsingStatus: 'READY' } : d)),
            );
            notify(`Tài liệu "${details.title}" đã được bóc tách và sẵn sàng cho AI!`, 'success');
            pollingTimersRef.current.delete(docId);
            return;
          }

          if (status === 'FAILED') {
            setDocuments((prev) =>
              prev.map((d) => (d.documentId === docId ? { ...d, aiParsingStatus: 'FAILED' } : d)),
            );
            notify(`Xử lý nội dung AI cho tài liệu "${details.title}" thất bại.`, 'error');
            pollingTimersRef.current.delete(docId);
            return;
          }

          // Non-terminal: update active status in list
          setDocuments((prev) =>
            prev.map((d) => (d.documentId === docId ? { ...d, aiParsingStatus: status } : d)),
          );

          // Schedule next poll with backoff
          delay = Math.min(delay * 1.5, maxDelay);
          const timer = setTimeout(checkStatus, delay);
          pollingTimersRef.current.set(docId, timer);
        } catch {
          // If 401/403/404 or connection loss, stop polling
          pollingTimersRef.current.delete(docId);
        }
      };

      const initialTimer = setTimeout(checkStatus, delay);
      pollingTimersRef.current.set(docId, initialTimer);
    },
    [notify],
  );

  // Load Folder Content
  const loadFolderContent = useCallback(async () => {
    setLoading(true);
    try {
      // 1. Fetch child folders and documents
      const foldersData = await api.folder.getChildFolders(currentFolderId);
      const docsData = await api.document.getUserDocuments(currentFolderId);
      setSubFolders(foldersData);
      setDocuments(docsData as any);

      // Auto-resume background polling for any non-terminal documents after reload
      if (Array.isArray(docsData)) {
        docsData.forEach((d: any) => {
          if (['QUEUED', 'PENDING', 'PROCESSING', 'CHUNKING'].includes(d.aiParsingStatus)) {
            pollDocumentStatus(d.documentId);
          }
        });
      }

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
  }, [currentFolderId, notify, pollDocumentStatus]);

  useEffect(() => {
    loadFolderContent();
  }, [loadFolderContent]);

  const startDocumentDrag = (event: React.DragEvent, documentId: number) => {
    const ids = selectedDocIds.has(documentId) ? Array.from(selectedDocIds) : [documentId];
    setDraggedDocIds(ids);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('application/x-ai-study-hub-document-ids', JSON.stringify(ids));
    event.dataTransfer.setData('text/plain', String(documentId));
  };

  const dropDocumentsIntoFolder = async (event: React.DragEvent, folder?: FolderItem) => {
    event.preventDefault();
    event.stopPropagation();
    setDragOverFolderId(null);
    let ids = draggedDocIds;
    try {
      const payload = event.dataTransfer.getData('application/x-ai-study-hub-document-ids');
      if (payload) ids = JSON.parse(payload);
    } catch {
      // Use the in-memory drag payload when browser dataTransfer parsing fails.
    }
    if (!ids.length) return;
    if ((folder?.folderId ?? undefined) === currentFolderId) {
      notify('Tài liệu đã nằm trong thư mục này.', 'info');
      setDraggedDocIds([]);
      return;
    }
    setBulkProcessing(true);
    try {
      await api.documentExtra.bulkMove(ids, folder?.folderId ?? null);
      setSelectedDocIds(new Set());
      notify(
        `Đã chuyển ${ids.length} tài liệu vào ${folder ? `thư mục “${folder.folderName}”` : 'thư mục gốc'}.`,
        'success',
      );
      await loadFolderContent();
    } catch (err: any) {
      notify(err.message || 'Không thể chuyển tài liệu vào thư mục.', 'error');
    } finally {
      setBulkProcessing(false);
      setDraggedDocIds([]);
    }
  };

  // Cleanup active polling timers and abort ongoing uploads on unmount
  useEffect(() => {
    const activeTimers = pollingTimersRef.current;
    return () => {
      activeTimers.forEach((timer) => clearTimeout(timer));
      activeTimers.clear();
      if (uploadAbortControllerRef.current) {
        uploadAbortControllerRef.current.abort();
      }
    };
  }, []);

  useEffect(() => {
    Promise.all([api.subjects.getApproved(), api.subjects.getTree('APPROVED')])
      .then(([list, tree]) => {
        if (list && list.length > 0) {
          const names = list.map((s) => s.name);
          if (!names.includes('Khác')) names.push('Khác');
          setApprovedSubjects(names);
        }
        if (Array.isArray(tree)) {
          setSubjectTree(tree);
          const other = tree.find((node) => node.name === 'Khác');
          setSelectedRootSubjectId(
            (current) => current ?? other?.subjectId ?? tree[0]?.subjectId ?? null,
          );
        }
      })
      .catch(() => {});
  }, []);

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
    setCustomSubjectInput('');
    setSelectedRootSubjectId(
      subjectTree.find((node) => node.name === 'Khác')?.subjectId ??
        subjectTree[0]?.subjectId ??
        null,
    );
    setUploadDraft({ file, title, subject: 'Khác', sharingPermission: 'PRIVATE' });
    e.target.value = '';
  };

  const handleConfirmUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!uploadDraft || !uploadDraft.title.trim() || !uploadDraft.subject.trim()) return;
    const { file, subject, sharingPermission } = uploadDraft;
    const finalTitle = uploadDraft.title.trim();

    setUploading(true);
    setUploadPercent(0);
    setUploadProgress('Đang tải file lên máy chủ (0%)...');

    const controller = new AbortController();
    uploadAbortControllerRef.current = controller;

    try {
      const fileExt = file.name.split('.').pop() || '';
      const response = await api.document.upload(
        file,
        currentFolderId,
        (pct) => {
          setUploadPercent(pct);
          setUploadProgress(`Đang tải file lên máy chủ (${pct}%)...`);
        },
        controller.signal,
      );

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
        setUploadProgress('Đang xếp hàng xử lý AI...');
        await api.document.confirm(
          response.documentId,
          finalTitle,
          subject,
          sharingPermission,
          currentFolderId,
        );
        notify(
          `Đã tải lên tài liệu "${finalTitle}". Hệ thống đang xử lý văn bản ở chế độ nền.`,
          'success',
        );
        await loadFolderContent();
        pollDocumentStatus(response.documentId);
        setUploading(false);
      }
    } catch (err: any) {
      if (controller.signal.aborted) {
        notify('Đã hủy tải lên tài liệu.', 'info');
      } else {
        notify(err.message || 'Lỗi tải tài liệu.', 'error');
      }
      setUploading(false);
    } finally {
      uploadAbortControllerRef.current = null;
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
      const session = await api.chat.createSession({
        sessionName: doc.title,
        documentId: doc.documentId,
      });
      navigate(`/chat?sessionId=${session.sessionId}`);
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
          className={`tree-node ${currentFolderId === f.folderId ? 'active' : ''} ${dragOverFolderId === f.folderId ? 'document-drop-target' : ''}`}
          onClick={() => setCurrentFolderId(f.folderId)}
          onDragEnter={(event) => {
            event.preventDefault();
            setDragOverFolderId(f.folderId);
          }}
          onDragOver={(event) => {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
          }}
          onDragLeave={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node))
              setDragOverFolderId(null);
          }}
          onDrop={(event) => dropDocumentsIntoFolder(event, f)}
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
          <button
            className={`tree-node root-node ${dragOverFolderId === -1 ? 'document-drop-target' : ''}`}
            onClick={() => setCurrentFolderId(undefined)}
            onDragEnter={(event) => {
              event.preventDefault();
              setDragOverFolderId(-1);
            }}
            onDragOver={(event) => {
              event.preventDefault();
              event.dataTransfer.dropEffect = 'move';
            }}
            onDragLeave={(event) => {
              if (!event.currentTarget.contains(event.relatedTarget as Node))
                setDragOverFolderId(null);
            }}
            onDrop={(event) => dropDocumentsIntoFolder(event)}
          >
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
            <div
              className="actions"
              style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}
            >
              <div
                className="glass-card flex items-center p-1 rounded-lg"
                style={{ display: 'flex', gap: '0.2rem', padding: '0.25rem' }}
              >
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
            <div
              className="glass-card p-3 my-3 flex items-center justify-between rounded-xl"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '0.75rem 1rem',
                margin: '0.75rem 0',
                background: 'rgba(30, 41, 59, 0.9)',
                border: '1px solid rgba(255,255,255,0.15)',
                borderRadius: '12px',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                <button
                  onClick={toggleSelectAllDocs}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.4rem',
                    background: 'none',
                    border: 'none',
                    color: '#e2e8f0',
                    cursor: 'pointer',
                  }}
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
                  style={{
                    padding: '0.4rem 0.8rem',
                    fontSize: '0.85rem',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.3rem',
                  }}
                >
                  <Download size={15} />
                  <span>Tải về ({selectedDocIds.size})</span>
                </button>
                <button
                  onClick={handleBulkDelete}
                  disabled={bulkProcessing}
                  className="btn-danger"
                  style={{
                    padding: '0.4rem 0.8rem',
                    fontSize: '0.85rem',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.3rem',
                  }}
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
            <div
              className="upload-loader glass-card"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: '12px',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                <Loader size={20} className="spin" />
                <span>{uploadProgress}</span>
              </div>
              {uploadPercent < 100 && (
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => uploadAbortControllerRef.current?.abort()}
                  style={{
                    padding: '4px 12px',
                    fontSize: '12px',
                    color: '#ef4444',
                    borderColor: 'rgba(239, 68, 68, 0.3)',
                    background: 'rgba(239, 68, 68, 0.1)',
                  }}
                >
                  Hủy tải lên
                </button>
              )}
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
                    {subFolders.map((folder) => (
                      <div
                        key={folder.folderId}
                        className={`item-card folder-card glass-card ${dragOverFolderId === folder.folderId ? 'document-drop-target' : ''}`}
                        onDragEnter={(event) => {
                          event.preventDefault();
                          setDragOverFolderId(folder.folderId);
                        }}
                        onDragOver={(event) => {
                          event.preventDefault();
                          event.dataTransfer.dropEffect = 'move';
                        }}
                        onDragLeave={(event) => {
                          if (!event.currentTarget.contains(event.relatedTarget as Node))
                            setDragOverFolderId(null);
                        }}
                        onDrop={(event) => dropDocumentsIntoFolder(event, folder)}
                      >
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
                            title="Chia sẻ & Quản lý quyền thư mục"
                          >
                            <Share2 size={16} />
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
                    <div
                      className="glass-card overflow-hidden my-3"
                      style={{ borderRadius: '12px', border: '1px solid rgba(255,255,255,0.1)' }}
                    >
                      <table
                        style={{
                          width: '100%',
                          borderCollapse: 'collapse',
                          textAlign: 'left',
                          fontSize: '0.9rem',
                        }}
                      >
                        <thead>
                          <tr
                            style={{
                              background: 'rgba(15, 23, 42, 0.7)',
                              color: '#94a3b8',
                              borderBottom: '1px solid rgba(255,255,255,0.1)',
                              fontSize: '0.8rem',
                              textTransform: 'uppercase',
                            }}
                          >
                            <th
                              style={{ padding: '0.75rem', width: '2.5rem', textAlign: 'center' }}
                            >
                              <button
                                onClick={toggleSelectAllDocs}
                                style={{
                                  background: 'none',
                                  border: 'none',
                                  color: 'inherit',
                                  cursor: 'pointer',
                                }}
                              >
                                {selectedDocIds.size > 0 &&
                                selectedDocIds.size === documents.length ? (
                                  <CheckSquare size={16} style={{ color: '#60a5fa' }} />
                                ) : (
                                  <Square size={16} />
                                )}
                              </button>
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderDocSortHeader('title', 'Tài liệu')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderDocSortHeader('subject', 'Môn học')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderDocSortHeader('fileSizeMb', 'Dung lượng')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderDocSortHeader('aiParsingStatus', 'Trạng thái AI')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderDocSortHeader('createdAt', 'Ngày tạo')}
                            </th>
                            <th style={{ padding: '0.75rem', textAlign: 'right' }}>Thao tác</th>
                          </tr>
                        </thead>
                        <tbody>
                          {sortedDocuments.map((doc) => {
                            const isSelected = selectedDocIds.has(doc.documentId);
                            return (
                              <tr
                                key={doc.documentId}
                                draggable={!bulkProcessing}
                                onDragStart={(event) => startDocumentDrag(event, doc.documentId)}
                                onDragEnd={() => {
                                  setDraggedDocIds([]);
                                  setDragOverFolderId(null);
                                }}
                                className="draggable-document"
                                style={{
                                  borderBottom: '1px solid rgba(255,255,255,0.05)',
                                  background: isSelected
                                    ? 'rgba(59, 130, 246, 0.1)'
                                    : 'transparent',
                                }}
                              >
                                <td style={{ padding: '0.75rem', textAlign: 'center' }}>
                                  <button
                                    onClick={() => toggleSelectDoc(doc.documentId)}
                                    style={{
                                      background: 'none',
                                      border: 'none',
                                      cursor: 'pointer',
                                      color: 'inherit',
                                    }}
                                  >
                                    {isSelected ? (
                                      <CheckSquare size={16} style={{ color: '#60a5fa' }} />
                                    ) : (
                                      <Square size={16} style={{ color: '#64748b' }} />
                                    )}
                                  </button>
                                </td>
                                <td style={{ padding: '0.75rem', fontWeight: 500 }}>
                                  <div
                                    style={{
                                      display: 'flex',
                                      alignItems: 'center',
                                      gap: '0.5rem',
                                      cursor: 'pointer',
                                    }}
                                    onClick={() => navigate(`/document/${doc.documentId}`)}
                                  >
                                    <FileTypeIcon extension={doc.fileExtension} size={22} />
                                    <span style={{ textDecoration: 'underline' }}>
                                      {getCleanTitle(doc.title, doc.fileExtension)}.
                                      {doc.fileExtension}
                                    </span>
                                  </div>
                                </td>
                                <td style={{ padding: '0.75rem', color: '#94a3b8' }}>
                                  {doc.subject || 'Khác'}
                                </td>
                                <td style={{ padding: '0.75rem', color: '#94a3b8' }}>
                                  {doc.fileSizeMb.toFixed(2)} MB
                                </td>
                                <td style={{ padding: '0.75rem' }}>
                                  <span
                                    style={{
                                      padding: '0.2rem 0.5rem',
                                      borderRadius: '4px',
                                      fontSize: '0.75rem',
                                      background: 'rgba(51, 65, 85, 0.6)',
                                      color: '#cbd5e1',
                                    }}
                                  >
                                    {doc.aiParsingStatus}
                                  </span>
                                </td>
                                <td
                                  style={{
                                    padding: '0.75rem',
                                    color: '#94a3b8',
                                    fontSize: '0.8rem',
                                  }}
                                >
                                  {doc.createdAt ? formatDateTime(doc.createdAt) : '-'}
                                </td>
                                <td style={{ padding: '0.75rem', textAlign: 'right' }}>
                                  <div
                                    style={{
                                      display: 'flex',
                                      alignItems: 'center',
                                      justifyContent: 'flex-end',
                                      gap: '0.3rem',
                                    }}
                                  >
                                    <button
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        setAccessModalItem({
                                          type: 'document',
                                          id: doc.documentId,
                                        });
                                      }}
                                      className="action-btn"
                                      title="Chia sẻ & Quản lý quyền"
                                    >
                                      <Share2 size={15} />
                                    </button>
                                    <button
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        setVersionModalDocId(doc.documentId);
                                      }}
                                      className="action-btn"
                                      title="Lịch sử phiên bản"
                                    >
                                      <History size={15} />
                                    </button>
                                    <button
                                      onClick={() => handleAskAi(doc)}
                                      className="action-btn ask-ai-btn"
                                      title="Hỏi AI"
                                      disabled={askingDocumentId === doc.documentId}
                                    >
                                      {askingDocumentId === doc.documentId ? (
                                        <Loader className="spin" size={15} />
                                      ) : (
                                        <Bot size={15} />
                                      )}
                                    </button>
                                    <button
                                      onClick={() => setDeleteDocumentTarget(doc)}
                                      className="delete-item-btn"
                                      title="Xóa tài liệu"
                                    >
                                      <Trash2 size={15} />
                                    </button>
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
                      {sortedDocuments.map((doc) => {
                        const isSelected = selectedDocIds.has(doc.documentId);
                        return (
                          <div
                            key={doc.documentId}
                            draggable={!bulkProcessing}
                            onDragStart={(event) => startDocumentDrag(event, doc.documentId)}
                            onDragEnd={() => {
                              setDraggedDocIds([]);
                              setDragOverFolderId(null);
                            }}
                            className={`item-card doc-card glass-card draggable-document ${isSelected ? 'selected-card' : ''}`}
                            style={
                              isSelected
                                ? {
                                    border: '1px solid #60a5fa',
                                    background: 'rgba(59, 130, 246, 0.08)',
                                  }
                                : {}
                            }
                          >
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                toggleSelectDoc(doc.documentId);
                              }}
                              style={{
                                position: 'absolute',
                                top: '0.6rem',
                                right: '0.6rem',
                                background: 'none',
                                border: 'none',
                                cursor: 'pointer',
                                zIndex: 2,
                              }}
                              title="Chọn tài liệu"
                            >
                              {isSelected ? (
                                <CheckSquare size={18} style={{ color: '#60a5fa' }} />
                              ) : (
                                <Square size={18} style={{ color: '#64748b' }} />
                              )}
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
                                <span
                                  className="item-title"
                                  title={`${getCleanTitle(doc.title, doc.fileExtension)}.${doc.fileExtension}`}
                                >
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
                                title="Chia sẻ & Quản lý quyền (Share & Access)"
                              >
                                <Share2 size={16} />
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

                  {viewMode === 'list' ? (
                    <div
                      className="glass-card overflow-hidden my-3"
                      style={{ borderRadius: '12px', border: '1px solid rgba(255,255,255,0.1)' }}
                    >
                      <table
                        style={{
                          width: '100%',
                          borderCollapse: 'collapse',
                          textAlign: 'left',
                          fontSize: '0.9rem',
                        }}
                      >
                        <thead>
                          <tr
                            style={{
                              background: 'rgba(15, 23, 42, 0.7)',
                              color: '#94a3b8',
                              borderBottom: '1px solid rgba(255,255,255,0.1)',
                              fontSize: '0.8rem',
                              textTransform: 'uppercase',
                            }}
                          >
                            <th style={{ padding: '0.75rem' }}>
                              {renderSharedSortHeader('title', 'Tài liệu')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderSharedSortHeader('uploaderName', 'Người chia sẻ')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderSharedSortHeader('subject', 'Môn học')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderSharedSortHeader('fileSizeMb', 'Dung lượng')}
                            </th>
                            <th style={{ padding: '0.75rem' }}>
                              {renderSharedSortHeader('aiParsingStatus', 'Trạng thái AI')}
                            </th>
                            <th style={{ padding: '0.75rem', textAlign: 'right' }}>Thao tác</th>
                          </tr>
                        </thead>
                        <tbody>
                          {sortedSharedDocuments.map((doc) => (
                            <tr
                              key={`shared-list-${doc.documentId}`}
                              style={{ borderBottom: '1px solid rgba(255,255,255,0.05)' }}
                            >
                              <td style={{ padding: '0.75rem', fontWeight: 500 }}>
                                <div
                                  style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '0.5rem',
                                    cursor: 'pointer',
                                  }}
                                  onClick={() => navigate(`/document/${doc.documentId}`)}
                                >
                                  <FileTypeIcon extension={doc.fileExtension} size={22} />
                                  <span style={{ textDecoration: 'underline' }}>
                                    {getCleanTitle(doc.title, doc.fileExtension)}.
                                    {doc.fileExtension}
                                  </span>
                                </div>
                              </td>
                              <td
                                style={{
                                  padding: '0.75rem',
                                  color: 'var(--accent-blue)',
                                  fontSize: '0.85rem',
                                }}
                              >
                                <span
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '0.3rem',
                                  }}
                                >
                                  <Users size={14} /> {doc.uploaderName || 'Bạn bè'}
                                </span>
                              </td>
                              <td style={{ padding: '0.75rem', color: '#94a3b8' }}>
                                {doc.subject || 'Khác'}
                              </td>
                              <td style={{ padding: '0.75rem', color: '#94a3b8' }}>
                                {doc.fileSizeMb ? doc.fileSizeMb.toFixed(2) : '0.00'} MB
                              </td>
                              <td style={{ padding: '0.75rem' }}>
                                <span
                                  style={{
                                    padding: '0.2rem 0.5rem',
                                    borderRadius: '4px',
                                    fontSize: '0.75rem',
                                    background: 'rgba(51, 65, 85, 0.6)',
                                    color: '#cbd5e1',
                                  }}
                                >
                                  {doc.aiParsingStatus || 'READY'}
                                </span>
                              </td>
                              <td style={{ padding: '0.75rem', textAlign: 'right' }}>
                                <div
                                  style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'flex-end',
                                    gap: '0.3rem',
                                  }}
                                >
                                  <button
                                    onClick={() => handleAskAi(doc)}
                                    className="action-btn ask-ai-btn"
                                    title="Hỏi AI về tài liệu"
                                    disabled={askingDocumentId === doc.documentId}
                                  >
                                    {askingDocumentId === doc.documentId ? (
                                      <Loader className="spin" size={15} />
                                    ) : (
                                      <Bot size={15} />
                                    )}
                                  </button>
                                  <button
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setVersionModalDocId(doc.documentId);
                                    }}
                                    className="action-btn"
                                    title="Lịch sử phiên bản"
                                  >
                                    <History size={15} />
                                  </button>
                                  {doc.cloudStorageUrl && (
                                    <button
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        window.open(doc.cloudStorageUrl, '_blank');
                                      }}
                                      className="action-btn"
                                      title="Tải xuống tài liệu"
                                    >
                                      <Download size={15} />
                                    </button>
                                  )}
                                  <button
                                    onClick={() => navigate(`/document/${doc.documentId}`)}
                                    className="action-btn"
                                    title="Xem chi tiết tài liệu"
                                  >
                                    <Eye size={15} />
                                  </button>
                                </div>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div className="grid-layout">
                      {sortedSharedDocuments.map((doc) => (
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
                              <span
                                className="item-title"
                                title={`${getCleanTitle(doc.title, doc.fileExtension)}.${doc.fileExtension}`}
                              >
                                {getCleanTitle(doc.title, doc.fileExtension)}.{doc.fileExtension}
                              </span>
                              <span className="doc-size">
                                {doc.subject || 'Khác'} • {(doc.fileSizeMb || 0).toFixed(2)} MB •{' '}
                                {doc.aiParsingStatus || 'READY'}
                              </span>
                              <span className="shared-badge">
                                <Users size={12} />{' '}
                                {doc.uploaderName
                                  ? `Chia sẻ bởi ${doc.uploaderName}`
                                  : 'Được bạn bè chia sẻ'}
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
                              onClick={(e) => {
                                e.stopPropagation();
                                setVersionModalDocId(doc.documentId);
                              }}
                              className="action-btn"
                              title="Lịch sử phiên bản (Versioning)"
                            >
                              <History size={16} />
                            </button>
                            {doc.cloudStorageUrl && (
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  window.open(doc.cloudStorageUrl, '_blank');
                                }}
                                className="action-btn"
                                title="Tải xuống tài liệu"
                              >
                                <Download size={16} />
                              </button>
                            )}
                            <button
                              onClick={() => navigate(`/document/${doc.documentId}`)}
                              className="action-btn"
                              title="Xem chi tiết tài liệu"
                            >
                              <Eye size={16} />
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
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
                Môn học chính
                <select
                  className="input-control"
                  value={selectedRootSubjectId ?? ''}
                  onChange={(e) => {
                    const root = subjectTree.find(
                      (node) => node.subjectId === Number(e.target.value),
                    );
                    setSelectedRootSubjectId(root?.subjectId ?? null);
                    if (root) setUploadDraft({ ...uploadDraft, subject: root.name });
                  }}
                >
                  {subjectTree.map((root) => (
                    <option key={root.subjectId} value={root.subjectId}>
                      {root.name}
                    </option>
                  ))}
                </select>
              </label>
              {uploadChildSubjects.length > 0 && selectedUploadRoot && (
                <label>
                  Chuyên mục môn học
                  <select
                    className="input-control"
                    value={
                      uploadChildSubjects.some((child) => child.name === uploadDraft.subject)
                        ? uploadDraft.subject
                        : ''
                    }
                    onChange={(e) =>
                      setUploadDraft({
                        ...uploadDraft,
                        subject: e.target.value || selectedUploadRoot.name,
                      })
                    }
                  >
                    <option value="">Không có chuyên mục</option>
                    {uploadChildSubjects.map((child) => (
                      <option key={child.subjectId} value={child.name}>
                        {'— '.repeat(Math.max(0, child.depth - selectedUploadRoot.depth - 1))}
                        {child.name}
                      </option>
                    ))}
                  </select>
                  <small style={{ color: 'var(--text-muted)' }}>
                    Chỉ dùng để phân loại; tài liệu vẫn được lưu trong folder bạn đã chọn.
                  </small>
                </label>
              )}
              {(selectedUploadRoot?.name === 'Khác' ||
                !approvedSubjects.includes(uploadDraft.subject)) && (
                <label style={{ marginTop: '0.4rem' }}>
                  <span style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
                    Nhập tên môn học khác{' '}
                    <small style={{ color: 'var(--text-muted)' }}>
                      (Hệ thống sẽ đối soát hoặc gửi kiểm duyệt nếu mới)
                    </small>
                  </span>
                  <input
                    type="text"
                    className="input-control"
                    placeholder="Ví dụ: Đại số tuyến tính, Trí tuệ nhân tạo..."
                    value={customSubjectInput}
                    onChange={(e) => {
                      const val = e.target.value;
                      setCustomSubjectInput(val);
                      setUploadDraft({ ...uploadDraft, subject: val.trim() || 'Khác' });
                    }}
                    maxLength={100}
                  />
                </label>
              )}
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
                    <th>{renderPendingSortHeader('title', 'Tên tài liệu')}</th>
                    <th>{renderPendingSortHeader('status', 'Trạng thái')}</th>
                    <th>{renderPendingSortHeader('moderationSubmittedAt', 'Ngày yêu cầu')}</th>
                    <th>{renderPendingSortHeader('moderatedAt', 'Ngày xét duyệt')}</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedPendingReviews.map((doc: any) => (
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

        .draggable-document { cursor: grab; }
        .dashboard-sort-header { border:0;background:transparent;color:inherit;font:inherit;text-transform:inherit;cursor:pointer;padding:0; }
        .dashboard-sort-header:hover { color:var(--accent-blue); }
        .draggable-document:active { cursor: grabbing; opacity: .72; }
        .folder-card.document-drop-target {
          border-color: var(--accent-blue) !important;
          background: rgba(0, 180, 216, .14) !important;
          box-shadow: 0 0 0 3px rgba(0, 180, 216, .12), 0 12px 30px rgba(0, 0, 0, .25);
          transform: translateY(-2px) scale(1.01);
        }
        .folder-card.document-drop-target .folder-icon { color: var(--accent-blue); }
        .tree-node.document-drop-target { color:var(--accent-blue);background:rgba(0,180,216,.16);box-shadow:inset 0 0 0 1px rgba(0,180,216,.5); }

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
          position: relative;
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
        .shared-badge {
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          color: var(--accent-blue);
          font-size: 0.72rem;
          font-weight: 500;
          margin-top: 0.35rem;
          background: rgba(0, 180, 216, 0.08);
          border: 1px solid rgba(0, 180, 216, 0.2);
          padding: 0.15rem 0.5rem;
          border-radius: 6px;
          width: fit-content;
        }

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
