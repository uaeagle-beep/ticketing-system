// Chart.js chart components for the analytics dashboard (Wave 3, ADR-0020, §10.3). Chart.js is bundled
// through npm/Vite (NO external CDN) so the strict CSP (`script-src 'self'`) is respected. This module is
// the ONLY place that touches Chart.js / react-chartjs-2 — components import the thin Bar/Line/Doughnut
// wrappers below, so a unit test can mock `react-chartjs-2` (jsdom has no <canvas>) without a real canvas.
//
// We register only the Chart.js pieces we use (tree-shakeable, keeps the bundle lean).

import {
  Chart as ChartJS,
  ArcElement,
  BarElement,
  CategoryScale,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  Tooltip,
  type ChartData,
  type ChartOptions,
} from 'chart.js';
import { Bar, Doughnut, Line } from 'react-chartjs-2';

ChartJS.register(
  ArcElement,
  BarElement,
  CategoryScale,
  LinearScale,
  LineElement,
  PointElement,
  Legend,
  Tooltip,
);

// A palette for categorical charts (state/priority/type/label). Matches the app's slate/blue family and
// stays readable in both chart types. Reused by index for consistent colours across cards.
export const CHART_PALETTE = [
  '#3b82f6', // blue
  '#22c55e', // green
  '#f59e0b', // amber
  '#ef4444', // red
  '#8b5cf6', // violet
  '#14b8a6', // teal
  '#ec4899', // pink
  '#64748b', // slate
];

interface BarChartProps {
  label: string;
  categories: string[];
  values: number[];
  /** Per-bar colours (defaults to the categorical palette, cycled). */
  colors?: string[];
  ariaLabel: string;
}

export function BarChart({ label, categories, values, colors, ariaLabel }: BarChartProps) {
  const data: ChartData<'bar'> = {
    labels: categories,
    datasets: [
      {
        label,
        data: values,
        backgroundColor: categories.map((_, i) => colors?.[i] ?? CHART_PALETTE[i % CHART_PALETTE.length]),
        borderRadius: 4,
      },
    ],
  };
  const options: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: { y: { beginAtZero: true, ticks: { precision: 0 } } },
  };
  return <Bar data={data} options={options} aria-label={ariaLabel} role="img" />;
}

interface DoughnutChartProps {
  categories: string[];
  values: number[];
  colors?: string[];
  ariaLabel: string;
}

export function DoughnutChart({ categories, values, colors, ariaLabel }: DoughnutChartProps) {
  const data: ChartData<'doughnut'> = {
    labels: categories,
    datasets: [
      {
        data: values,
        backgroundColor: categories.map((_, i) => colors?.[i] ?? CHART_PALETTE[i % CHART_PALETTE.length]),
        borderWidth: 0,
      },
    ],
  };
  const options: ChartOptions<'doughnut'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { position: 'bottom' } },
  };
  return <Doughnut data={data} options={options} aria-label={ariaLabel} role="img" />;
}

interface LineChartProps {
  label: string;
  categories: string[];
  values: number[];
  ariaLabel: string;
}

export function LineChart({ label, categories, values, ariaLabel }: LineChartProps) {
  const data: ChartData<'line'> = {
    labels: categories,
    datasets: [
      {
        label,
        data: values,
        borderColor: CHART_PALETTE[0],
        backgroundColor: 'rgba(59, 130, 246, 0.15)',
        fill: true,
        tension: 0.3,
        pointRadius: 3,
      },
    ],
  };
  const options: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: { y: { beginAtZero: true, ticks: { precision: 0 } } },
  };
  return <Line data={data} options={options} aria-label={ariaLabel} role="img" />;
}
