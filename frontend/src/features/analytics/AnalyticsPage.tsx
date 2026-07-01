// Analytics / reporting dashboard (Wave 3, ADR-0020, §10.3).
//
// Responsibilities:
//  - Team selector (a member sees their teams; admin can pick any). Selection persisted in the URL
//    ?team= so refresh/links keep it, mirroring the board.
//  - Date range: quick presets (last 4/12/26 weeks, YTD) plus custom from/to inputs.
//  - Summary stat tiles (open/done, overdue, cycle time) + charts (bar/line/doughnut) for the ~nine
//    metrics, all rendered from ONE pre-aggregated round-trip (the client plots ≤ a few dozen points).
//  - Distinct loading / error / empty (team with no tickets) states (NFR-USE-1).

import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { Dashboard, DashboardRange, TicketPriority, TicketState, TicketType } from '@/api/types';
import { priorityLabel, stateLabel, typeLabel } from '@/lib/labels';
import { useTeams } from '@/features/teams/useTeams';
import { useDashboard } from './useDashboard';
import { BarChart, DoughnutChart, LineChart } from './charts';
import { LoadingState, EmptyState, ErrorState } from '@/components/States';
import { errorMessage } from '@/lib/errors';

// Remember the last analytics team like the board does (localStorage is a convenience; the server is
// authoritative about which teams are accessible).
const LAST_TEAM_KEY = 'tt.analytics.lastTeamId';
function readLastTeamId(): string | null {
  try {
    return window.localStorage.getItem(LAST_TEAM_KEY);
  } catch {
    return null;
  }
}
function writeLastTeamId(teamId: string): void {
  try {
    window.localStorage.setItem(LAST_TEAM_KEY, teamId);
  } catch {
    /* private mode — selection just won't persist */
  }
}

// Quick presets computed relative to "today" (client clock; the server still bounds the range).
type PresetKey = '4w' | '12w' | '26w' | 'ytd' | 'custom';
const PRESET_KEYS: ReadonlyArray<PresetKey> = ['4w', '12w', '26w', 'ytd', 'custom'];

function isoDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function presetRange(key: PresetKey): DashboardRange {
  const today = new Date();
  const to = isoDay(today);
  if (key === 'ytd') {
    return { from: `${today.getUTCFullYear()}-01-01`, to };
  }
  const weeks = key === '4w' ? 4 : key === '26w' ? 26 : 12;
  const from = new Date(today);
  from.setUTCDate(from.getUTCDate() - weeks * 7);
  return { from: isoDay(from), to };
}

function round1(value: number): string {
  return value.toFixed(1);
}

export function AnalyticsPage() {
  const { t } = useTranslation('analytics');
  const [searchParams, setSearchParams] = useSearchParams();

  const teamsQuery = useTeams();
  const teams = teamsQuery.data ?? [];

  const teamParam = searchParams.get('team') ?? undefined;
  const selectedTeamId = useMemo(() => {
    if (teamParam && teams.some((t) => t.id === teamParam)) return teamParam;
    const lastTeamId = readLastTeamId();
    if (lastTeamId && teams.some((t) => t.id === lastTeamId)) return lastTeamId;
    return teams[0]?.id;
  }, [teamParam, teams]);

  useEffect(() => {
    if (selectedTeamId) writeLastTeamId(selectedTeamId);
  }, [selectedTeamId]);

  const [preset, setPreset] = useState<PresetKey>('12w');
  const [customRange, setCustomRange] = useState<DashboardRange>({});

  // The effective range sent to the server: a preset, or the custom inputs (each side optional).
  const range: DashboardRange = preset === 'custom' ? customRange : presetRange(preset);

  const dashboardQuery = useDashboard(selectedTeamId, range);
  const dashboard = dashboardQuery.data;

  const handleTeamChange = (teamId: string) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('team', teamId);
      return next;
    });
  };

  // ---- Render branches ----

  if (teamsQuery.isLoading) {
    return <LoadingState label={t('loadingTeams')} />;
  }
  if (teamsQuery.isError) {
    return <ErrorState message={errorMessage(teamsQuery.error)} onRetry={() => teamsQuery.refetch()} />;
  }
  if (teams.length === 0) {
    return (
      <EmptyState
        title={t('noTeams.title')}
        message={t('noTeams.message')}
      />
    );
  }

  return (
    <div>
      <div className="board-toolbar">
        <div className="field" style={{ margin: 0 }}>
          <select
            className="select"
            aria-label={t('selectTeam')}
            value={selectedTeamId ?? ''}
            onChange={(e) => handleTeamChange(e.target.value)}
          >
            {teams.map((team) => (
              <option key={team.id} value={team.id}>
                {team.name}
              </option>
            ))}
          </select>
        </div>
        <div className="field" style={{ margin: 0 }}>
          <select
            className="select"
            aria-label={t('dateRange')}
            value={preset}
            onChange={(e) => setPreset(e.target.value as PresetKey)}
          >
            {PRESET_KEYS.map((key) => (
              <option key={key} value={key}>
                {t(`presets.${key}`)}
              </option>
            ))}
          </select>
        </div>
        {preset === 'custom' ? (
          <>
            <div className="field" style={{ margin: 0 }}>
              <input
                type="date"
                className="select"
                aria-label={t('fromDate')}
                value={customRange.from ?? ''}
                onChange={(e) => setCustomRange((r) => ({ ...r, from: e.target.value || undefined }))}
              />
            </div>
            <div className="field" style={{ margin: 0 }}>
              <input
                type="date"
                className="select"
                aria-label={t('toDate')}
                value={customRange.to ?? ''}
                onChange={(e) => setCustomRange((r) => ({ ...r, to: e.target.value || undefined }))}
              />
            </div>
          </>
        ) : null}
        <div className="spacer" />
      </div>

      {dashboardQuery.isError ? (
        <ErrorState
          message={errorMessage(dashboardQuery.error)}
          onRetry={() => dashboardQuery.refetch()}
        />
      ) : dashboardQuery.isLoading && !dashboard ? (
        <LoadingState label={t('loadingDashboard')} />
      ) : dashboard ? (
        <DashboardView dashboard={dashboard} />
      ) : null}
    </div>
  );
}

