import { useQuery } from '@tanstack/react-query';
import { adminUsersApi } from '@/api/endpoints';

// Query key for the admin user list (admin-only zone).
export const usersQueryKey = ['admin', 'users'] as const;

// Admin user list (GET /api/admin/users). Used by the Users management screen.
export function useUsers() {
  return useQuery({
    queryKey: usersQueryKey,
    queryFn: ({ signal }) => adminUsersApi.list(signal),
  });
}
