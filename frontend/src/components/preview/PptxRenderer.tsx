import React, { useEffect, useRef, useState } from 'react';
import { AlertOctagon, ChevronLeft, ChevronRight, Download, Loader } from 'lucide-react';

interface PptxRendererProps {
  blob: Blob;
  isFs: boolean;
  zoom: number;
  onDownload?: () => void;
}

interface PptxPreviewer {
  preview(file: ArrayBuffer): Promise<unknown>;
  destroy(): void;
}

const SLIDE_WIDTH = 960;
const SLIDE_HEIGHT = 540;

const PptxRenderer: React.FC<PptxRendererProps> = ({ blob, isFs, zoom, onDownload }) => {
  const renderHostRef = useRef<HTMLDivElement>(null);
  const previewerRef = useRef<PptxPreviewer | null>(null);
  const [slideCount, setSlideCount] = useState(0);
  const [activeIndex, setActiveIndex] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    const host = renderHostRef.current;
    if (!host) return undefined;

    setLoading(true);
    setError('');
    setSlideCount(0);
    setActiveIndex(0);
    previewerRef.current?.destroy();
    previewerRef.current = null;
    host.replaceChildren();

    const renderPresentation = async () => {
      try {
        const [{ init }, buffer] = await Promise.all([import('pptx-preview'), blob.arrayBuffer()]);
        if (cancelled) return;

        const previewer = init(host, { width: SLIDE_WIDTH, height: SLIDE_HEIGHT }) as PptxPreviewer;
        previewerRef.current = previewer;
        await previewer.preview(buffer);
        if (cancelled) return;

        const slides = host.querySelectorAll<HTMLElement>('.pptx-preview-slide-wrapper');
        if (slides.length === 0) throw new Error('Không tìm thấy slide có thể hiển thị.');

        const wrapper = host.querySelector<HTMLElement>('.pptx-preview-wrapper');
        if (wrapper) {
          wrapper.style.width = `${SLIDE_WIDTH}px`;
          wrapper.style.height = `${SLIDE_HEIGHT}px`;
          wrapper.style.minHeight = `${SLIDE_HEIGHT}px`;
          wrapper.style.overflow = 'visible';
          wrapper.style.margin = '0';
          wrapper.style.background = 'transparent';
        }
        slides.forEach((slide, index) => {
          slide.style.display = index === 0 ? 'block' : 'none';
          slide.style.margin = '0';
        });
        setSlideCount(slides.length);
      } catch {
        if (!cancelled) {
          setError('Không thể dựng bản trình chiếu. Tệp có thể bị hỏng, được mã hóa hoặc chứa thành phần chưa được hỗ trợ.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    void renderPresentation();

    return () => {
      cancelled = true;
      previewerRef.current?.destroy();
      previewerRef.current = null;
      host.replaceChildren();
    };
  }, [blob]);

  useEffect(() => {
    const host = renderHostRef.current;
    if (!host || slideCount === 0) return;
    host.querySelectorAll<HTMLElement>('.pptx-preview-slide-wrapper').forEach((slide, index) => {
      slide.style.display = index === activeIndex ? 'block' : 'none';
    });
  }, [activeIndex, slideCount]);

  const safeZoom = Math.max(0.25, zoom);
  const scaledWidth = SLIDE_WIDTH * safeZoom;
  const scaledHeight = SLIDE_HEIGHT * safeZoom;

  return (
    <div
      style={{
        width: '100%',
        minHeight: isFs ? 'calc(100vh - 130px)' : 520,
        display: 'flex',
        flexDirection: 'column',
        gap: 12,
        colorScheme: 'light',
      }}
    >
      {!error && slideCount > 0 && (
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12 }}>
          <button type="button" className="zoom-btn" disabled={activeIndex === 0} onClick={() => setActiveIndex((value) => value - 1)} aria-label="Slide trước">
            <ChevronLeft size={18} />
          </button>
          <span style={{ color: 'var(--text-secondary)', fontSize: 13 }}>Slide {activeIndex + 1} / {slideCount}</span>
          <button type="button" className="zoom-btn" disabled={activeIndex === slideCount - 1} onClick={() => setActiveIndex((value) => value + 1)} aria-label="Slide sau">
            <ChevronRight size={18} />
          </button>
        </div>
      )}

      <div
        style={{
          position: 'relative',
          flex: 1,
          minHeight: isFs ? 'calc(100vh - 190px)' : 460,
          overflow: 'auto',
          padding: 16,
        }}
      >
        <div
          style={{
            width: scaledWidth,
            height: scaledHeight,
            margin: safeZoom <= 1 ? 'auto' : '0',
            position: 'relative',
            boxShadow: loading || error ? 'none' : '0 8px 30px rgba(0,0,0,.35)',
            transition: 'width 160ms ease, height 160ms ease',
          }}
        >
          <div
            ref={renderHostRef}
            style={{
              width: SLIDE_WIDTH,
              height: SLIDE_HEIGHT,
              transform: `scale(${safeZoom})`,
              transformOrigin: 'top left',
              transition: 'transform 160ms ease',
            }}
          />
        </div>

        {loading && (
          <div className="original-preview-state" style={{ position: 'absolute', inset: 0 }}>
            <Loader className="spin" size={28} />
            <p>Đang dựng nội dung trình chiếu...</p>
          </div>
        )}
        {!loading && error && (
          <div className="original-preview-state" style={{ position: 'absolute', inset: 0 }}>
            <AlertOctagon size={28} />
            <p>{error}</p>
            {onDownload && (
              <button type="button" className="btn btn-secondary" onClick={onDownload}>
                <Download size={16} /> Tải PPTX gốc
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export default PptxRenderer;
