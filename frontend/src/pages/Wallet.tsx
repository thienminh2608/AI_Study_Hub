import React, { useState, useEffect, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { formatDateTime } from '../utils/dateTime';
import { Loader, X } from 'lucide-react';

const DEPOSIT_AMOUNTS = [20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000];

interface Transaction {
  transactionId: number;
  amount: number;
  type: string; // "DEPOSIT" | "WITHDRAW"
  status: string; // "PENDING" | "SUCCESS" | "CANCELLED"
  startedAt?: string;
  completedAt?: string;
}

interface TransferConfiguration {
  bankCode?: string;
  bankName?: string;
  accountNumber?: string;
  accountName?: string;
  qrTemplate?: string;
  transferContentPrefix?: string;
  isActive: boolean;
}

export const Wallet: React.FC = () => {
  const { user, refreshUser } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);

  // Forms
  const [amount, setAmount] = useState<number | null>(null);
  const [showDepositModal, setShowDepositModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [transferConfig, setTransferConfig] = useState<TransferConfiguration | null>(null);
  const [sortKey, setSortKey] = useState<keyof Transaction>('startedAt');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const sortedTransactions = useMemo(
    () =>
      [...transactions].sort((left, right) => {
        const a = left[sortKey] ?? '',
          b = right[sortKey] ?? '';
        const result =
          typeof a === 'number' && typeof b === 'number'
            ? a - b
            : String(a).localeCompare(String(b), 'vi');
        return sortDirection === 'asc' ? result : -result;
      }),
    [transactions, sortKey, sortDirection],
  );
  const sortHeader = (key: keyof Transaction, label: string) => (
    <button
      className={`sortable-header ${sortKey === key ? 'active' : ''}`}
      onClick={() => {
        if (sortKey === key) setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
        else {
          setSortKey(key);
          setSortDirection('asc');
        }
      }}
    >
      {label}
      <span aria-hidden="true">
        {sortKey === key ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : ' ↕'}
      </span>
    </button>
  );

  const loadTransactions = async () => {
    try {
      const data = await api.transaction.getUserTransactions();
      setTransactions(data);
    } catch (err: any) {
      console.error('Failed to load transactions:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadTransactions();
    api.transaction
      .getTransferConfig()
      .then(setTransferConfig)
      .catch(() => setTransferConfig({ isActive: false }));
  }, []);

  useEffect(() => {
    if (searchParams.get('deposit') !== '1') return;

    setError('');
    setSuccess('');
    setAmount(null);
    setShowDepositModal(true);

    const nextParams = new URLSearchParams(searchParams);
    nextParams.delete('deposit');
    setSearchParams(nextParams, { replace: true });
  }, [searchParams, setSearchParams]);

  const transferContent =
    `${transferConfig?.transferContentPrefix || 'AIStudyHub'} ${user?.username || ''}`.trim();
  const qrUrl =
    amount && transferConfig?.isActive && transferConfig.bankCode && transferConfig.accountNumber
      ? `https://img.vietqr.io/image/${encodeURIComponent(transferConfig.bankCode)}-${encodeURIComponent(transferConfig.accountNumber)}-${encodeURIComponent(transferConfig.qrTemplate || 'compact2')}.png?amount=${amount}&addInfo=${encodeURIComponent(transferContent)}&accountName=${encodeURIComponent(transferConfig.accountName || '')}`
      : '';

  const handleSubmitTransaction = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    if (!amount || !DEPOSIT_AMOUNTS.includes(amount)) {
      setError('Vui lòng chọn một mệnh giá nạp tiền.');
      return;
    }

    setSubmitting(true);
    try {
      await api.transaction.create({
        amount,
        type: 'DEPOSIT',
      });
      setSuccess(
        `Đã gửi yêu cầu nạp ${amount.toLocaleString('vi-VN')}đ. Vui lòng đợi Quản trị viên phê duyệt.`,
      );
      setAmount(null);
      setShowDepositModal(false);
      await loadTransactions();
      await refreshUser();
    } catch (err: any) {
      setError(err.message || 'Tạo giao dịch thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  const renderStatusBadge = (status: string) => {
    switch (status) {
      case 'SUCCESS':
        return (
          <span className="status-badge success">
            <span>Thành công</span>
          </span>
        );
      case 'CANCELLED':
        return (
          <span className="status-badge danger">
            <span>Đã hủy</span>
          </span>
        );
      default:
        return (
          <span className="status-badge warning">
            <span>Đang chờ duyệt</span>
          </span>
        );
    }
  };

  return (
    <div className="wallet-container">
      <div className="wallet-overview animate-slide-up">
        <div className="balance-card glass-panel">
          <div className="card-glow"></div>
          <div className="balance-content">
            <div>
              <div className="balance-header">
                <span>Số dư khả dụng</span>
              </div>
              <h2>{(user?.balance || 0).toLocaleString('vi-VN')}đ</h2>
              <p>
                Tài khoản thành viên: <strong>{user?.tierName || 'Free'}</strong>
              </p>
            </div>
            <button
              type="button"
              className="btn-primary deposit-button"
              onClick={() => {
                setError('');
                setSuccess('');
                setAmount(null);
                setShowDepositModal(true);
              }}
            >
              Nạp tiền
            </button>
          </div>
        </div>
        {success && <div className="success-alert wallet-alert">{success}</div>}
      </div>

      {showDepositModal && (
        <div
          className="deposit-modal-overlay"
          onMouseDown={() => !submitting && setShowDepositModal(false)}
        >
          <div
            className="deposit-modal glass-panel animate-slide-up"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <button
              className="deposit-modal-close"
              onClick={() => setShowDepositModal(false)}
              disabled={submitting}
              aria-label="Đóng"
            >
              <X size={20} />
            </button>
            <h3>Nạp tiền vào ví</h3>
            <p className="deposit-modal-subtitle">Chọn mệnh giá bạn muốn nạp</p>
            {error && <div className="error-alert">{error}</div>}
            <form onSubmit={handleSubmitTransaction}>
              <div className="deposit-amount-grid">
                {DEPOSIT_AMOUNTS.map((value) => (
                  <button
                    key={value}
                    type="button"
                    className={`deposit-amount-option ${amount === value ? 'selected' : ''}`}
                    onClick={() => {
                      setAmount(value);
                      setError('');
                    }}
                    disabled={submitting}
                  >
                    <span>{value.toLocaleString('vi-VN')}</span>
                    <small>VNĐ</small>
                  </button>
                ))}
              </div>
              <div className="deposit-summary">
                <span>Số tiền nạp</span>
                <strong>{amount ? `${amount.toLocaleString('vi-VN')}đ` : 'Chưa chọn'}</strong>
              </div>
              {amount &&
                (transferConfig?.isActive && qrUrl ? (
                  <section className="transfer-qr-panel" aria-live="polite">
                    <img
                      src={qrUrl}
                      alt={`Mã QR chuyển khoản ${amount.toLocaleString('vi-VN')} đồng`}
                    />
                    <div className="transfer-details">
                      <strong>{transferConfig.bankName || transferConfig.bankCode}</strong>
                      <span>
                        Số tài khoản: <b>{transferConfig.accountNumber}</b>
                      </span>
                      <span>
                        Chủ tài khoản: <b>{transferConfig.accountName}</b>
                      </span>
                      <span>
                        Nội dung: <b>{transferContent}</b>
                      </span>
                      <small>Quét QR đúng mệnh giá rồi gửi yêu cầu để Admin xác nhận.</small>
                    </div>
                  </section>
                ) : (
                  <div className="error-alert">
                    Admin chưa bật cấu hình chuyển khoản. Vui lòng thử lại sau.
                  </div>
                ))}
              <button
                type="submit"
                className="btn-primary tx-submit"
                disabled={submitting || !amount || !transferConfig?.isActive}
              >
                {submitting ? <Loader className="spin" size={18} /> : 'Xác nhận nạp tiền'}
              </button>
            </form>
          </div>
        </div>
      )}

      {/* Historic Transaction Logs */}
      <div className="history-pane glass-panel animate-slide-up" style={{ marginTop: '2rem' }}>
        <h3>Lịch sử giao dịch</h3>

        <div className="table-scroll">
          {loading ? (
            <div className="history-loader">
              <Loader className="spin" size={32} />
              <p>Đang tải lịch sử giao dịch...</p>
            </div>
          ) : transactions.length === 0 ? (
            <div className="empty-history">
              <p>Chưa có giao dịch nào được thực hiện.</p>
            </div>
          ) : (
            <table className="tx-table">
              <thead>
                <tr>
                  <th>{sortHeader('transactionId', 'Mã giao dịch')}</th>
                  <th>{sortHeader('type', 'Loại')}</th>
                  <th>{sortHeader('amount', 'Số tiền')}</th>
                  <th>{sortHeader('status', 'Trạng thái')}</th>
                  <th>{sortHeader('startedAt', 'Thời gian khởi tạo')}</th>
                  <th>{sortHeader('completedAt', 'Hoàn thành lúc')}</th>
                </tr>
              </thead>
              <tbody>
                {sortedTransactions.map((tx) => {
                  const isDeposit = tx.type === 'DEPOSIT' || tx.amount > 0;
                  return (
                    <tr key={tx.transactionId}>
                      <td className="tx-id">#{tx.transactionId}</td>
                      <td>
                        <span className={`type-tag ${isDeposit ? 'deposit' : 'withdraw'}`}>
                          {isDeposit ? 'Nạp tiền' : 'Rút tiền'}
                        </span>
                      </td>
                      <td className={`tx-amount ${isDeposit ? 'positive' : 'negative'}`}>
                        {isDeposit ? '+' : ''}
                        {tx.amount.toLocaleString()}đ
                      </td>
                      <td>{renderStatusBadge(tx.status)}</td>
                      <td>{formatDateTime(tx.startedAt)}</td>
                      <td>{formatDateTime(tx.completedAt)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <style>{`
        .wallet-container {
          min-height: 80vh;
        }

        .wallet-overview { display: flex; flex-direction: column; gap: 1rem; }

        .balance-card {
          padding: 2.5rem;
          display: flex;
          justify-content: center;
          position: relative;
          overflow: hidden;
          border-radius: var(--radius-md);
        }

        .balance-content { position: relative; z-index: 1; display: flex; justify-content: space-between; align-items: center; gap: 2rem; }
        .deposit-button { min-width: 150px; height: 48px; display: flex; align-items: center; justify-content: center; gap: .5rem; }

        .card-glow {
          position: absolute;
          width: 150px;
          height: 150px;
          background: var(--accent-purple);
          filter: blur(80px);
          top: -20px;
          right: -20px;
          opacity: 0.3;
        }

        .balance-header {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          color: var(--text-secondary);
          font-size: 1rem;
          font-weight: 600;
          margin-bottom: 1rem;
        }

        .balance-card h2 {
          font-size: 3rem;
          margin-bottom: 0.5rem;
          background: linear-gradient(135deg, #10b981, #00b4d8);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }

        .balance-card p {
          font-size: 0.95rem;
          color: var(--text-secondary);
        }

        .error-alert {
          background: rgba(239, 68, 68, 0.1);
          border: 1px solid rgba(239, 68, 68, 0.2);
          color: var(--danger);
          padding: 0.6rem 0.8rem;
          border-radius: var(--radius-sm);
          font-size: 0.85rem;
          margin-bottom: 1rem;
        }

        .success-alert {
          background: rgba(16, 185, 129, 0.1);
          border: 1px solid rgba(16, 185, 129, 0.2);
          color: var(--success);
          padding: 0.6rem 0.8rem;
          border-radius: var(--radius-sm);
          font-size: 0.85rem;
          margin-bottom: 1rem;
        }

        .tx-submit {
          width: 100%;
          display: flex;
          align-items: center;
          justify-content: center;
          height: 44px;
        }

        .wallet-alert { margin: 0; }
        .deposit-modal-overlay { position: fixed; inset: 0; z-index: 1000; display: grid; place-items: center; padding: 1rem; background: rgba(3, 7, 18, .76); backdrop-filter: blur(8px); }
        .deposit-modal { position: relative; width: min(520px, 100%); padding: 2rem; border-radius: var(--radius-md); }
        .deposit-modal-close { position: absolute; top: 1rem; right: 1rem; border: 0; background: transparent; color: var(--text-secondary); cursor: pointer; }
        .deposit-modal h3 { font-size: 1.4rem; }
        .deposit-modal-subtitle { margin: .35rem 0 1.25rem; color: var(--text-secondary); }
        .deposit-amount-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: .75rem; }
        .deposit-amount-option { display: flex; flex-direction: column; align-items: center; gap: .2rem; padding: 1rem .5rem; border: 1px solid rgba(255,255,255,.1); border-radius: var(--radius-sm); background: rgba(255,255,255,.035); color: var(--text-primary); cursor: pointer; transition: var(--transition-fast); }
        .deposit-amount-option:hover { border-color: rgba(16,185,129,.45); transform: translateY(-2px); }
        .deposit-amount-option.selected { border-color: var(--success); background: rgba(16,185,129,.14); box-shadow: 0 0 0 2px rgba(16,185,129,.08); }
        .deposit-amount-option span { font-size: 1.05rem; font-weight: 750; }
        .deposit-amount-option small { color: var(--text-muted); }
        .deposit-summary { display: flex; justify-content: space-between; margin: 1.25rem 0; padding: 1rem; border-radius: var(--radius-sm); background: rgba(255,255,255,.04); color: var(--text-secondary); }
        .deposit-summary strong { color: var(--success); }
        .transfer-qr-panel { display:grid; grid-template-columns:180px minmax(0,1fr); gap:1rem; align-items:center; margin:0 0 1.25rem; padding:1rem; border:1px solid rgba(16,185,129,.25); border-radius:var(--radius-sm); background:rgba(16,185,129,.06); }
        .transfer-qr-panel img { width:180px; aspect-ratio:1; object-fit:contain; border-radius:10px; background:white; }
        .transfer-details { display:flex; flex-direction:column; gap:.42rem; color:var(--text-secondary); font-size:.85rem; overflow-wrap:anywhere; }
        .transfer-details>strong { color:var(--text-primary); font-size:1rem; }
        .transfer-details b { color:var(--success); }
        .transfer-details small { margin-top:.35rem; color:var(--text-muted); line-height:1.4; }

        /* History Table styles */
        .history-pane {
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .history-pane h3 {
          margin-bottom: 1.25rem;
        }

        .table-scroll {
          width: 100%;
          overflow-x: auto;
        }

        .history-loader, .empty-history {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 200px;
          color: var(--text-muted);
          gap: 0.75rem;
        }

        .empty-icon {
          color: rgba(255, 255, 255, 0.02);
        }

        .tx-table {
          width: 100%;
          border-collapse: collapse;
          text-align: left;
        }

        .tx-table th {
          padding: 0.75rem 1rem;
          font-size: 0.85rem;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          color: var(--text-secondary);
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
        }

        .tx-table td {
          padding: 1rem;
          font-size: 0.9rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.03);
          color: var(--text-primary);
        }

        .tx-id {
          font-family: monospace;
          color: var(--accent-blue);
        }

        .type-tag {
          font-size: 0.8rem;
          font-weight: 700;
          padding: 0.2rem 0.5rem;
          border-radius: 4px;
        }

        .type-tag.deposit {
          background: rgba(16, 185, 129, 0.1);
          color: var(--success);
        }

        .type-tag.withdraw {
          background: rgba(239, 68, 68, 0.1);
          color: var(--danger);
        }

        .tx-amount {
          font-weight: 700;
        }

        .tx-amount.positive {
          color: var(--success);
        }

        .tx-amount.negative {
          color: var(--danger);
        }

        .status-badge {
          display: inline-flex;
          align-items: center;
          gap: 0.3rem;
          font-size: 0.8rem;
          font-weight: 600;
        }

        .status-badge.success {
          color: var(--success);
        }

        .status-badge.danger {
          color: var(--danger);
        }

        .status-badge.warning {
          color: var(--warning);
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        @media (max-width: 768px) {
          .balance-content { align-items: stretch; flex-direction: column; }
          .deposit-button { width: 100%; }
          .deposit-amount-grid { grid-template-columns: repeat(2, 1fr); }
          .transfer-qr-panel { grid-template-columns:1fr; justify-items:center; }
          .transfer-details { width:100%; }
        }
      `}</style>
    </div>
  );
};
