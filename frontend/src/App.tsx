import React from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { SidebarLayout } from './components/SidebarLayout';

// Pages
import { Login } from './pages/Login';
import { Register } from './pages/Register';
import { ForgotPassword } from './pages/ForgotPassword';
import { Dashboard } from './pages/Dashboard';
import { ChatAssistant } from './pages/ChatAssistant';
import { Friends } from './pages/Friends';
import { Wallet } from './pages/Wallet';
import { Premium } from './pages/Premium';
import { Profile } from './pages/Profile';
import { DocumentViewer } from './pages/DocumentViewer';
import { AdminDashboard } from './pages/AdminDashboard';

import './App.css';

const App: React.FC = () => {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          {/* Public Auth Routes */}
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />

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
            <Route path="chat" element={<ChatAssistant />} />
            <Route path="friends" element={<Friends />} />
            <Route path="wallet" element={<Wallet />} />
            <Route path="premium" element={<Premium />} />
            <Route path="profile" element={<Profile />} />
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
          </Route>

          {/* Catch-all Fallback */}
          <Route path="*" element={<Login />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
};

export default App;