function DashboardView({ dashboard }: { dashboard: Dashboard }) {
  const { t } = useTranslation('analytics');
  const totalTickets = Object.values(dashboard.byState).reduce((a, b) => a + b, 0);

  // A team with no tickets: everything is zero — show a single empty state, not blank charts.
  if (totalTickets === 0) {
    return (
      <EmptyState
        title={t('noTickets.title')}
        message={t('noTickets.message')}
      />
    );
  }

  const states = Object.keys(dashboard.byState) as TicketState[];
  const priorities = Object.keys(dashboard.byPriority) as TicketPriority[];
  const types = Object.keys(dashboard.byType) as TicketType[];
  const cycle = dashboard.cycleTime;

  return (
    <div className="analytics-dash">
      {/* Summary stat tiles. */}
      <div className="stat-tiles">
        <StatTile label={t('tiles.open')} value={String(dashboard.openVsDone.open)} />
        <StatTile label={t('tiles.done')} value={String(dashboard.openVsDone.done)} />
        <StatTile label={t('tiles.overdue')} value={String(dashboard.overdueCount)} tone="danger" />
        <StatTile
          label={t('tiles.avgCycleTime')}
          value={cycle.avgDays !== null ? `${round1(cycle.avgDays)}d` : '—'}
          hint={t('cycle.hint', {
            median: cycle.medianDays !== null ? `${round1(cycle.medianDays)}d` : '—',
            n: cycle.sampleSize,
          })}
        />
      </div>

      {/* Charts grid. */}
      <div className="analytics-grid">
        <ChartCard title={t('charts.byState')}>
          <BarChart
            label="Tickets"
            categories={states.map(stateLabel)}
            values={states.map((s) => dashboard.byState[s])}
            ariaLabel={t('aria.byState')}
          />
        </ChartCard>

        <ChartCard title={t('charts.byPriority')}>
          <BarChart
            label="Tickets"
            categories={priorities.map(priorityLabel)}
            values={priorities.map((p) => dashboard.byPriority[p])}
            ariaLabel={t('aria.byPriority')}
          />
        </ChartCard>

        <ChartCard title={t('charts.byType')}>
          <DoughnutChart
            categories={types.map(typeLabel)}
            values={types.map((ty) => dashboard.byType[ty])}
            ariaLabel={t('aria.byType')}
          />
        </ChartCard>

        <ChartCard title={t('charts.openVsDone')}>
          <DoughnutChart
            categories={[t('tiles.open'), t('tiles.done')]}
            values={[dashboard.openVsDone.open, dashboard.openVsDone.done]}
            colors={['#f59e0b', '#22c55e']}
            ariaLabel={t('aria.openVsDone')}
          />
        </ChartCard>

        <ChartCard title={t('charts.throughput')}>
          {dashboard.throughput.length > 0 ? (
            <LineChart
              label="Done"
              categories={dashboard.throughput.map((b) => b.weekStart)}
              values={dashboard.throughput.map((b) => b.doneCount)}
              ariaLabel={t('aria.throughput')}
            />
          ) : (
            <p className="muted">{t('empty.noThroughput')}</p>
          )}
        </ChartCard>

        <ChartCard title={t('charts.byLabel')}>
          {dashboard.byLabel.length > 0 ? (
            <BarChart
              label="Tickets"
              categories={dashboard.byLabel.map((l) => l.name)}
              values={dashboard.byLabel.map((l) => l.count)}
              colors={dashboard.byLabel.map((l) => l.color)}
              ariaLabel={t('aria.byLabel')}
            />
          ) : (
            <p className="muted">{t('empty.noLabels')}</p>
          )}
        </ChartCard>

        <ChartCard title={t('charts.wip')}>
          <div className="wip-report">
            {dashboard.wip.map((w) => (
              <div key={w.state} className={`wip-report-row${w.overLimit ? ' over-limit' : ''}`}>
                <span className="wip-report-state">{stateLabel(w.state)}</span>
                <span className="wip-report-count">
                  {w.count}
                  {w.limit !== null ? ` / ${w.limit}` : ''}
                </span>
                {w.overLimit ? <span className="wip-report-badge">{t('overLimit')}</span> : null}
              </div>
            ))}
          </div>
        </ChartCard>
      </div>
    </div>
  );
}

function StatTile({
  label,
  value,
  hint,
  tone,
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: 'danger';
}) {
  return (
    <div className="stat-tile">
      <div className="stat-tile-label">{label}</div>
      <div className={`stat-tile-value${tone === 'danger' ? ' danger' : ''}`}>{value}</div>
      {hint ? <div className="stat-tile-hint">{hint}</div> : null}
    </div>
  );
}

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="chart-card panel" aria-label={title}>
      <h3 className="chart-card-title">{title}</h3>
      <div className="chart-card-body">{children}</div>
    </section>
  );
}
