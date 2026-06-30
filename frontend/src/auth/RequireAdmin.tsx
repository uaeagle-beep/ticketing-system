// Route guard for the admin-only "Users" zone (ADR-0007). Sits INSIDE RequireAuth, so the
// session is already resolved here: a non-admin is sent back to the board (the SPA never relies on
// hiding the nav item alone — the backend re-checks every admin call regardless).

import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from './AuthContext';

export function RequireAdmin() {
  const { user } = useAuth();

  if (!user?.isAdmin) {
    return <Navigate to="/board" replace />;
  }

  return <Outlet />;
}
