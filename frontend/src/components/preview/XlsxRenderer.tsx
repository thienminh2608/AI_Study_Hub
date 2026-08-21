import React, { useEffect, useState, useMemo } from 'react';
import * as XLSX from 'xlsx';
import { Loader, AlertOctagon, AlertTriangle, Table } from 'lucide-react';

interface XlsxRendererProps {
  blob: Blob;
  isFs: boolean;
  zoom: number;
  onDownload?: () => void;
}

const MAX_ROWS = 500;
const MAX_COLS = 50;

export const XlsxRenderer: React.FC<XlsxRendererProps> = ({ blob, zoom, onDownload }) => {
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [workbook, setWorkbook] = useState<XLSX.WorkBook | null>(null);
  const [sheetNames, setSheetNames] = useState<string[]>([]);
  const [activeSheetName, setActiveSheetName] = useState<string>('');

  useEffect(() => {
    let isCancelled = false;
    setLoading(true);
    setError(null);

    blob
      .arrayBuffer()
      .then((buffer) => {
        if (isCancelled) return;
        try {
          const wb = XLSX.read(buffer, { type: 'array' });
          setWorkbook(wb);
          setSheetNames(wb.SheetNames || []);
          if (wb.SheetNames && wb.SheetNames.length > 0) {
            setActiveSheetName(wb.SheetNames[0]);
          }
        } catch {
          if (!isCancelled) {
            setError('Không thể đọc dữ liệu tệp bảng tính Excel (XLSX/XLS).');
          }
        }
      })
      .catch(() => {
        if (!isCancelled) setError('Lỗi khi tải dữ liệu nhị phân của tệp.');
      })
      .finally(() => {
        if (!isCancelled) setLoading(false);
      });

    return () => {
      isCancelled = true;
    };
  }, [blob]);

  const { rows, totalRows, totalCols, isTruncated } = useMemo(() => {
    if (!workbook || !activeSheetName) {
      return { rows: [], totalRows: 0, totalCols: 0, isTruncated: false };
    }
    const sheet = workbook.Sheets[activeSheetName];
    if (!sheet) {
      return { rows: [], totalRows: 0, totalCols: 0, isTruncated: false };
    }

    const rawData = XLSX.utils.sheet_to_json<any[]>(sheet, { header: 1, defval: '' });
    const totalR = rawData.length;
    let maxC = 0;
    for (const r of rawData) {
      if (Array.isArray(r) && r.length > maxC) maxC = r.length;
    }

    const truncated = totalR > MAX_ROWS || maxC > MAX_COLS;
    const sliceRows = rawData.slice(0, MAX_ROWS).map((r) => (Array.isArray(r) ? r.slice(0, MAX_COLS) : []));

    return {
      rows: sliceRows,
      totalRows: totalR,
      totalCols: maxC,
      isTruncated: truncated,
    };
  }, [workbook, activeSheetName]);

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

  if (loading) {
    return (
      <div className="original-preview-state">
        <Loader className="spin" size={28} />
        <p>Đang đọc dữ liệu bảng tính...</p>
      </div>
    );
  }

  if (!workbook || sheetNames.length === 0) {
    return (
      <div className="original-preview-state">
        <Table size={28} />
        <p>Tệp bảng tính không có trang tính (sheet) nào.</p>
      </div>
    );
  }

  return (
    <div className="original-preview-xlsx-wrapper" style={{ width: '100%', height: '100%', display: 'flex', flexDirection: 'column', color: '#111827', background: '#ffffff', colorScheme: 'light' }}>
      {/* Sheet Tabs Bar */}
      <div
        className="xlsx-sheet-tabs"
        style={{
          display: 'flex',
          gap: '4px',
          padding: '8px 12px',
          borderBottom: '1px solid var(--border, #e2e8f0)',
          overflowX: 'auto',
          background: 'var(--bg-card, #f8fafc)',
        }}
      >
        {sheetNames.map((name) => (
          <button
            key={name}
            type="button"
            onClick={() => setActiveSheetName(name)}
            style={{
              padding: '6px 14px',
              borderRadius: '6px',
              border: activeSheetName === name ? '1px solid var(--primary, #3b82f6)' : '1px solid transparent',
              background: activeSheetName === name ? 'var(--primary-light, #eff6ff)' : 'transparent',
              color: activeSheetName === name ? 'var(--primary, #3b82f6)' : 'var(--text-secondary, #64748b)',
              fontWeight: activeSheetName === name ? 600 : 400,
              fontSize: '13px',
              cursor: 'pointer',
              whiteSpace: 'nowrap',
              transition: 'all 0.15s ease',
            }}
          >
            {name}
          </button>
        ))}
      </div>

      {/* Truncation Warning */}
      {isTruncated && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            padding: '6px 12px',
            background: '#fffbeb',
            color: '#b45309',
            fontSize: '12px',
            borderBottom: '1px solid #fef3c7',
          }}
        >
          <AlertTriangle size={14} />
          <span>
            Bảng tính lớn ({totalRows} dòng &times; {totalCols} cột). Đang hiển thị an toàn {Math.min(totalRows, MAX_ROWS)} dòng đầu tiên và {Math.min(totalCols, MAX_COLS)} cột đầu.
          </span>
        </div>
      )}

      {/* Sheet Content Table */}
      <div style={{ flex: 1, overflow: 'auto', padding: '12px' }}>
        <div style={{ zoom }}>
        {rows.length === 0 ? (
          <div className="original-preview-state">
            <p>Trang tính &ldquo;{activeSheetName}&rdquo; không có dữ liệu.</p>
          </div>
        ) : (
          <div style={{ overflowX: 'auto', maxWidth: '100%' }}>
            <table
              style={{
                width: '100%',
                borderCollapse: 'collapse',
                fontSize: '13px',
                textAlign: 'left',
                border: '1px solid var(--border, #e2e8f0)',
              }}
            >
              <tbody>
                {rows.map((row, rIdx) => (
                  <tr
                    key={rIdx}
                    style={{
                      background: rIdx === 0 ? 'var(--bg-card, #f8fafc)' : rIdx % 2 === 0 ? '#ffffff' : '#fafafa',
                      fontWeight: rIdx === 0 ? 600 : 400,
                    }}
                  >
                    <td
                      style={{
                        padding: '6px 10px',
                        border: '1px solid var(--border, #e2e8f0)',
                        background: '#f1f5f9',
                        color: '#64748b',
                        fontSize: '11px',
                        userSelect: 'none',
                        width: '40px',
                        textAlign: 'center',
                      }}
                    >
                      {rIdx + 1}
                    </td>
                    {row.map((cell: any, cIdx: number) => (
                      <td
                        key={cIdx}
                        style={{
                          padding: '6px 10px',
                          border: '1px solid var(--border, #e2e8f0)',
                          background: '#ffffff',
                          color: '#111827',
                          whiteSpace: 'nowrap',
                          maxWidth: '300px',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                        }}
                        title={String(cell ?? '')}
                      >
                        {String(cell ?? '')}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        </div>
      </div>
    </div>
  );
};

export default XlsxRenderer;
