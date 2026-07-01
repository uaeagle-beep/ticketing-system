import { Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from '@/components/AppLayout';
import { RequireAuth } from '@/auth/RequireAuth';
import { RequireAdmin } from '@/auth/RequireAdmin';
import { PublicOnly } from '@/auth/PublicOnly';
import { LoginPage } from '@/features/auth/LoginPage';
import { SignupPage } from '@/features/auth/SignupPage';
import { VerifyEmailPage } from '@/features/auth/VerifyEmailPage';
import { ForgotPasswordPage } from '@/features/auth/ForgotPasswordPage';
import { ResetPasswordPage } from '@/features/auth/ResetPasswordPage';
import { BoardPage } from '@/features/board/BoardPage';
import { TicketPage } from '@/features/tickets/TicketPage';
import { TeamsPage } from '@/features/teams/TeamsPage';
import { EpicsPage } from '@/features/epics/EpicsPage';
import { UsersPage } from '@/features/users/UsersPage';
import { AccountPage } from '@/features/account/AccountPage';
import { NotFoundPage } from '@/components/NotFoundPage';

export function App() {
  return (
    <Routes>
      {/* Public auth routes — redirect to the board if already logged in. */}
      <Route element={<PublicOnly />}>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/signup" element={<SignupPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      </Route>

      {/* Verification + reset routes are always reachable (followed from an email). */}
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />

      {/* Protected business routes — require an authenticated, verified session. */}
      <Route element={<RequireAuth />}>
        <Route element={<AppLayout />}>
          <Route path="/board" element={<BoardPage />} />
          <Route path="/tickets/new" element={<TicketPage />} />
          <Route path="/tickets/:id" element={<TicketPage />} />
          <Route path="/teams" element={<TeamsPage />} />
          <Route path="/epics" element={<EpicsPage />} />
          <Route path="/account" element={<AccountPage />} />
          {/* Admin-only Users zone (ADR-0007). Backend re-checks admin on every call. */}
          <Route element={<RequireAdmin />}>
            <Route path="/users" element={<UsersPage />} />
          </Route>
        </Route>
      </Route>

      <Route path="/" element={<Navigate to="/board" replace />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}
