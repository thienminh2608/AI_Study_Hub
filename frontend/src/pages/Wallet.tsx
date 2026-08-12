import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { 
  Wallet as WalletIcon, 
  Coins, 
  ArrowUpRight, 
  ArrowDownLeft, 
  Loader, 
  Clock, 
  CheckCircle, 
  XCircle 
} from 'lucide-react';

interface Transaction {
  transactionId: number;
  amount: number;
  type: string; // "DEPOSIT" | "WITHDRAW"
  status: string; // "PENDING" | "SUCCESS" | "CANCELLED"
  startedAt?: string;
  completedAt?: string;
}

export const Wallet: React.FC = () => {
  const { user, refreshUser } = useAuth();
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);

  // Forms
  const [amount, setAmount] = useState<number | ''>('');
  const [txType, setTxType] = useState<'DEPOSIT' | 'WITHDRAW'>('DEPOSIT');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

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
  }, []);

  const handleSubmitTransaction = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    if (!amount || amount <= 0) {
      setError('Số tiền phải lớn hơn 0đ.');
      return;
    }

    if (txType === 'WITHDRAW' && user && user.balance < amount) {
      setError('Số dư ví không đủ để thực hiện yêu cầu rút tiền này.');
      return;
    }

    setSubmitting(true);
    try {
      // For deposit, value is positive. For withdraw, value is negative.
      const rawAmount = txType === 'DEPOSIT' ? amount : -amount;
      await api.transaction.create({
        amount: rawAmount,
        type: txType
      });
      setSuccess('Gửi yêu cầu giao dịch thành công. Vui lòng đợi Quản trị viên phê duyệt.');
      setAmount('');
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
            <CheckCircle size={12} />
            <span>Thành công</span>
          </span>
        );
      case 'CANCELLED':
        return (
          <span className="status-badge danger">
            <XCircle size={12} />
            <span>Đã hủy</span>
          </span>
        );
      default:
        return (
          <span className="status-badge warning">
            <Clock size={12} />
            <span>Đang chờ duyệt</span>
          </span>
        );
    }
  };

  return (
    <div className="wallet-container">
      {/* Wallet overview stats */}
      <div className="wallet-overview-grid animate-slide-up">
        
        {/* Balance Card */}
        <div className="balance-card glass-panel">
          <div className="card-glow"></div>
          <div className="balance-header">
            <Coins size={24} className="coin-icon" />
            <span>Số dư khả dụng</span>
          </div>
          <h2>{(user?.balance || 0).toLocaleString()}đ</h2>
          <p>Tài khoản thành viên: <strong>{user?.tierName || 'Free'}</strong></p>
        </div>

        {/* Transaction Request Form */}
        <div className="tx-form-card glass-panel">
          <h3>Tạo giao dịch</h3>
          {error && <div className="error-alert">{error}</div>}
          {success && <div className="success-alert">{success}</div>}

          <form onSubmit={handleSubmitTransaction} className="tx-form">
            <div className="type-toggle">
              <button 
                type="button" 
                onClick={() => setTxType('DEPOSIT')} 
                className={`toggle-btn ${txType === 'DEPOSIT' ? 'active deposit' : ''}`}
              >
                <ArrowDownLeft size={16} />
                <span>Nạp tiền</span>
              </button>
              <button 
                type="button" 
                onClick={() => setTxType('WITHDRAW')} 
                className={`toggle-btn ${txType === 'WITHDRAW' ? 'active withdraw' : ''}`}
              >
                <ArrowUpRight size={16} />
                <span>Rút tiền</span>
              </button>
            </div>

            <div className="form-group">
              <label>Số tiền (VNĐ)</label>
              <input
                type="number"
                placeholder="Ví dụ: 100000"
                value={amount}
                onChange={(e) => setAmount(e.target.value ? parseInt(e.target.value) : '')}
                className="input-control"
                disabled={submitting}
              />
            </div>

            <button type="submit" className="btn-primary tx-submit" disabled={submitting || !amount}>
              {submitting ? <Loader className="spin" size={18} /> : 'Tạo yêu cầu'}
            </button>
          </form>
        </div>

      </div>

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
              <WalletIcon size={48} className="empty-icon" />
              <p>Chưa có giao dịch nào được thực hiện.</p>
            </div>
          ) : (
            <table className="tx-table">
              <thead>
                <tr>
                  <th>Mã giao dịch</th>
                  <th>Loại</th>
                  <th>Số tiền</th>
                  <th>Trạng thái</th>
                  <th>Thời gian khởi tạo</th>
                  <th>Hoàn thành lúc</th>
                </tr>
              </thead>
              <tbody>
                {transactions.map(tx => {
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
                        {isDeposit ? '+' : ''}{tx.amount.toLocaleString()}đ
                      </td>
                      <td>{renderStatusBadge(tx.status)}</td>
                      <td>{tx.startedAt ? new Date(tx.startedAt).toLocaleString() : 'N/A'}</td>
                      <td>{tx.completedAt ? new Date(tx.completedAt).toLocaleString() : '---'}</td>
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

        .wallet-overview-grid {
          display: grid;
          grid-template-columns: 1.2fr 1fr;
          gap: 1.5rem;
        }

        .balance-card {
          padding: 2.5rem;
          display: flex;
          flex-direction: column;
          justify-content: center;
          position: relative;
          overflow: hidden;
          border-radius: var(--radius-md);
        }

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

        .coin-icon {
          color: var(--warning);
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

        .tx-form-card {
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .tx-form-card h3 {
          margin-bottom: 1rem;
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

        .tx-form {
          display: flex;
          flex-direction: column;
          gap: 1rem;
        }

        .type-toggle {
          display: flex;
          gap: 0.5rem;
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid rgba(255, 255, 255, 0.05);
          padding: 0.25rem;
          border-radius: var(--radius-sm);
        }

        .toggle-btn {
          flex: 1;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 0.4rem;
          background: transparent;
          border: none;
          color: var(--text-secondary);
          cursor: pointer;
          font-weight: 600;
          font-size: 0.9rem;
          padding: 0.5rem;
          border-radius: var(--radius-sm);
          transition: var(--transition-fast);
        }

        .toggle-btn:hover {
          color: var(--text-primary);
        }

        .toggle-btn.active.deposit {
          background: rgba(16, 185, 129, 0.15);
          color: var(--success);
          border: 1px solid rgba(16, 185, 129, 0.2);
        }

        .toggle-btn.active.withdraw {
          background: rgba(239, 68, 68, 0.15);
          color: var(--danger);
          border: 1px solid rgba(239, 68, 68, 0.2);
        }

        .tx-submit {
          justify-content: center;
          height: 44px;
        }

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
          .wallet-overview-grid {
            grid-template-columns: 1fr;
          }
        }
      `}</style>
    </div>
  );
};
