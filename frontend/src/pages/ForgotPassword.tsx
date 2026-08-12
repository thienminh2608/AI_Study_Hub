import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../services/api';
import { Mail, Lock, Key, Loader } from 'lucide-react';

export const ForgotPassword: React.FC = () => {
  const [step, setStep] = useState<1 | 2>(1); // Step 1: Send OTP, Step 2: Verify and Reset
  const [email, setEmail] = useState('');
  const [otp, setOtp] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const navigate = useNavigate();

  const handleSendOtp = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    if (!email) {
      setError('Vui lòng nhập địa chỉ email.');
      return;
    }

    setSubmitting(true);
    try {
      await api.auth.forgotPassword(email);
      setSuccess('Mã OTP đã được gửi về email của bạn. Vui lòng kiểm tra.');
      setTimeout(() => {
        setStep(2);
        setSuccess('');
      }, 1500);
    } catch (err: any) {
      setError(err.message || 'Gửi OTP thất bại. Email có thể không tồn tại.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleResetPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    if (!otp || !newPassword || !confirmPassword) {
      setError('Vui lòng điền đầy đủ các thông tin.');
      return;
    }

    if (newPassword !== confirmPassword) {
      setError('Mật khẩu xác nhận không khớp.');
      return;
    }

    setSubmitting(true);
    try {
      // 1. Verify OTP first
      await api.auth.verifyOtp({ email, otp });
      // 2. Reset password
      await api.auth.resetPassword({ email, otp, newPassword });
      setSuccess('Đặt lại mật khẩu thành công! Đang chuyển hướng về trang đăng nhập...');
      setTimeout(() => {
        navigate('/login');
      }, 2500);
    } catch (err: any) {
      setError(err.message || 'Xác thực hoặc đổi mật khẩu thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-card glass-panel animate-slide-up">
        <div className="auth-header">
          <h1>Quên Mật Khẩu</h1>
          <p>
            {step === 1 
              ? 'Nhập email đăng ký để nhận mã OTP xác minh.' 
              : 'Nhập mã OTP đã nhận và mật khẩu mới của bạn.'}
          </p>
        </div>

        {error && (
          <div className="error-alert">
            <span>{error}</span>
          </div>
        )}

        {success && (
          <div className="success-alert">
            <span>{success}</span>
          </div>
        )}

        {step === 1 ? (
          <form onSubmit={handleSendOtp} className="auth-form">
            <div className="form-group">
              <label>Địa chỉ Email</label>
              <div className="input-icon-wrapper">
                <Mail size={18} className="input-icon" />
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="email@vi-du.com"
                  className="input-control"
                  disabled={submitting}
                />
              </div>
            </div>

            <button type="submit" className="btn-primary auth-submit" disabled={submitting}>
              {submitting ? <Loader className="spin" size={18} /> : 'Gửi mã OTP'}
            </button>
          </form>
        ) : (
          <form onSubmit={handleResetPassword} className="auth-form">
            <div className="form-group">
              <label>Mã OTP (6 chữ số)</label>
              <div className="input-icon-wrapper">
                <Key size={18} className="input-icon" />
                <input
                  type="text"
                  value={otp}
                  onChange={(e) => setOtp(e.target.value)}
                  placeholder="123456"
                  maxLength={6}
                  className="input-control"
                  disabled={submitting}
                />
              </div>
            </div>

            <div className="form-group">
              <label>Mật khẩu mới</label>
              <div className="input-icon-wrapper">
                <Lock size={18} className="input-icon" />
                <input
                  type="password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  placeholder="••••••••"
                  className="input-control"
                  disabled={submitting}
                />
              </div>
            </div>

            <div className="form-group">
              <label>Xác nhận mật khẩu mới</label>
              <div className="input-icon-wrapper">
                <Lock size={18} className="input-icon" />
                <input
                  type="password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  placeholder="••••••••"
                  className="input-control"
                  disabled={submitting}
                />
              </div>
            </div>

            <button type="submit" className="btn-primary auth-submit" disabled={submitting}>
              {submitting ? <Loader className="spin" size={18} /> : 'Đặt lại mật khẩu'}
            </button>
          </form>
        )}

        <div className="auth-footer">
          <p>Quay lại trang <Link to="/login">Đăng nhập</Link></p>
        </div>
      </div>

      <style>{`
        .auth-page {
          display: flex;
          justify-content: center;
          align-items: center;
          min-height: 100vh;
          background-color: var(--bg-primary);
          padding: 1rem;
          position: relative;
          overflow: hidden;
        }

        .auth-page::before {
          content: '';
          position: absolute;
          width: 300px;
          height: 300px;
          background: var(--accent-purple);
          filter: blur(150px);
          top: -10%;
          left: -10%;
          opacity: 0.15;
        }

        .auth-page::after {
          content: '';
          position: absolute;
          width: 300px;
          height: 300px;
          background: var(--accent-blue);
          filter: blur(150px);
          bottom: -10%;
          right: -10%;
          opacity: 0.15;
        }

        .auth-card {
          width: 100%;
          max-width: 440px;
          padding: 2.5rem;
          border-radius: var(--radius-lg);
          z-index: 10;
        }

        .auth-header {
          text-align: center;
          margin-bottom: 2rem;
        }

        .auth-header h1 {
          font-size: 2rem;
          margin-bottom: 0.5rem;
          background: var(--accent-glow);
          -webkit-background-clip: text;
          -webkit-text-fill-color: transparent;
        }

        .auth-header p {
          color: var(--text-secondary);
          font-size: 0.9rem;
        }

        .error-alert {
          background: rgba(239, 68, 68, 0.1);
          border: 1px solid rgba(239, 68, 68, 0.2);
          color: var(--danger);
          padding: 0.75rem 1rem;
          border-radius: var(--radius-sm);
          margin-bottom: 1.5rem;
          font-size: 0.9rem;
          font-weight: 500;
        }

        .success-alert {
          background: rgba(16, 185, 129, 0.1);
          border: 1px solid rgba(16, 185, 129, 0.2);
          color: var(--success);
          padding: 0.75rem 1rem;
          border-radius: var(--radius-sm);
          margin-bottom: 1.5rem;
          font-size: 0.9rem;
          font-weight: 500;
        }

        .auth-form {
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

        .input-icon-wrapper {
          position: relative;
        }

        .input-icon {
          position: absolute;
          left: 1rem;
          top: 50%;
          transform: translateY(-50%);
          color: var(--text-muted);
        }

        .input-icon-wrapper .input-control {
          padding-left: 2.75rem;
        }

        .auth-submit {
          margin-top: 0.5rem;
          justify-content: center;
          height: 48px;
        }

        .spin {
          animation: spin 1s linear infinite;
        }

        .auth-footer {
          margin-top: 1.5rem;
          text-align: center;
          font-size: 0.9rem;
          color: var(--text-secondary);
        }

        @keyframes spin {
          to { transform: rotate(360deg); }
        }
      `}</style>
    </div>
  );
};
