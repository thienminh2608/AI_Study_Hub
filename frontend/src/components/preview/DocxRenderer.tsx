import React, { useEffect, useRef, useState } from 'react';
import * as docxPreview from 'docx-preview';
import { Loader, AlertOctagon, Info } from 'lucide-react';

interface DocxRendererProps {
  blob: Blob;
  isFs: boolean;
  zoom: number;
  onDownload?: () => void;
}

export const DocxRenderer: React.FC<DocxRendererProps> = ({ blob, zoom, onDownload }) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;
    setLoading(true);
    setError(null);
    containerRef.current.innerHTML = '';

    docxPreview
      .renderAsync(blob, containerRef.current)
      .catch(() => setError('Không thể hiển thị bản xem trước DOCX.'))
      .finally(() => setLoading(false));
  }, [blob]);

  if (error) {
    return (
      <div className="original-preview-state">
        <AlertOctagon size={28} />
        <p>{error}</p>
        {onDownload && (
          <button type="button" className="btn btn-secondary" onClick={onDownload} style={{ marginTop: '8px' }}>
            Tải tệp gốc
          </button>
        )}
      </div>
    );
  }

  return (
    <div className="original-preview-docx-wrapper">
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '8px 12px',
          background: 'var(--bg-card, #f8fafc)',
          borderRadius: '6px',
          fontSize: '13px',
          color: 'var(--text-secondary, #64748b)',
          marginBottom: '12px',
          border: '1px solid var(--border, #e2e8f0)',
        }}
      >
        <Info size={16} />
        <span>Bản xem trước DOCX có thể không hiển thị đầy đủ 100% định dạng phức tạp. Bạn có thể tải tệp gốc để xem chính xác nhất.</span>
      </div>
      {loading && (
        <div className="original-preview-state">
          <Loader className="spin" size={28} />
          <p>Đang dựng giao diện DOCX...</p>
        </div>
      )}
      <div style={{ overflow: 'auto', width: '100%' }}>
        <div
          ref={containerRef}
          className="original-preview-docx-container"
          style={{ zoom }}
        />
      </div>
    </div>
  );
};

export default DocxRenderer;
