import React, { useEffect, useRef, useState } from 'react';
import { Document, Page, pdfjs } from 'react-pdf';
import * as docxPreview from 'docx-preview';
import * as XLSX from 'xlsx';
import { Loader, ChevronLeft, ChevronRight, AlertOctagon, Download } from 'lucide-react';
import { api } from '../services/api';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import 'react-pdf/dist/Page/TextLayer.css';

pdfjs.GlobalWorkerOptions.workerSrc = new URL(
  'pdfjs-dist/build/pdf.worker.min.mjs',
  import.meta.url,
).toString();

interface Props {
  documentId: number;
  fileExtension: string;
  highlightPage?: number | null;
  onDownload?: () => void;
}

export const OriginalDocumentPreview: React.FC<Props> = ({
  documentId,
  fileExtension,
  highlightPage,
  onDownload,
}) => {
  const ext = fileExtension.toLowerCase();
  const [blob, setBlob] = useState<Blob | null>(null);
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [numPages, setNumPages] = useState(0);
  const [pageNumber, setPageNumber] = useState(1);
  const docxContainerRef = useRef<HTMLDivElement | null>(null);
  const xlsxContainerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setLoading(true);
    setError('');
    setBlob(null);
    setNumPages(0);
    api.document
      .getRawFile(documentId)
      .then((b) => setBlob(b))
      .catch(() => setError('Không thể tải file gốc để xem trước.'))
      .finally(() => setLoading(false));
  }, [documentId]);

  useEffect(() => {
    if (!blob || ext !== 'pdf') {
      setFileUrl(null);
      return;
    }
    const url = URL.createObjectURL(blob);
    setFileUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [blob, ext]);

  useEffect(() => {
    if (highlightPage && highlightPage > 0) setPageNumber(highlightPage);
  }, [highlightPage]);

  useEffect(() => {
    if (ext !== 'docx' || !blob || !docxContainerRef.current) return;
    docxContainerRef.current.innerHTML = '';
    docxPreview
      .renderAsync(blob, docxContainerRef.current)
      .catch(() => setError('Không thể hiển thị bản xem trước DOCX.'));
  }, [ext, blob]);

  useEffect(() => {
    if (ext !== 'xlsx' || !blob || !xlsxContainerRef.current) return;
    blob.arrayBuffer().then((buffer) => {
      try {
        const workbook = XLSX.read(buffer, { type: 'array' });
        const firstSheetName = workbook.SheetNames[0];
        const html = XLSX.utils.sheet_to_html(workbook.Sheets[firstSheetName]);
        if (xlsxContainerRef.current) xlsxContainerRef.current.innerHTML = html;
      } catch {
        setError('Không thể hiển thị bản xem trước XLSX.');
      }
    });
  }, [ext, blob]);

  if (loading) {
    return (
      <div className="original-preview-state">
        <Loader className="spin" size={28} />
        <p>Đang tải bản xem trước...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="original-preview-state">
        <AlertOctagon size={28} />
        <p>{error}</p>
      </div>
    );
  }

  if (!blob) return null;

  if (ext === 'pdf' && fileUrl) {
    return (
      <div className="original-preview-pdf">
        <Document
          file={fileUrl}
          onLoadSuccess={({ numPages: n }) => setNumPages(n)}
          loading={
            <div className="original-preview-state">
              <Loader className="spin" size={28} />
            </div>
          }
          error={
            <div className="original-preview-state">
              <AlertOctagon size={28} />
              <p>Không thể hiển thị file PDF.</p>
            </div>
          }
        >
          <Page pageNumber={pageNumber} renderTextLayer renderAnnotationLayer />
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
  }

  if (ext === 'docx') {
    return <div className="original-preview-docx" ref={docxContainerRef} />;
  }

  if (ext === 'xlsx') {
    return <div className="original-preview-xlsx" ref={xlsxContainerRef} />;
  }

  return (
    <div className="original-preview-state">
      <AlertOctagon size={28} />
      <p>
        Chưa hỗ trợ xem trước bản gốc cho định dạng .{ext}. Vui lòng tải xuống để xem nội dung đầy
        đủ.
      </p>
      {onDownload && (
        <button type="button" className="btn-secondary" onClick={onDownload}>
          <Download size={16} /> Tải xuống
        </button>
      )}
    </div>
  );
};
