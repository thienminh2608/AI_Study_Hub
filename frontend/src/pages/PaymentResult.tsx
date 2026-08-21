import React, { useEffect, useState } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { api } from '../services/api';
import { CheckCircle2, XCircle, Clock, ArrowRight, RefreshCw, Wallet } from 'lucide-react';

export const PaymentResult: React.FC = () => {
  const [searchParams] = useSearchParams();
  const orderCode = searchParams.get('orderCode');

  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<string>('CHECKING'); // CHECKING, SUCCESS, PENDING, CANCELLED, FAILED
  const [amount, setAmount] = useState<number>(0);

  useEffect(() => {
    if (!orderCode) {
      setStatus('CANCELLED');
      setLoading(false);
      return;
    }

    let isMounted = true;
    let timer: any = null;
    let pollCount = 0;

    const checkStatus = async () => {
      try {
        const res = await api.transaction.getPayosStatus(orderCode);
        if (!isMounted) return;

        if (res?.amount) {
          setAmount(res.amount);
        }

        if (res?.status === 'SUCCESS') {
          setStatus('SUCCESS');
          setLoading(false);
        } else if (res?.status === 'CANCELLED' || res?.status === 'EXPIRED' || res?.status === 'FAILED') {
          setStatus('CANCELLED');
          setLoading(false);
        } else {
          // Still PENDING or CREATING
          pollCount++;
          if (pollCount >= 10) {
            setStatus('PENDING');
            setLoading(false);
          } else {
            timer = setTimeout(checkStatus, 2000);
          }
        }
      } catch {
        if (!isMounted) return;
        pollCount++;
        if (pollCount >= 5) {
          setStatus('PENDING');
          setLoading(false);
        } else {
          timer = setTimeout(checkStatus, 2000);
        }
      }
    };

    checkStatus();

    return () => {
      isMounted = false;
      if (timer) clearTimeout(timer);
    };
  }, [orderCode]);

  return (
    <div className="payment-result-page" style={{ maxWidth: '600px', margin: '40px auto', padding: '24px' }}>
      <div className="glass-panel" style={{ padding: '32px', textAlign: 'center', borderRadius: '16px' }}>
        {loading || status === 'CHECKING' ? (
          <div>
            <RefreshCw className="animate-spin" size={48} style={{ color: 'var(--primary-color)', margin: '0 auto 16px' }} />
            <h2 style={{ fontSize: '1.5rem', fontWeight: 600, marginBottom: '8px' }}>Đang Xác Nhận Giao Dịch...</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '16px' }}>
              Vui lòng đợi trong giây lát, hệ thống đang đồng bộ kết quả từ cổng thanh toán PayOS.
            </p>
            {orderCode && <div style={{ fontSize: '0.9rem', color: 'var(--text-muted)' }}>Mã đơn: #{orderCode}</div>}
          </div>
        ) : status === 'SUCCESS' ? (
          <div>
            <CheckCircle2 size={56} style={{ color: '#10b981', margin: '0 auto 16px' }} />
            <h2 style={{ fontSize: '1.5rem', fontWeight: 700, color: '#10b981', marginBottom: '8px' }}>Nạp Tiền Thành Công!</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '16px' }}>
              Giao dịch đã được ghi nhận và số dư đã được cộng vào ví của bạn.
            </p>
            {amount > 0 && (
              <div style={{ fontSize: '1.25rem', fontWeight: 700, margin: '16px 0', padding: '12px', background: 'rgba(16,185,129,0.1)', borderRadius: '8px', color: '#10b981' }}>
                +{amount.toLocaleString('vi-VN')} VND
              </div>
            )}
            <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', marginTop: '24px' }}>
              <Link to="/wallet" className="btn-primary" style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', padding: '10px 20px', borderRadius: '8px', textDecoration: 'none' }}>
                <Wallet size={18} /> Vào Ví Cá Nhân
              </Link>
              <Link to="/premium" className="btn-outline" style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', padding: '10px 20px', borderRadius: '8px', textDecoration: 'none' }}>
                Mua Gói Premium <ArrowRight size={18} />
              </Link>
            </div>
          </div>
        ) : status === 'PENDING' ? (
          <div>
            <Clock size={56} style={{ color: '#f59e0b', margin: '0 auto 16px' }} />
            <h2 style={{ fontSize: '1.5rem', fontWeight: 600, color: '#f59e0b', marginBottom: '8px' }}>Giao Dịch Đang Chờ Xử Lý</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '16px' }}>
              Hệ thống chưa nhận được tín hiệu tức thì từ ngân hàng. Khi giao dịch hoàn tất, số dư sẽ tự động được cập nhật.
            </p>
            <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', marginTop: '24px' }}>
              <Link to="/wallet" className="btn-primary" style={{ padding: '10px 20px', borderRadius: '8px', textDecoration: 'none' }}>
                Quay Lại Ví
              </Link>
            </div>
          </div>
        ) : (
          <div>
            <XCircle size={56} style={{ color: '#ef4444', margin: '0 auto 16px' }} />
            <h2 style={{ fontSize: '1.5rem', fontWeight: 600, color: '#ef4444', marginBottom: '8px' }}>Giao Dịch Đã Bị Hủy</h2>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '16px' }}>
              Bạn đã hủy hoặc đơn thanh toán đã hết thời hạn thanh toán.
            </p>
            <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', marginTop: '24px' }}>
              <Link to="/wallet" className="btn-primary" style={{ padding: '10px 20px', borderRadius: '8px', textDecoration: 'none' }}>
                Thử Lại Tại Ví
              </Link>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
