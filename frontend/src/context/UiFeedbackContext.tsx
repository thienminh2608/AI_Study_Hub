import React, { createContext, useCallback, useContext, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react';

type FeedbackKind = 'success' | 'error' | 'info';
type Toast = { id: number; message: string; kind: FeedbackKind };
type ConfirmOptions = { title?: string; message: string; confirmLabel?: string; danger?: boolean };

interface UiFeedbackValue {
  notify: (message: string, kind?: FeedbackKind) => void;
  confirm: (options: ConfirmOptions | string) => Promise<boolean>;
}

const UiFeedbackContext = createContext<UiFeedbackValue | undefined>(undefined);

export const UiFeedbackProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [confirmation, setConfirmation] = useState<ConfirmOptions | null>(null);
  const resolver = useRef<((value: boolean) => void) | null>(null);
  const nextId = useRef(1);

  const notify = useCallback((message: string, kind: FeedbackKind = 'info') => {
    const id = nextId.current++;
    setToasts((current) => [...current, { id, message, kind }]);
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 4500);
  }, []);

  const confirm = useCallback((options: ConfirmOptions | string) => {
    setConfirmation(typeof options === 'string' ? { message: options } : options);
    return new Promise<boolean>((resolve) => {
      resolver.current = resolve;
    });
  }, []);

  const resolveConfirmation = (value: boolean) => {
    resolver.current?.(value);
    resolver.current = null;
    setConfirmation(null);
  };

  return (
    <UiFeedbackContext.Provider value={{ notify, confirm }}>
      {children}
      <div className="app-toast-stack" aria-live="polite" aria-atomic="true">
        {toasts.map((toast) => {
          const Icon =
            toast.kind === 'success' ? CheckCircle2 : toast.kind === 'error' ? XCircle : Info;
          return (
            <div
              key={toast.id}
              className={`app-toast ${toast.kind}`}
              role={toast.kind === 'error' ? 'alert' : 'status'}
            >
              <Icon size={20} />
              <span>{toast.message}</span>
              <button
                aria-label="Đóng thông báo"
                onClick={() =>
                  setToasts((current) => current.filter((item) => item.id !== toast.id))
                }
              >
                <X size={16} />
              </button>
            </div>
          );
        })}
      </div>
      {confirmation && (
        <div
          className="app-confirm-overlay"
          role="presentation"
          onMouseDown={() => resolveConfirmation(false)}
        >
          <section
            className="app-confirm-modal glass-panel"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="app-confirm-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <span className={`app-confirm-icon ${confirmation.danger ? 'danger' : ''}`}>
              <AlertTriangle size={24} />
            </span>
            <div>
              <h3 id="app-confirm-title">{confirmation.title || 'Xác nhận thao tác'}</h3>
              <p>{confirmation.message}</p>
            </div>
            <footer>
              <button className="btn-secondary" onClick={() => resolveConfirmation(false)}>
                Hủy
              </button>
              <button
                className={confirmation.danger ? 'btn-danger' : 'btn-primary'}
                onClick={() => resolveConfirmation(true)}
              >
                {confirmation.confirmLabel || 'Xác nhận'}
              </button>
            </footer>
          </section>
        </div>
      )}
    </UiFeedbackContext.Provider>
  );
};

// oxlint-disable-next-line react/only-export-components
export const useUiFeedback = () => {
  const value = useContext(UiFeedbackContext);
  if (!value) throw new Error('useUiFeedback must be used within UiFeedbackProvider');
  return value;
};
