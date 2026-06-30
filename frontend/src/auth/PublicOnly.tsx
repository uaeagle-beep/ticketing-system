// Wrapper for public auth routes (login / signup). If the user is already
// authenticated, send them to the board instead of showing the login form.
// The verify-email and resend routes remain accessible regardless (a logged-in
// user could still follow a verification link), so they do NOT use this guard.

import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { FullPageLoader } from '@/components/FullPageLoader';

export function PublicOnly() {
  const { status } = useAuth();

  if (status === 'loading') {
    return <FullPageLoader label="Loading…" />;
  }
  if (status === 'authenticated') {
    return <Navigate to="/board" replace />;
  }
  return <Outlet />;
}
