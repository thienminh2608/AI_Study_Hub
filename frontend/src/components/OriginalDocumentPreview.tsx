import React, { useEffect, useState, Suspense } from 'react';
import { createPortal } from 'react-dom';
import {
  Loader,
  AlertOctagon,
  Download,
  Maximize2,
  ExternalLink,
  ZoomIn,
  ZoomOut,
  RotateCcw,
  X,
} from 'lucide-react';
import { api } from '../services/api';

const PdfRenderer = React.lazy(() => import('./preview/PdfRenderer'));
const DocxRenderer = React.lazy(() => import('./preview/DocxRenderer'));
const XlsxRenderer = React.lazy(() => import('./preview/XlsxRenderer'));
const PptxRenderer = React.lazy(() => import('./preview/PptxRenderer'));

interface Props {
  documentId: number;
  fileExtension: string;
  shareToken?: string;
  evidenceReportId?: number;
  highlightPage?: number | null;
  onDownload?: () => void;
  showToolbar?: boolean;
}

export const OriginalDocumentPreview: React.FC<Props> = ({
  documentId,
  fileExtension,
  shareToken,
  evidenceReportId,
  highlightPage,
  onDownload,
  showToolbar = true,
}) => {
  const ext = fileExtension.toLowerCase();
  const [blob, setBlob] = useState<Blob | null>(null);
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [zoom, setZoom] = useState<number>(1);
  const [textContent, setTextContent] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    setError('');
    setBlob(null);
    setTextContent(null);
    setZoom(1);
    const fetchPromise = evidenceReportId
      ? api.moderation.getReportRawEvidence(evidenceReportId)
      : shareToken
      ? api.document.getRawFileByShareToken(shareToken)
      : api.document.getRawFile(documentId);

    fetchPromise
      .then((b: Blob) => setBlob(b))
      .catch(() => setError('Không thể tải file gốc để xem trước.'))
      .finally(() => setLoading(false));
  }, [documentId, shareToken, evidenceReportId]);

  useEffect(() => {
    if (!blob) {
      setFileUrl(null);
      return;
    }
    if (ext === 'pdf' || ['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg'].includes(ext)) {
      const url = URL.createObjectURL(blob);
      setFileUrl(url);
      return () => URL.revokeObjectURL(url);
    }
    if (['txt', 'md', 'csv', 'json'].includes(ext)) {
      blob
        .text()
        .then((text) => setTextContent(text))
        .catch(() => setError('Không thể đọc nội dung file text.'));
    }
  }, [blob, ext]);

  // Escape key handler for fullscreen
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isFullscreen) {
        setIsFullscreen(false);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isFullscreen]);

  const handleOpenRawTab = () => {
    if (!blob) return;
    const url = URL.createObjectURL(blob);
    window.open(url, '_blank');
  };

  const handleZoomIn = () => {
    setZoom((prev) => Math.min(2.5, Math.round((prev + 0.15) * 100) / 100));
  };

  const handleZoomOut = () => {
    setZoom((prev) => Math.max(0.4, Math.round((prev - 0.15) * 100) / 100));
  };

  const handleZoomReset = () => {
    setZoom(1);
  };

  if (loading) {
    return (
      <div className="original-preview-state">
        <Loader className="spin" size={28} />
        <p>Đang tải bản xem trước tệp gốc...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="original-preview-state">
        <AlertOctagon size={28} />
        <p>{error}</p>
        {onDownload && (
          <button type="button" className="btn btn-secondary" onClick={onDownload} style={{ marginTop: '8px' }}>
            <Download size={16} /> Tải tệp gốc
          </button>
        )}
      </div>
    );
  }

  if (!blob) return null;

  const renderContent = (isFs: boolean) => {
    if (ext === 'pdf' && fileUrl) {
      return (
        <Suspense
          fallback={
            <div className="original-preview-state">
              <Loader className="spin" size={28} />
              <p>Đang tải trình đọc PDF...</p>
            </div>
          }
        >
          <PdfRenderer
            fileUrl={fileUrl}
            zoom={zoom}
            isFs={isFs}
            highlightPage={highlightPage}
            onDownload={onDownload}
          />
        </Suspense>
      );
    }

    if (['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg'].includes(ext) && fileUrl) {
      return (
        <div
          className="original-preview-image"
          style={{
            textAlign: 'center',
            padding: '1rem',
            zoom: zoom,
          }}
        >
          <img
            src={fileUrl}
            alt="Bản xem trước hình ảnh gốc"
            style={{
              maxWidth: '100%',
              maxHeight: isFs ? '85vh' : '520px',
              objectFit: 'contain',
              borderRadius: '8px',
              boxShadow: '0 4px 20px rgba(0,0,0,0.4)',
            }}
          />
        </div>
      );
    }

    if (['txt', 'md', 'csv', 'json'].includes(ext) && textContent !== null) {
      return (
        <div
          className="original-preview-text"
          style={{
            padding: '1rem',
            overflowY: 'auto',
            zoom: zoom,
            fontSize: `${0.86 * zoom}rem`,
            lineHeight: 1.65,
          }}
        >
          <pre
            style={{
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              fontFamily: 'monospace',
            }}
          >
            {textContent}
          </pre>
        </div>
      );
    }

    if (ext === 'docx') {
      return (
        <Suspense
          fallback={
            <div className="original-preview-state">
              <Loader className="spin" size={28} />
              <p>Đang tải trình đọc Word (DOCX)...</p>
            </div>
          }
        >
          <DocxRenderer blob={blob} isFs={isFs} zoom={zoom} onDownload={onDownload} />
        </Suspense>
      );
    }

    if (ext === 'xlsx' || ext === 'xls') {
      return (
        <Suspense
          fallback={
            <div className="original-preview-state">
              <Loader className="spin" size={28} />
              <p>Đang tải trình đọc Excel (XLSX)...</p>
            </div>
          }
        >
          <XlsxRenderer blob={blob} isFs={isFs} zoom={zoom} onDownload={onDownload} />
        </Suspense>
      );
    }

    if (ext === 'pptx') {
      return (
        <Suspense
          fallback={
            <div className="original-preview-state">
              <Loader className="spin" size={28} />
              <p>Đang tải trình đọc PowerPoint (PPTX)...</p>
            </div>
          }
        >
          <PptxRenderer blob={blob} isFs={isFs} zoom={zoom} onDownload={onDownload} />
        </Suspense>
      );
    }

    return (
      <div className="original-preview-state">
        <AlertOctagon size={28} />
        <p>
          Chưa hỗ trợ xem trước trực tiếp cho định dạng .{ext}. Vui lòng tải xuống để xem tệp gốc đầy đủ.
        </p>
        {onDownload && (
          <button type="button" className="btn btn-secondary" onClick={onDownload} style={{ marginTop: '8px' }}>
            <Download size={16} /> Tải xuống
          </button>
        )}
      </div>
    );
  };

  const zoomControls = (
    <div className="preview-zoom-group">
      <button
        type="button"
        className="zoom-btn"
        onClick={handleZoomOut}
        title="Thu nhỏ"
        disabled={zoom <= 0.4}
      >
        <ZoomOut size={13} />
      </button>
      <button
        type="button"
        className="zoom-percent-badge"
        onClick={handleZoomReset}
        title="Bấm để đặt lại 100%"
      >
        {Math.round(zoom * 100)}%
      </button>
      <button
        type="button"
        className="zoom-btn"
        onClick={handleZoomIn}
        title="Phóng to"
        disabled={zoom >= 2.5}
      >
        <ZoomIn size={13} />
      </button>
      {zoom !== 1 && (
        <button
          type="button"
          className="zoom-btn"
          onClick={handleZoomReset}
          title="Đặt lại 100%"
        >
          <RotateCcw size={12} />
        </button>
      )}
    </div>
  );

  return (
    <>
      {/* Normal Embedded View */}
      <div className="original-preview-wrapper">
        {showToolbar && (
          <div className="original-preview-top-toolbar">
            <div className="toolbar-left">
              <span className="file-badge-tag">{ext.toUpperCase()}</span>
              {zoomControls}
            </div>
            <div className="toolbar-btn-group">
              <button
                type="button"
                className="toolbar-btn"
                onClick={handleOpenRawTab}
                title="Mở tệp gốc trực tiếp trong tab mới của trình duyệt"
              >
                <ExternalLink size={13} />
                <span>Mở tab tệp gốc</span>
              </button>
              <button
                type="button"
                className="toolbar-btn highlight"
                onClick={() => setIsFullscreen(true)}
                title="Xem toàn màn hình không bị giới hạn"
              >
                <Maximize2 size={13} />
                <span>Toàn màn hình</span>
              </button>
              {onDownload && (
                <button
                  type="button"
                  className="toolbar-btn"
                  onClick={onDownload}
                  title="Tải tệp gốc về máy"
                >
                  <Download size={13} />
                </button>
              )}
            </div>
          </div>
        )}

        <div className="original-preview-body-scroll custom-scroll">
          {renderContent(false)}
        </div>
      </div>

      {/* Fullscreen Modal View rendered via Portal onto document.body */}
      {isFullscreen &&
        createPortal(
          <div
            className="original-preview-fullscreen-portal animate-fade-in"
            onKeyDown={(e) => e.key === 'Escape' && setIsFullscreen(false)}
          >
            {/* Top Bar for Fullscreen */}
            <div className="fullscreen-topbar">
              <div className="fullscreen-left">
                <span className="file-badge-tag">{ext.toUpperCase()}</span>
                <span className="fullscreen-title">BẢN XEM TRƯỚC TỆP GỐC TOÀN MÀN HÌNH</span>
                {zoomControls}
              </div>
              <div className="fullscreen-right">
                <button
                  type="button"
                  className="fullscreen-action-btn"
                  onClick={handleOpenRawTab}
                  title="Mở tệp gốc trực tiếp trong tab mới"
                >
                  <ExternalLink size={14} />
                  <span>Mở tab mới</span>
                </button>
                {onDownload && (
                  <button
                    type="button"
                    className="fullscreen-action-btn"
                    onClick={onDownload}
                    title="Tải về máy"
                  >
                    <Download size={14} />
                    <span>Tải về</span>
                  </button>
                )}
                <button
                  type="button"
                  className="fullscreen-close-btn"
                  onClick={() => setIsFullscreen(false)}
                  title="Đóng chế độ toàn màn hình (Phím ESC)"
                >
                  <X size={18} />
                  <span>Đóng toàn màn hình (ESC)</span>
                </button>
              </div>
            </div>

            {/* Fullscreen Body Scroll */}
            <div className="fullscreen-body-scroll custom-scroll">
              {renderContent(true)}
            </div>
          </div>,
          document.body,
        )}

      <style>{`
        .original-preview-wrapper {
          display: flex;
          flex-direction: column;
          width: 100%;
          height: 100%;
          min-height: 0;
          border-radius: 8px;
          overflow: hidden;
          background: rgba(0, 0, 0, 0.25);
          border: 1px solid rgba(255, 255, 255, 0.08);
        }
        .original-preview-top-toolbar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 0.55rem 0.75rem;
          background: rgba(18, 26, 42, 0.95);
          backdrop-filter: blur(10px);
          border-bottom: 1px solid rgba(255, 255, 255, 0.08);
          gap: 0.5rem;
          flex-wrap: wrap;
          position: sticky;
          top: 0;
          z-index: 20;
        }
        .toolbar-left {
          display: flex;
          align-items: center;
          gap: 0.6rem;
        }
        .file-badge-tag {
          font-size: 0.72rem;
          font-weight: 800;
          padding: 0.2rem 0.5rem;
          border-radius: 4px;
          background: rgba(0, 180, 216, 0.15);
          color: var(--accent-blue);
          letter-spacing: 0.05em;
        }
        .preview-zoom-group {
          display: inline-flex;
          align-items: center;
          gap: 0.2rem;
          background: rgba(0, 0, 0, 0.35);
          padding: 0.15rem 0.3rem;
          border-radius: 6px;
          border: 1px solid rgba(255, 255, 255, 0.1);
        }
        .zoom-btn {
          border: 0;
          background: transparent;
          color: var(--text-secondary);
          display: grid;
          place-items: center;
          padding: 0.25rem 0.35rem;
          border-radius: 4px;
          cursor: pointer;
          transition: all 0.15s ease;
        }
        .zoom-btn:hover:not(:disabled) {
          color: #fff;
          background: rgba(255, 255, 255, 0.12);
        }
        .zoom-btn:disabled {
          opacity: 0.3;
          cursor: not-allowed;
        }
        .zoom-percent-badge {
          border: 0;
          background: transparent;
          color: var(--text-primary);
          font-size: 0.72rem;
          font-weight: 700;
          padding: 0.1rem 0.35rem;
          cursor: pointer;
          min-width: 40px;
          text-align: center;
        }
        .zoom-percent-badge:hover {
          color: var(--accent-blue);
        }
        .toolbar-btn-group {
          display: flex;
          align-items: center;
          gap: 0.4rem;
        }
        .toolbar-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          padding: 0.32rem 0.6rem;
          font-size: 0.74rem;
          font-weight: 650;
          border-radius: 6px;
          border: 1px solid rgba(255, 255, 255, 0.12);
          background: rgba(255, 255, 255, 0.04);
          color: var(--text-secondary);
          cursor: pointer;
          transition: all 0.18s ease;
        }
        .toolbar-btn:hover {
          color: #fff;
          border-color: rgba(255, 255, 255, 0.25);
          background: rgba(255, 255, 255, 0.09);
        }
        .toolbar-btn.highlight {
          color: var(--accent-blue);
          border-color: rgba(0, 180, 216, 0.35);
          background: rgba(0, 180, 216, 0.1);
        }
        .toolbar-btn.highlight:hover {
          background: rgba(0, 180, 216, 0.2);
          border-color: var(--accent-blue);
        }
        .original-preview-body-scroll {
          overflow-y: auto;
          overflow-x: auto;
          padding: 0.75rem;
          flex: 1;
          min-height: 0;
        }

        /* Fullscreen Portal Styling */
        .original-preview-fullscreen-portal {
          position: fixed !important;
          inset: 0 !important;
          width: 100vw !important;
          height: 100vh !important;
          z-index: 999999 !important;
          background: rgba(8, 12, 22, 0.97) !important;
          backdrop-filter: blur(16px) !important;
          display: flex !important;
          flex-direction: column !important;
          box-sizing: border-box !important;
        }
        .fullscreen-topbar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 0.75rem 1.5rem;
          background: rgba(255, 255, 255, 0.04);
          border-bottom: 1px solid rgba(255, 255, 255, 0.1);
          gap: 1rem;
          flex-shrink: 0;
        }
        .fullscreen-left {
          display: flex;
          align-items: center;
          gap: 1rem;
        }
        .fullscreen-title {
          font-size: 0.82rem;
          font-weight: 700;
          color: var(--text-secondary);
          letter-spacing: 0.05em;
        }
        .fullscreen-right {
          display: flex;
          align-items: center;
          gap: 0.6rem;
        }
        .fullscreen-action-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.4rem;
          padding: 0.45rem 0.85rem;
          font-size: 0.8rem;
          font-weight: 650;
          border-radius: 7px;
          border: 1px solid rgba(255, 255, 255, 0.15);
          background: rgba(255, 255, 255, 0.06);
          color: var(--text-primary);
          cursor: pointer;
          transition: all 0.18s ease;
        }
        .fullscreen-action-btn:hover {
          background: rgba(255, 255, 255, 0.12);
        }
        .fullscreen-close-btn {
          display: inline-flex;
          align-items: center;
          gap: 0.45rem;
          padding: 0.45rem 1rem;
          font-size: 0.82rem;
          font-weight: 700;
          border-radius: 7px;
          border: 1px solid rgba(239, 68, 68, 0.4);
          background: rgba(239, 68, 68, 0.15);
          color: #fca5a5;
          cursor: pointer;
          transition: all 0.18s ease;
        }
        .fullscreen-close-btn:hover {
          background: rgba(239, 68, 68, 0.3);
          border-color: #ef4444;
          color: #fff;
        }
        .fullscreen-body-scroll {
          flex: 1;
          overflow-y: auto;
          padding: 2rem;
          display: flex;
          justify-content: center;
        }

        /* DOCX styling with native zoom */
        .original-preview-docx-wrapper {
          width: 100%;
        }
        .original-preview-docx-container {
          width: 100%;
          background: #f1f5f9;
          color: #111;
          padding: 1rem;
          border-radius: 6px;
          overflow: auto;
          box-sizing: border-box;
          transform-origin: top center;
        }
        .original-preview-docx-container .docx-wrapper {
          background: transparent !important;
          padding: 0 !important;
        }
        .original-preview-docx-container .docx-wrapper > section.docx {
          margin: 0 auto 1.5rem !important;
          box-shadow: 0 4px 18px rgba(0, 0, 0, 0.25) !important;
          background: #ffffff !important;
          box-sizing: border-box !important;
        }

        /* PDF styling with scale */
        .original-preview-pdf {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.75rem;
          width: 100%;
        }
        .original-preview-pdf .react-pdf__Page__canvas {
          margin: 0 auto;
          border-radius: 6px;
          box-shadow: 0 4px 16px rgba(0, 0, 0, 0.35);
        }
      `}</style>
    </>
  );
};
