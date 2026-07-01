import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http } from 'msw';
import { renderWithProviders, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, emptyDashboard, errorEnvelope, sampleDashboard } from '@/test/handlers';
import type { ChartData } from 'chart.js';

// react-chartjs-2 draws on a <canvas>, which jsdom does not implement. We mock the three chart
// components with lightweight stubs that render their data/props into the DOM (a testid + a JSON dump
// of the numeric values) so the tests assert the DATA the page feeds each chart WITHOUT opening a real
// canvas/WebGL context (Wave 3 test constraint, §11 C / hard constraint #4).
vi.mock('react-chartjs-2', () => {
  const stub = (kind: string) =>
    function ChartStub({ data, 'aria-label': ariaLabel }: { data: ChartData; 'aria-label'?: string }) {
      const values = data.datasets?.[0]?.data ?? [];
      return (
        <div
          data-testid={`chart-${kind}`}
          data-aria-label={ariaLabel}
          data-values={JSON.stringify(values)}
        >
          {String(ariaLabel)}
        </div>
      );
    };
  return { Bar: stub('bar'), Line: stub('line'), Doughnut: stub('doughnut') };
});

// Import AFTER the mock so charts.tsx picks up the stubbed react-chartjs-2.
import { AnalyticsPage } from './AnalyticsPage';

describe('AnalyticsPage', () => {
  it('renders summary tiles and charts from the dashboard payload', async () => {
    seedAuthToken('t');
    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });

    // Summary tiles reflect the fixture (open 29, done 8, overdue 3, avg cycle 6.4d).
    expect(await screen.findByText('Overdue')).toBeInTheDocument();
    expect(screen.getByText('29')).toBeInTheDocument(); // open
    expect(screen.getByText('6.4d')).toBeInTheDocument(); // avg cycle time
    expect(screen.getByText(/median 5\.0d · n=8/)).toBeInTheDocument();

    // Every chart card is present (by its accessible section label).
    expect(screen.getByRole('region', { name: 'Tickets by state' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Throughput (done per week)' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Tickets by type' })).toBeInTheDocument();
  });

  it('feeds the by-state bar chart the correct per-state counts', async () => {
    seedAuthToken('t');
    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });

    const bars = await screen.findAllByTestId('chart-bar');
    const byState = bars.find(
      (el) => el.getAttribute('data-aria-label') === 'Bar chart of tickets by workflow state',
    );
    expect(byState).toBeDefined();
    // The values are the fixture's byState counts in workflow order [new, rfi, in_progress, rfa, done].
    expect(JSON.parse(byState!.getAttribute('data-values')!)).toEqual([10, 6, 8, 5, 8]);
  });

  it('feeds the throughput line chart the weekly done counts', async () => {
    seedAuthToken('t');
    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });

    const line = await screen.findByTestId('chart-line');
    expect(JSON.parse(line.getAttribute('data-values')!)).toEqual(
      sampleDashboard.throughput.map((b) => b.doneCount),
    );
  });

  it('highlights states that are over their WIP limit', async () => {
    seedAuthToken('t');
    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });

    // in_progress is 8/3 in the fixture → over limit; the row carries the "Over limit" badge.
    await screen.findByRole('region', { name: 'Work in progress vs limit' });
    const badges = screen.getAllByText('Over limit');
    expect(badges.length).toBeGreaterThanOrEqual(1);
  });

  it('shows a loading state while the dashboard is fetching', async () => {
    seedAuthToken('t');
    // Never-resolving handler keeps the query pending so the loading branch renders.
    server.use(http.get(`${API}/analytics/dashboard`, () => new Promise(() => {})));

    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });
    expect(await screen.findByText('Loading dashboard…')).toBeInTheDocument();
  });

  it('shows an error state with retry when the dashboard call fails', async () => {
    seedAuthToken('t');
    server.use(
      http.get(`${API}/analytics/dashboard`, () =>
        errorEnvelope(403, 'forbidden', 'You are not allowed to perform this action.'),
      ),
    );

    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });
    expect(await screen.findByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('shows an empty state for a team with no tickets', async () => {
    seedAuthToken('t');
    server.use(http.get(`${API}/analytics/dashboard`, () => HttpResponseJson(emptyDashboard)));

    renderWithProviders(<AnalyticsPage />, { initialEntries: ['/analytics'] });
    expect(await screen.findByText('No tickets to report on')).toBeInTheDocument();
  });
});

// Small helper so the empty-team override reads cleanly (MSW json response).
function HttpResponseJson<T>(body: T) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}
