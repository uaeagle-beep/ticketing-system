// Team-scoped labels (Wave 2, ADR-0016). A team's labels are member-visible and member-managed; the
// backend enforces M(team) on every call. This hook powers both the label picker on the ticket form and
// the label management surface. Mutations invalidate the team's label list and any affected board so
// chips/filters refresh.

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { labelsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import type { CreateLabelRequest, Label, UpdateLabelRequest } from '@/api/types';

/** List a team's labels (ordered by name server-side). Disabled until a teamId is given. */
export function useLabels(teamId: string | undefined) {
  return useQuery({
    queryKey: teamId ? queryKeys.labels(teamId) : ['labels', 'none'],
    queryFn: ({ signal }) => labelsApi.list(teamId as string, signal),
    enabled: !!teamId,
    staleTime: 60_000,
  });
}

/**
 * Create / rename / delete mutations for a team's labels. Each invalidates the team's label list and the
 * team's board (chips + label filter read from those). `teamId` scopes the invalidations.
 */
export function useLabelMutations(teamId: string | undefined) {
  const queryClient = useQueryClient();

  const invalidate = () => {
    if (!teamId) return;
    queryClient.invalidateQueries({ queryKey: queryKeys.labels(teamId) });
    queryClient.invalidateQueries({ queryKey: ['board', teamId] });
  };

  const create = useMutation({
    mutationFn: (body: CreateLabelRequest): Promise<Label> => labelsApi.create(body),
    onSuccess: invalidate,
  });

  const update = useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateLabelRequest }): Promise<Label> =>
      labelsApi.update(id, body),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (id: string) => labelsApi.remove(id),
    onSuccess: invalidate,
  });

  return { create, update, remove };
}
