import React, { Suspense } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { UiFeedbackProvider } from './context/UiFeedbackContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { SidebarLayout } from './components/SidebarLayout';
import { Loader } from 'lucide-react';

// Lazy-loaded Pages via Named Export Adapter
const Login = React.lazy(() => import('./pages/Login').then((m) => ({ default: m.Login })));
const Register = React.lazy(() => import('./pages/Register').then((m) => ({ default: m.Register })));
const ForgotPassword = React.lazy(() => import('./pages/ForgotPassword').then((m) => ({ default: m.ForgotPassword })));
const Dashboard = React.lazy(() => import('./pages/Dashboard').then((m) => ({ default: m.Dashboard })));
const ChatAssistant = React.lazy(() => import('./pages/ChatAssistant').then((m) => ({ default: m.ChatAssistant })));
const Friends = React.lazy(() => import('./pages/Friends').then((m) => ({ default: m.Friends })));
const Wallet = React.lazy(() => import('./pages/Wallet').then((m) => ({ default: m.Wallet })));
const Premium = React.lazy(() => import('./pages/Premium').then((m) => ({ default: m.Premium })));
const Profile = React.lazy(() => import('./pages/Profile').then((m) => ({ default: m.Profile })));
const DocumentViewer = React.lazy(() => import('./pages/DocumentViewer').then((m) => ({ default: m.DocumentViewer })));
const AdminDashboard = React.lazy(() => import('./pages/AdminDashboard').then((m) => ({ default: m.AdminDashboard })));
const PublicDocuments = React.lazy(() => import('./pages/PublicDocuments').then((m) => ({ default: m.PublicDocuments })));
const ModeratorDashboard = React.lazy(() => import('./pages/ModeratorDashboard').then((m) => ({ default: m.ModeratorDashboard })));
const Notifications = React.lazy(() => import('./pages/Notifications').then((m) => ({ default: m.Notifications })));
const TrashPage = React.lazy(() => import('./pages/Trash').then((m) => ({ default: m.TrashPage })));
const PaymentResult = React.lazy(() => import('./pages/PaymentResult').then((m) => ({ default: m.PaymentResult })));

import './App.css';

const PageLoader: React.FC = () => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: '60vh',
      width: '100%',
      color: 'var(--primary, #3b82f6)',
    }}
  >
    <Loader className="spin" size={32} />
  </div>
);

const App: React.FC = () => {
  return (
    <AuthProvider>
      <UiFeedbackProvider>
        <BrowserRouter>
          <Suspense fallback={<PageLoader />}>
            <Routes>
              {/* Public Auth & Shared Document Routes */}
              <Route path="/login" element={<Login />} />
              <Route path="/register" element={<Register />} />
              <Route path="/forgot-password" element={<ForgotPassword />} />
              <Route path="/payment/success" element={<PaymentResult />} />
              <Route path="/payment/cancel" element={<PaymentResult />} />
              <Route path="/d/:token" element={<DocumentViewer />} />
              <Route path="/share/:token" element={<DocumentViewer />} />

              {/* Protected Client Dashboard Routes */}
              <Route
                path="/"
                element={
                  <ProtectedRoute>
                    <SidebarLayout />
                  </ProtectedRoute>
                }
              >
                <Route index element={<Dashboard />} />
                <Route path="public-documents" element={<PublicDocuments />} />
                <Route path="chat" element={<ChatAssistant />} />
                <Route path="friends" element={<Friends />} />
                <Route path="wallet" element={<Wallet />} />
                <Route path="premium" element={<Premium />} />
                <Route path="profile" element={<Profile />} />
                <Route path="notifications" element={<Notifications />} />
                <Route path="trash" element={<TrashPage />} />
                <Route path="document/:id" element={<DocumentViewer />} />

                {/* Protected Admin Routes */}
                <Route
                  path="admin"
                  element={
                    <ProtectedRoute allowedRoles={['ADMIN']}>
                      <AdminDashboard />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="moderator"
                  element={
                    <ProtectedRoute allowedRoles={['ADMIN', 'MODERATOR']}>
                      <ModeratorDashboard />
                    </ProtectedRoute>
                  }
                />
              </Route>

              {/* Catch-all Fallback */}
              <Route path="*" element={<Login />} />
            </Routes>
          </Suspense>
        </BrowserRouter>
      </UiFeedbackProvider>
    </AuthProvider>
  );
};

export default App;
