// Self-service API keys (Wave 3, ADR-0021, §5.6). A user's personal access tokens; the backend scopes
// every call to the authenticated caller (Self). This hook powers the API-keys section on the Account
// page. Create reveals the raw ptk_ key once; revoke is idempotent. Mutations invalidate the key list.

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiKeysApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { ApiKey, CreateApiKeyRequest, CreateApiKeyResponse } from '@/api/types';

/** List the caller's API keys (newest-first, active + revoked). */
export function useApiKeys() {
  return useQuery({
    queryKey: queryKeys.apiKeys,
    queryFn: ({ signal }) => apiKeysApi.list(signal),
    staleTime: 30_000,
  });
}

/** Create / revoke mutations for the caller's keys. Both invalidate the key list. */
export function useApiKeyMutations() {
  const queryClient = useQueryClient();
  const invalidate = () => queryClient.invalidateQueries({ queryKey: queryKeys.apiKeys });

  const create = useMutation({
    mutationFn: (body: CreateApiKeyRequest): Promise<CreateApiKeyResponse> => apiKeysApi.create(body),
    onSuccess: invalidate,
  });

  const revoke = useMutation({
    mutationFn: (id: string): Promise<void> => apiKeysApi.revoke(id),
    onSuccess: invalidate,
  });

  return { create, revoke };
}

export type { ApiKey };
