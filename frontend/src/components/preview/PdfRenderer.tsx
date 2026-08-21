import React, { useState, useEffect } from 'react';
import { Document, Page, pdfjs } from 'react-pdf';
import { Loader, AlertOctagon, ChevronLeft, ChevronRight } from 'lucide-react';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import 'react-pdf/dist/Page/TextLayer.css';

pdfjs.GlobalWorkerOptions.workerSrc = new URL(
  'pdfjs-dist/build/pdf.worker.min.mjs',
  import.meta.url,
).toString();

interface PdfRendererProps {
  fileUrl: string;
  zoom: number;
  isFs: boolean;
  highlightPage?: number | null;
  onDownload?: () => void;
}

export const PdfRenderer: React.FC<PdfRendererProps> = ({
  fileUrl,
  zoom,
  isFs,
  highlightPage,
  onDownload,
}) => {
  const [numPages, setNumPages] = useState<number>(0);
  const [pageNumber, setPageNumber] = useState<number>(highlightPage && highlightPage > 0 ? highlightPage : 1);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (highlightPage && highlightPage > 0) {
      setPageNumber(highlightPage);
    }
  }, [highlightPage]);

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
    <div className="original-preview-pdf">
      <Document
        file={fileUrl}
        onLoadSuccess={({ numPages: n }) => setNumPages(n)}
        onLoadError={() => setError('Không thể hiển thị tệp PDF. Tệp có thể bị mã hóa hoặc lỗi cấu trúc.')}
        loading={
          <div className="original-preview-state">
            <Loader className="spin" size={28} />
          </div>
        }
      >
        <Page
          pageNumber={pageNumber}
          scale={isFs ? zoom * 1.3 : zoom}
          renderTextLayer
          renderAnnotationLayer
        />
      </Document>
      {numPages > 1 && (
        <div className="original-preview-pager">
          <button
            type="button"
            disabled={pageNumber <= 1}
            onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
          >
            <ChevronLeft size={16} />
          </button>
          <span>
            Trang {pageNumber}/{numPages}
          </span>
          <button
            type="button"
            disabled={pageNumber >= numPages}
            onClick={() => setPageNumber((p) => Math.min(numPages, p + 1))}
          >
            <ChevronRight size={16} />
          </button>
        </div>
      )}
    </div>
  );
};

export default PdfRenderer;
