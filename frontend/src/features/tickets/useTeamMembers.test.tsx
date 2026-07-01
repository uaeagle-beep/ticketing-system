// useTeamMembers (Wave 2 §8bis / ADR-0017) — sources the assignee-candidate pool from the
// member-visible GET /api/teams/{id}/members endpoint, so a NON-admin member gets candidates too
// (closing the Wave-1 gap). Verifies the hook hits that route, maps TeamMember[] -> AssigneeRef[]
// (sorted by displayName), and returns an empty, non-loading pool when no teamId is given.

import { describe, expect, it } from 'vitest';
import type { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import { http, HttpResponse } from 'msw';
import { useTeamMembers } from './useTeamMembers';
import type { TeamMember } from '@/api/types';
import { makeTestQueryClient } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

function wrapper() {
  const client = makeTestQueryClient();
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { Wrapper };
}

describe('useTeamMembers', () => {
  it('fetches team members and maps them to sorted AssigneeRef candidates', async () => {
    const members: TeamMember[] = [
      { id: 'u-charlie', displayName: 'charlie@dataart.com', isAdmin: false },
      { id: 'u-alice', displayName: 'alice@dataart.com', isAdmin: false },
    ];
    let requestedTeamId: string | null = null;
    server.use(
      http.get(`${API}/teams/:id/members`, ({ params }) => {
        requestedTeamId = String(params.id);
        return HttpResponse.json(members, { status: 200 });
      }),
    );

    const { Wrapper } = wrapper();
    const { result } = renderHook(() => useTeamMembers('team-42'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.candidates.length).toBe(2));
    expect(requestedTeamId).toBe('team-42');
    expect(result.current.canList).toBe(true);
    // Sorted by displayName.
    expect(result.current.candidates.map((c) => c.displayName)).toEqual([
      'alice@dataart.com',
      'charlie@dataart.com',
    ]);
  });

  it('returns an empty, non-loading, non-listable pool when no teamId is given', () => {
    const { Wrapper } = wrapper();
    const { result } = renderHook(() => useTeamMembers(undefined), { wrapper: Wrapper });

    expect(result.current.candidates).toEqual([]);
    expect(result.current.isLoading).toBe(false);
    expect(result.current.canList).toBe(false);
  });
});
