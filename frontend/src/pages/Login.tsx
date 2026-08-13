import React, { useEffect, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { api } from '../services/api';
import { Eye, EyeOff, Lock, Mail, Loader } from 'lucide-react';

export const Login: React.FC = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(false);

  const { login, user, loading } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (!loading && user) {
      navigate(
        user.role.trim().toUpperCase() === 'ADMIN'
          ? '/admin'
          : user.role.trim().toUpperCase() === 'MODERATOR'
            ? '/moderator'
            : '/',
        { replace: true },
      );
    }
  }, [loading, navigate, user]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (!email || !password) {
      setError('Vui lòng điền đầy đủ các thông tin.');
      return;
    }

    setSubmitting(true);
    try {
      const response = await api.auth.login({ email, password, rememberMe });
      await login(response.token, rememberMe, response.refreshToken);
      navigate(
        response.role?.trim().toUpperCase() === 'ADMIN'
          ? '/admin'
          : response.role?.trim().toUpperCase() === 'MODERATOR'
            ? '/moderator'
            : '/',
        { replace: true },
      );
    } catch (err: any) {
      setError(err.message || 'Đăng nhập thất bại. Vui lòng kiểm tra lại tài khoản.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-card glass-panel animate-slide-up">
        <div className="auth-header">
          <h1>Đăng Nhập</h1>
          <p>Trở lại hành trình học tập thông minh cùng AI Study Hub</p>
        </div>

        {error && (
          <div className="error-alert">
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="auth-form">
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

          <div className="form-group">
            <div className="password-header">
              <label>Mật khẩu</label>
              <Link to="/forgot-password" className="forgot-link">
                Quên mật khẩu?
              </Link>
            </div>
            <div className="input-icon-wrapper">
              <Lock size={18} className="input-icon" />
              <input
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••"
                className="input-control"
                disabled={submitting}
              />
              <button
                type="button"
                className="password-toggle"
                onClick={() => setShowPassword((value) => !value)}
                aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
              >
                {showPassword ? <EyeOff size={19} /> : <Eye size={19} />}
              </button>
            </div>
          </div>

          <label className="remember-login">
            <input
              type="checkbox"
              checked={rememberMe}
              onChange={(event) => setRememberMe(event.target.checked)}
            />
            <span>
              <strong>Ghi nhớ đăng nhập</strong>
              <small>Tự động gia hạn phiên đăng nhập trên thiết bị này.</small>
            </span>
          </label>

          <button type="submit" className="btn-primary auth-submit" disabled={submitting}>
            {submitting ? <Loader className="spin" size={18} /> : 'Đăng nhập'}
          </button>
        </form>

        <div className="auth-footer">
          <p>
            Chưa có tài khoản? <Link to="/register">Đăng ký ngay</Link>
          </p>
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

        .password-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
        }

        .forgot-link {
          font-size: 0.85rem;
          color: var(--accent-blue);
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
        .input-icon-wrapper .input-control[type='password'],
        .input-icon-wrapper .input-control[type='text'] { padding-right: 3rem; }
        .password-toggle { position:absolute;right:.65rem;top:50%;transform:translateY(-50%);width:36px;height:36px;display:grid;place-items:center;border:1px solid rgba(0,180,216,.25);border-radius:8px;background:rgba(0,180,216,.12);color:#67e8f9;cursor:pointer; }
        .password-toggle:hover { background:rgba(0,180,216,.22);border-color:var(--accent-blue);color:white;box-shadow:0 0 12px rgba(0,180,216,.2); }
        .remember-login { display:flex!important;align-items:flex-start;gap:.7rem;color:var(--text-primary)!important;cursor:pointer; }
        .remember-login input { width:17px;height:17px;margin-top:.15rem;accent-color:var(--accent-blue); }
        .remember-login span { display:grid;gap:.18rem; }
        .remember-login strong { font-size:.86rem; }
        .remember-login small { color:var(--text-muted);font-size:.74rem;font-weight:400; }

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
