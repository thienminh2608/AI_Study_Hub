import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { User, Mail, Shield, Coins, Calendar, Loader, Key, Pencil, Save, X } from 'lucide-react';

export const Profile: React.FC = () => {
  const { user, refreshUser } = useAuth();
  const [editingUsername, setEditingUsername] = useState(false);
  const [username, setUsername] = useState(user?.username ?? '');
  const [savingUsername, setSavingUsername] = useState(false);

  // States for Password Change
  const [step, setStep] = useState<0 | 1 | 2>(0); // 0: Idle, 1: Sent OTP, 2: Reset
  const [otp, setOtp] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [loading, setLoading] = useState(false);

  const handleStartPasswordReset = async () => {
    if (!user) return;
    setError('');
    setSuccess('');
    setLoading(true);

    try {
      await api.auth.forgotPassword(user.email);
      setSuccess('Mã OTP xác minh đã được gửi đến email của bạn.');
      setStep(1);
    } catch (err: any) {
      setError(err.message || 'Không thể gửi mã xác minh.');
    } finally {
      setLoading(false);
    }
  };

  const handleSaveUsername = async () => {
    const nextName = username.trim();
    if (!nextName || nextName === user?.username) {
      setEditingUsername(false);
      return;
    }
    setError('');
    setSuccess('');
    setSavingUsername(true);
    try {
      await api.auth.updateUsername(nextName);
      await refreshUser();
      setSuccess('Đã cập nhật tên người dùng.');
      setEditingUsername(false);
    } catch (err: any) {
      setError(err.message || 'Không thể cập nhật tên người dùng.');
    } finally {
      setSavingUsername(false);
    }
  };

  const handleVerifyOtp = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!user || !otp) return;
    setError('');
    setSuccess('');
    setLoading(true);

    try {
      await api.auth.verifyOtp({ email: user.email, otp });
      setSuccess('Xác minh OTP thành công. Điền mật khẩu mới của bạn.');
      setStep(2);
    } catch (err: any) {
      setError(err.message || 'Mã OTP không chính xác hoặc đã hết hạn.');
    } finally {
      setLoading(false);
    }
  };

  const handleUpdatePassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!user || !newPassword || !confirmPassword) return;
    setError('');
    setSuccess('');

    if (newPassword !== confirmPassword) {
      setError('Mật khẩu xác nhận không khớp.');
      return;
    }

    setLoading(true);
    try {
      await api.auth.resetPassword({ email: user.email, otp, newPassword });
      setSuccess('Cập nhật mật khẩu thành công!');
      setStep(0);
      setOtp('');
      setNewPassword('');
      setConfirmPassword('');
    } catch (err: any) {
      setError(err.message || 'Cập nhật mật khẩu thất bại.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="profile-container">
      <div className="profile-grid animate-slide-up">
        {/* Left Side: Account Info Card */}
        <div className="info-card glass-panel">
          <div className="avatar-glow"></div>
          <div className="avatar-section">
            <div className="avatar-circle">
              <User size={48} className="avatar-icon" />
            </div>
            <div className="username-editor">
              {editingUsername ? (
                <>
                  <input
                    value={username}
                    onChange={(event) => setUsername(event.target.value)}
                    className="username-input"
                    maxLength={50}
                    autoFocus
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') handleSaveUsername();
                      if (event.key === 'Escape') {
                        setUsername(user?.username ?? '');
                        setEditingUsername(false);
                      }
                    }}
                  />
                  <button
                    type="button"
                    onClick={handleSaveUsername}
                    disabled={savingUsername}
                    aria-label="Lưu tên người dùng"
                  >
                    {savingUsername ? <Loader className="spin" size={17} /> : <Save size={17} />}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setUsername(user?.username ?? '');
                      setEditingUsername(false);
                    }}
                    disabled={savingUsername}
                    aria-label="Hủy sửa tên"
                  >
                    <X size={17} />
                  </button>
                </>
              ) : (
                <>
                  <h2>{user?.username}</h2>
                  <button
                    type="button"
                    onClick={() => {
                      setUsername(user?.username ?? '');
                      setEditingUsername(true);
                    }}
                    aria-label="Sửa tên người dùng"
                    title="Sửa tên người dùng"
                  >
                    <Pencil size={17} />
                  </button>
                </>
              )}
            </div>
            <span
              className={`badge ${user?.role === 'ADMIN' ? 'admin' : user?.tierId === 3 ? 'premium' : 'free'}`}
            >
              {user?.role === 'ADMIN' ? 'ADMIN' : user?.tierId === 3 ? 'PREMIUM' : 'FREE'}
            </span>
          </div>

          <div className="info-list">
            <div className="info-row">
              <Mail size={16} className="info-row-icon" />
              <div className="info-details">
                <span className="label">Email</span>
                <span className="val">{user?.email}</span>
              </div>
            </div>
            <div className="info-row">
              <Coins size={16} className="info-row-icon" />
              <div className="info-details balance-details">
                <span className="label">Số dư khả dụng</span>
                <div className="profile-balance-actions">
                  <span className="val balance">{(user?.balance || 0).toLocaleString()}đ</span>
                  {user?.role !== 'ADMIN' && (
                    <Link to="/wallet?deposit=1" className="profile-deposit-link">
                      Nạp tiền
                    </Link>
                  )}
                </div>
              </div>
            </div>
            <div className="info-row">
              <Shield size={16} className="info-row-icon" />
              <div className="info-details">
                <span className="label">Trạng thái tài khoản</span>
                <span className={`val status-tag ${user?.status}`}>
                  {user?.status === 'ACTIVE' ? 'Hoạt động' : 'Tạm khóa'}
                </span>
              </div>
            </div>
            {user?.expiresAt && (
              <div className="info-row">
                <Calendar size={16} className="info-row-icon" />
                <div className="info-details">
                  <span className="label">Ngày hết hạn Premium</span>
                  <span className="val">{new Date(user.expiresAt).toLocaleDateString()}</span>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Right Side: Security / Password Change settings */}
        <div className="security-card glass-panel">
          <h3>Bảo mật tài khoản</h3>
          <p className="subtitle">
            Thay đổi mật khẩu đăng nhập bằng cách gửi mã OTP xác minh qua Email.
          </p>

          {error && <div className="error-alert">{error}</div>}
          {success && <div className="success-alert">{success}</div>}

          {step === 0 && (
            <div className="security-action">
              <p className="action-desc">
                Nhấn nút bên dưới để gửi mã xác minh (OTP) về hộp thư <strong>{user?.email}</strong>
                .
              </p>
              <button onClick={handleStartPasswordReset} className="btn-primary" disabled={loading}>
                {loading ? (
                  <Loader className="spin" size={18} />
                ) : (
                  <>
                    <Key size={18} />
                    <span>Yêu cầu thay đổi mật khẩu</span>
                  </>
                )}
              </button>
            </div>
          )}

          {step === 1 && (
            <form onSubmit={handleVerifyOtp} className="security-form">
              <div className="form-group">
                <label>Nhập mã OTP (6 chữ số)</label>
                <input
                  type="text"
                  placeholder="123456"
                  maxLength={6}
                  value={otp}
                  onChange={(e) => setOtp(e.target.value)}
                  className="input-control"
                  required
                  disabled={loading}
                />
              </div>
              <div className="form-actions">
                <button type="button" onClick={() => setStep(0)} className="btn-secondary">
                  Hủy
                </button>
                <button type="submit" className="btn-primary" disabled={loading}>
                  {loading ? <Loader className="spin" size={16} /> : 'Xác nhận OTP'}
                </button>
              </div>
            </form>
          )}

          {step === 2 && (
            <form onSubmit={handleUpdatePassword} className="security-form">
              <div className="form-group">
                <label>Mật khẩu mới</label>
                <input
                  type="password"
                  placeholder="••••••••"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  className="input-control"
                  required
                  disabled={loading}
                />
              </div>
              <div className="form-group">
                <label>Xác nhận mật khẩu mới</label>
                <input
                  type="password"
                  placeholder="••••••••"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="input-control"
                  required
                  disabled={loading}
                />
              </div>
              <div className="form-actions">
                <button type="button" onClick={() => setStep(0)} className="btn-secondary">
                  Hủy
                </button>
                <button type="submit" className="btn-primary" disabled={loading}>
                  {loading ? <Loader className="spin" size={16} /> : 'Cập nhật mật khẩu'}
                </button>
              </div>
            </form>
          )}
        </div>
      </div>

      <style>{`
        .profile-container {
          max-width: 900px;
          margin: 0 auto;
          padding: 1rem 0;
          min-height: 80vh;
        }

        .profile-grid {
          display: grid;
          grid-template-columns: 1fr 1.3fr;
          gap: 1.5rem;
        }

        .info-card {
          padding: 2.5rem 1.5rem;
          display: flex;
          flex-direction: column;
          align-items: center;
          position: relative;
          overflow: hidden;
          border-radius: var(--radius-md);
        }

        .avatar-glow {
          position: absolute;
          width: 120px;
          height: 120px;
          background: var(--accent-blue);
          filter: blur(80px);
          top: -20px;
          left: 50%;
          transform: translateX(-50%);
          opacity: 0.2;
        }

        .avatar-section {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.5rem;
          margin-bottom: 2rem;
          z-index: 5;
        }

        .avatar-circle {
          width: 80px;
          height: 80px;
          border-radius: 50%;
          background: rgba(255, 255, 255, 0.03);
          border: 1px solid var(--border-neon);
          display: flex;
          justify-content: center;
          align-items: center;
          margin-bottom: 0.5rem;
        }

        .avatar-icon {
          color: var(--accent-purple);
        }

        .info-card h2 {
          font-size: 1.25rem;
        }

        .username-editor { display:flex;align-items:center;justify-content:center;gap:.4rem;width:100%; }
        .username-editor button { width:30px;height:30px;display:grid;place-items:center;border:1px solid rgba(255,255,255,.1);border-radius:7px;background:rgba(255,255,255,.04);color:var(--accent-blue);cursor:pointer; }
        .username-editor button:hover { border-color:var(--accent-blue);background:rgba(0,180,216,.1); }
        .username-input { min-width:0;width:min(170px,60%);padding:.45rem .55rem;border:1px solid var(--accent-blue);border-radius:7px;background:rgba(0,0,0,.2);color:var(--text-primary);font-weight:600;text-align:center; }

        .badge {
          font-size: 0.75rem;
          padding: 0.25rem 0.6rem;
          border-radius: 4px;
          font-weight: 700;
        }

        .badge.free {
          background: rgba(255, 255, 255, 0.1);
          color: var(--text-secondary);
        }

        .badge.premium {
          background: rgba(157, 78, 221, 0.2);
          color: var(--accent-purple);
          border: 1px solid rgba(157, 78, 221, 0.3);
        }

        .badge.admin {
          background: rgba(0, 180, 216, 0.2);
          color: var(--accent-blue);
          border: 1px solid rgba(0, 180, 216, 0.3);
        }

        .info-list {
          width: 100%;
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .info-row {
          display: flex;
          gap: 0.75rem;
          align-items: center;
        }

        .info-row-icon {
          color: var(--text-muted);
        }

        .info-details {
          display: flex;
          flex-direction: column;
          gap: 0.15rem;
        }

        .info-details .label {
          font-size: 0.8rem;
          color: var(--text-muted);
          font-weight: 500;
        }

        .info-details .val {
          font-size: 0.95rem;
          color: var(--text-primary);
          font-weight: 600;
        }

        .val.balance {
          color: var(--success);
        }

        .balance-details {
          flex: 1;
        }

        .profile-balance-actions {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 0.75rem;
        }

        .profile-deposit-link {
          padding: 0.38rem 0.7rem;
          border: 1px solid rgba(16, 185, 129, 0.35);
          border-radius: 7px;
          background: rgba(16, 185, 129, 0.1);
          color: var(--success);
          font-size: 0.78rem;
          font-weight: 700;
          white-space: nowrap;
          transition: var(--transition-fast);
        }

        .profile-deposit-link:hover {
          background: rgba(16, 185, 129, 0.2);
          border-color: var(--success);
          transform: translateY(-1px);
        }

        .status-tag {
          font-size: 0.85rem;
          font-weight: 700;
        }

        .status-tag.ACTIVE {
          color: var(--success);
        }

        .status-tag.SUSPENDED {
          color: var(--danger);
        }

        .security-card {
          padding: 2rem;
          border-radius: var(--radius-md);
        }

        .security-card h3 {
          margin-bottom: 0.25rem;
        }

        .security-card .subtitle {
          color: var(--text-secondary);
          font-size: 0.9rem;
          margin-bottom: 2rem;
        }

        .security-action {
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
          background: rgba(255, 255, 255, 0.02);
          border: 1px solid rgba(255, 255, 255, 0.05);
          padding: 1.5rem;
          border-radius: var(--radius-md);
        }

        .action-desc {
          font-size: 0.9rem;
          color: var(--text-secondary);
          line-height: 1.5;
        }

        .security-form {
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
        }

        .form-group {
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
        }

        .form-group label {
          font-size: 0.9rem;
          font-weight: 600;
          color: var(--text-secondary);
        }

        .form-actions {
          display: flex;
          justify-content: flex-end;
          gap: 0.75rem;
          margin-top: 0.5rem;
        }

        .error-alert {
          background: rgba(239, 68, 68, 0.1);
          border: 1px solid rgba(239, 68, 68, 0.2);
          color: var(--danger);
          padding: 0.75rem 1rem;
          border-radius: var(--radius-sm);
          font-size: 0.9rem;
          font-weight: 500;
          margin-bottom: 1.5rem;
        }

        .success-alert {
          background: rgba(16, 185, 129, 0.1);
          border: 1px solid rgba(16, 185, 129, 0.2);
          color: var(--success);
          padding: 0.75rem 1rem;
          border-radius: var(--radius-sm);
          font-size: 0.9rem;
          font-weight: 500;
          margin-bottom: 1.5rem;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }

        @media (max-width: 768px) {
          .profile-grid {
            grid-template-columns: 1fr;
          }
        }
      `}</style>
    </div>
  );
};
