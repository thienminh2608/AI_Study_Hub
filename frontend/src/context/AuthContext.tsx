import React, { createContext, useContext, useState, useEffect } from 'react';
import { api } from '../services/api';

interface User {
  userId: number;
  username: string;
  email: string;
  role: string;
  tierId: number;
  tierName?: string;
  balance: number;
  status: string;
  expiresAt?: string;
}

interface AuthContextType {
  user: User | null;
  loading: boolean;
  login: (token: string, rememberMe?: boolean, refreshToken?: string) => Promise<void>;
  logout: () => void;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  const fetchUser = async () => {
    try {
      const u = await api.auth.getMe();
      setUser(u);
    } catch (err) {
      console.error('Failed to restore auth session:', err);
      localStorage.removeItem('token');
      localStorage.removeItem('refreshToken');
      sessionStorage.removeItem('token');
      setUser(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    const token = localStorage.getItem('token') || sessionStorage.getItem('token');
    if (token) {
      fetchUser();
    } else {
      setLoading(false);
    }

    const handleAuthChange = () => {
      const activeToken = localStorage.getItem('token') || sessionStorage.getItem('token');
      if (!activeToken) {
        setUser(null);
      }
    };

    window.addEventListener('auth-status-changed', handleAuthChange);
    return () => window.removeEventListener('auth-status-changed', handleAuthChange);
  }, []);

  const login = async (token: string, rememberMe = false, refreshToken?: string) => {
    localStorage.removeItem('token');
    localStorage.removeItem('refreshToken');
    sessionStorage.removeItem('token');
    if (rememberMe) {
      localStorage.setItem('token', token);
      if (refreshToken) localStorage.setItem('refreshToken', refreshToken);
    } else {
      sessionStorage.setItem('token', token);
    }
    setLoading(true);
    await fetchUser();
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('refreshToken');
    sessionStorage.removeItem('token');
    setUser(null);
    window.location.href = '/login';
  };

  const refreshUser = async () => {
    try {
      const u = await api.auth.getMe();
      setUser(u);
    } catch (err) {
      console.error('Failed to refresh user stats:', err);
    }
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, logout, refreshUser }}>
      {children}
    </AuthContext.Provider>
  );
};

// The hook intentionally lives beside its provider so the authentication contract stays centralized.
// oxlint-disable-next-line react/only-export-components
export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
