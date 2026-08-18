import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { Award, Check, Loader, Star, AlertTriangle, ShieldCheck } from 'lucide-react';
import { useUiFeedback } from '../context/UiFeedbackContext';

interface Tier {
  tierId: number;
  tierName: string;
  price: number;
  maxStorageMb: number;
  totalStorageMb: number;
  aiPromptLimitPerDay: number;
}

export const Premium: React.FC = () => {
  const { confirm, notify } = useUiFeedback();
  const { user, refreshUser } = useAuth();
  const [tiers, setTiers] = useState<Tier[]>([]);
  const [loading, setLoading] = useState(true);
  const [buying, setBuying] = useState(false);
  const [togglingAutoRenew, setTogglingAutoRenew] = useState(false);

  const loadTiers = async () => {
    try {
      const data = await api.transaction.getTiers();
      setTiers(data);
    } catch (err: any) {
      console.error('Failed to load subscription tiers:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleToggleAutoRenew = async () => {
    setTogglingAutoRenew(true);
    try {
      const res = await api.auth.toggleAutoRenew();
      notify(res.message, 'success');
      await refreshUser();
    } catch (err: any) {
      notify(err.message || 'Thay đổi tự động gia hạn thất bại.', 'error');
    } finally {
      setTogglingAutoRenew(false);
    }
  };

  useEffect(() => {
    loadTiers();
  }, []);

  const freeTier = tiers.find((t) => t.tierId === 2);
  const premiumTier = tiers.find((t) => t.tierId === 3);

  const freeStorage = freeTier?.maxStorageMb ?? 50;
  const freeFileSize = freeTier?.totalStorageMb ?? 5;
  const freeAiLimit = freeTier?.aiPromptLimitPerDay ?? 10;

  const premiumPrice = premiumTier?.price ?? 99000;
  const premiumStorage = premiumTier?.maxStorageMb ?? 1000;
  const premiumFileSize = premiumTier?.totalStorageMb ?? 50;
  const premiumAiLimit = premiumTier?.aiPromptLimitPerDay ?? 200;

  const handleBuyPremium = async () => {
    if (user && user.balance < premiumPrice) {
      notify(
        `Số dư của bạn không đủ (${user.balance.toLocaleString()}đ / ${premiumPrice.toLocaleString()}đ). Vui lòng dùng nút Nạp tiền cạnh số dư để thực hiện nâng cấp.`,
        'error',
      );
      return;
    }

    if (
      !(await confirm({
        title: 'Đăng ký Premium',
        message: `Tài khoản sẽ bị trừ ${premiumPrice.toLocaleString()}đ và được gia hạn Premium thêm 30 ngày.`,
        confirmLabel: 'Đăng ký Premium',
      }))
    ) {
      return;
    }

    setBuying(true);
    try {
      await api.transaction.buyPremium();
      notify('Đăng ký gói Premium thành công! Tài khoản của bạn đã được nâng cấp.', 'success');
      await refreshUser();
    } catch (err: any) {
      notify(err.message || 'Giao dịch nâng cấp thất bại.', 'error');
    } finally {
      setBuying(false);
    }
  };

  const isPremiumActive = user && user.tierId === 3;

  return (
    <div className="premium-container">
      <div className="premium-header animate-fade-in">
        <Star size={36} className="star-icon glow-yellow" />
        <h1>Gói Thành Viên Premium</h1>
        <p>
          Bứt phá mọi giới hạn học tập, tối ưu hóa không gian lưu trữ và giải phóng sức mạnh AI trợ
          lý.
        </p>
      </div>

      {loading ? (
        <div className="premium-loader">
          <Loader className="spin" size={32} />
          <p>Đang tải bảng giá dịch vụ...</p>
        </div>
      ) : (
        <div className="tiers-grid animate-slide-up">
          {/* Free Tier Card */}
          <div className="tier-card glass-panel">
            <h3>Thành viên Free</h3>
            <div className="price-tag">
              <span className="amount">0đ</span>
              <span className="period">/ vĩnh viễn</span>
            </div>
            <p className="description">
              Dành cho cá nhân muốn trải nghiệm thử các tính năng trợ lý học tập cơ bản.
            </p>

            <ul className="features-list">
              <li>
                <Check size={16} className="check-icon" />
                <span>
                  Dung lượng lưu trữ: <strong>{freeStorage} MB</strong>
                </span>
              </li>
              <li>
                <Check size={16} className="check-icon" />
                <span>
                  Giới hạn file tải lên: <strong>{freeFileSize} MB</strong>
                </span>
              </li>
              <li>
                <Check size={16} className="check-icon" />
                <span>
                  Hỏi trợ lý AI: <strong>{freeAiLimit} lượt / ngày</strong>
                </span>
              </li>
              <li>
                <Check size={16} className="check-icon" />
                <span>Hỗ trợ định dạng: PDF, Excel, Word, TXT</span>
              </li>
            </ul>

            <button className="btn-secondary tier-btn" disabled>
              Gói mặc định
            </button>
          </div>

          {/* Premium Tier Card */}
          <div className="tier-card premium glass-panel active-glow">
            <div className="popular-badge">Khuyên dùng</div>
            <h3>Thành viên Premium</h3>
            <div className="price-tag">
              <span className="amount">{premiumPrice.toLocaleString()}đ</span>
              <span className="period">/ 30 ngày</span>
            </div>
            <p className="description">
              Gói tối ưu nhất dành cho học sinh, sinh viên cần học tập và nghiên cứu tài liệu liên
              tục.
            </p>

            <ul className="features-list">
              <li>
                <Check size={16} className="check-icon premium-color" />
                <span>
                  Dung lượng lưu trữ:{' '}
                  <strong>
                    {premiumStorage >= 1000
                      ? `${(premiumStorage / 1000).toFixed(0)} GB`
                      : `${premiumStorage} MB`}
                  </strong>
                </span>
              </li>
              <li>
                <Check size={16} className="check-icon premium-color" />
                <span>
                  Giới hạn file tải lên: <strong>{premiumFileSize} MB</strong>
                </span>
              </li>
              <li>
                <Check size={16} className="check-icon premium-color" />
                <span>
                  Hỏi trợ lý AI: <strong>{premiumAiLimit} lượt / ngày</strong>
                </span>
              </li>
              <li>
                <Check size={16} className="check-icon premium-color" />
                <span>Hệ thống AI Agent tự động phân tích đệ quy</span>
              </li>
              <li>
                <Check size={16} className="check-icon premium-color" />
                <span>Tự động gia hạn khi ví đủ số dư</span>
              </li>
            </ul>

            {isPremiumActive ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', width: '100%' }}>
                <div className="active-premium-status">
                  <ShieldCheck size={20} className="shield-icon" />
                  <div>
                    <p className="status-title">Đang hoạt động</p>
                    {user?.expiresAt && (
                      <p className="status-date">
                        Hạn dùng: {new Date(user.expiresAt).toLocaleDateString()}
                      </p>
                    )}
                  </div>
                </div>
                {user?.gracePeriodEndsAt && new Date(user.gracePeriodEndsAt).getTime() > Date.now() && (
                  <div className="warning-alert" style={{ display: 'flex', flexDirection: 'column', gap: '4px', padding: '10px', fontSize: '13px', borderRadius: '6px', background: 'rgba(239, 68, 68, 0.1)', color: '#ef4444', border: '1px solid rgba(239, 68, 68, 0.2)', textAlign: 'left' }}>
                    <strong>Gói đang trong thời gian gia hạn tạm (Grace Period)</strong>
                    <span>Vui lòng nạp tiền trước ngày {new Date(user.gracePeriodEndsAt).toLocaleDateString()} để tránh bị hạ cấp gói dịch vụ.</span>
                  </div>
                )}
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px', border: '1px solid var(--border-color)', borderRadius: '6px', background: 'var(--card-bg-light)', fontSize: '13px' }}>
                  <span style={{ color: 'var(--text-secondary)' }}>Tự động gia hạn mỗi tháng</span>
                  <label className="switch-control" style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={user?.isAutoRenew ?? true}
                      onChange={handleToggleAutoRenew}
                      disabled={togglingAutoRenew}
                      style={{ cursor: 'pointer' }}
                    />
                    <span>{user?.isAutoRenew ? 'Bật' : 'Tắt'}</span>
                  </label>
                </div>
              </div>
            ) : (
              <button
                onClick={handleBuyPremium}
                className="btn-primary tier-btn premium-btn"
                disabled={buying}
              >
                {buying ? (
                  <Loader className="spin" size={18} />
                ) : (
                  <>
                    <Award size={18} />
                    <span>Nâng cấp ngay</span>
                  </>
                )}
              </button>
            )}
          </div>
        </div>
      )}

      {/* Safety notice disclaimer */}
      <div className="safety-disclaimer glass-card animate-slide-up">
        <AlertTriangle size={20} className="alert-icon" />
        <p>
          <strong>Lưu ý về cơ chế tự động gia hạn:</strong> Gói Premium của bạn sẽ tự động quét gia
          hạn thêm 30 ngày vào thời điểm hết hạn nếu ví tín dụng của bạn có số dư tối thiểu 99.000đ.
          Hệ thống sẽ gửi email thông báo trước 3 ngày nếu số dư của bạn không đủ để thực hiện gia
          hạn.
        </p>
      </div>

      <style>{`
        .premium-container {
          max-width: 900px;
          margin: 0 auto;
          padding: 1rem 0;
          min-height: 80vh;
        }

        .premium-header {
          text-align: center;
          margin-bottom: 3.5rem;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.75rem;
        }

        .premium-header h1 {
          font-size: 2.25rem;
          background: var(--accent-glow);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }

        .premium-header p {
          max-width: 550px;
          color: var(--text-secondary);
          font-size: 0.95rem;
          line-height: 1.5;
        }

        .star-icon {
          color: var(--warning);
          filter: drop-shadow(0 0 10px rgba(245, 158, 11, 0.4));
        }

        .premium-loader {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 250px;
          color: var(--text-muted);
          gap: 0.75rem;
        }

        .tiers-grid {
          display: grid;
          grid-template-columns: 1fr 1.1fr;
          gap: 2rem;
          align-items: center;
          margin-bottom: 3rem;
        }

        .tier-card {
          padding: 2.5rem 2rem;
          border-radius: var(--radius-lg);
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
          position: relative;
        }

        .tier-card.premium {
          border-color: var(--accent-purple);
          background: rgba(22, 16, 37, 0.6);
        }

        .popular-badge {
          position: absolute;
          top: -12px;
          right: 2rem;
          background: var(--accent-glow);
          color: white;
          font-weight: 700;
          font-size: 0.75rem;
          padding: 0.3rem 0.75rem;
          border-radius: 20px;
          text-transform: uppercase;
          box-shadow: var(--shadow-neon);
        }

        .tier-card h3 {
          font-size: 1.35rem;
        }

        .price-tag {
          display: flex;
          align-items: baseline;
          gap: 0.25rem;
          border-bottom: 1px solid rgba(255, 255, 255, 0.05);
          padding-bottom: 1rem;
        }

        .price-tag .amount {
          font-size: 2.5rem;
          font-weight: 800;
          color: var(--text-primary);
        }

        .price-tag .period {
          color: var(--text-muted);
          font-size: 0.95rem;
        }

        .description {
          font-size: 0.9rem;
          color: var(--text-secondary);
          line-height: 1.4;
          min-height: 2.8rem;
        }

        .features-list {
          list-style: none;
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
          margin: 0.5rem 0;
        }

        .features-list li {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          font-size: 0.92rem;
          color: var(--text-secondary);
        }

        .check-icon {
          color: var(--accent-blue);
          flex-shrink: 0;
        }

        .check-icon.premium-color {
          color: var(--accent-purple);
        }

        .tier-btn {
          width: 100%;
          justify-content: center;
          height: 48px;
          font-size: 1rem;
        }

        .premium-btn {
          box-shadow: var(--shadow-neon);
        }

        .active-premium-status {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          background: rgba(16, 185, 129, 0.1);
          border: 1px solid rgba(16, 185, 129, 0.25);
          padding: 0.75rem 1.25rem;
          border-radius: var(--radius-sm);
          color: var(--success);
          width: 100%;
        }

        .shield-icon {
          flex-shrink: 0;
        }

        .status-title {
          font-weight: 700;
          font-size: 0.95rem;
        }

        .status-date {
          font-size: 0.75rem;
          color: var(--text-secondary);
          margin-top: 0.1rem;
        }

        .safety-disclaimer {
          display: flex;
          gap: 1rem;
          align-items: flex-start;
          padding: 1.25rem 1.5rem;
          border: 1px solid rgba(245, 158, 11, 0.15);
          background: rgba(245, 158, 11, 0.03);
          border-radius: var(--radius-md);
        }

        .safety-disclaimer .alert-icon {
          color: var(--warning);
          flex-shrink: 0;
          margin-top: 0.15rem;
        }

        .safety-disclaimer p {
          font-size: 0.88rem;
          color: var(--text-secondary);
          line-height: 1.5;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        @media (max-width: 768px) {
          .tiers-grid {
            grid-template-columns: 1fr;
          }
        }
      `}</style>
    </div>
  );
};
