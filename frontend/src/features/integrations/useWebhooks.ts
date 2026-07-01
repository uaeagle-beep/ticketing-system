// Team-scoped webhook subscriptions (Wave 3, ADR-0021, §5.5). Subscriptions are member-managed; the
// backend enforces M(team) on every call. This hook powers the webhooks management panel on the Teams
// page. Mutations invalidate the team's subscription list so the panel refreshes. The delivery audit is a
// separate query keyed by subscription id.

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { webhooksApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type {
  CreateWebhookRequest,
  CreateWebhookResponse,
  UpdateWebhookRequest,
  UpdateWebhookResponse,
  WebhookSubscription,
} from '@/api/types';

/** List a team's webhook subscriptions. Disabled until a teamId is given. */
export function useWebhooks(teamId: string | undefined) {
  return useQuery({
    queryKey: teamId ? queryKeys.webhooks(teamId) : ['webhooks', 'none'],
    queryFn: ({ signal }) => webhooksApi.list(teamId as string, signal),
    enabled: !!teamId,
    staleTime: 30_000,
  });
}

/** A subscription's delivery audit (newest-first). Disabled until enabled (e.g. the drawer is open). */
export function useWebhookDeliveries(subscriptionId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: subscriptionId ? queryKeys.webhookDeliveries(subscriptionId) : ['webhook-deliveries', 'none'],
    queryFn: ({ signal }) => webhooksApi.deliveries(subscriptionId as string, undefined, signal),
    enabled: !!subscriptionId && enabled,
    staleTime: 5_000,
  });
}

/** Create / update / delete / ping mutations for a team's subscriptions. `teamId` scopes invalidations. */
export function useWebhookMutations(teamId: string | undefined) {
  const queryClient = useQueryClient();

  const invalidate = () => {
    if (!teamId) return;
    queryClient.invalidateQueries({ queryKey: queryKeys.webhooks(teamId) });
  };

  const create = useMutation({
    mutationFn: (body: CreateWebhookRequest): Promise<CreateWebhookResponse> =>
      webhooksApi.create(teamId as string, body),
    onSuccess: invalidate,
  });

  const update = useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateWebhookRequest }): Promise<UpdateWebhookResponse> =>
      webhooksApi.update(id, body),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (id: string): Promise<void> => webhooksApi.remove(id),
    onSuccess: invalidate,
  });

  const ping = useMutation({
    mutationFn: (id: string) => webhooksApi.ping(id),
    onSuccess: (_res, id) => {
      // Refresh the deliveries drawer for this subscription so the new ping appears.
      queryClient.invalidateQueries({ queryKey: queryKeys.webhookDeliveries(id) });
    },
  });

  return { create, update, remove, ping };
}

export type { WebhookSubscription };
