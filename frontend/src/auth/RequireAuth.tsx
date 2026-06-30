// Route guard for protected (business) routes. Until the auth bootstrap
// resolves we show a full-page loader; unauthenticated users are redirected to
// /login, preserving the intended destination so they return there after login.

import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { FullPageLoader } from '@/components/FullPageLoader';

export function RequireAuth() {
  const { status } = useAuth();
  const location = useLocation();

  if (status === 'loading') {
    return <FullPageLoader label="Loading your session…" />;
  }

  if (status === 'unauthenticated') {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }

  return <Outlet />;
}
