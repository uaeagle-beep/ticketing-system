import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import { App } from './App';
import { AuthProvider } from '@/auth/AuthContext';
import { RealtimeProvider } from '@/features/realtime/RealtimeProvider';
import { ToastProvider } from '@/components/toast/ToastContext';
import { ApiError } from '@/api/client';
import i18n, { initI18n } from '@/i18n/config';
import './styles.css';

// Wave 3 i18n (ADR-0022): initialize the shared i18n singleton before rendering. Language =
// localStorage → 'uk' fallback here; the user's profile `locale` from /me is applied on bootstrap
// inside AuthProvider. Resources are bundled (no CDN) so init is synchronous.
initI18n();

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Business data changes through our own mutations; refetch is driven by
      // explicit invalidation. Keep things fresh enough but avoid chatty refetch.
      staleTime: 10_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        // Never retry auth/validation/conflict errors; only transient ones.
        if (error instanceof ApiError) {
          if (error.status >= 400 && error.status < 500) return false;
        }
        return failureCount < 2;
      },
    },
    mutations: {
      retry: false,
    },
  },
});

const rootElement = document.getElementById('root');
if (!rootElement) throw new Error('Root element #root not found');

createRoot(rootElement).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <ToastProvider>
            <AuthProvider>
              <RealtimeProvider>
                <App />
              </RealtimeProvider>
            </AuthProvider>
          </ToastProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </I18nextProvider>
  </StrictMode>,
);
