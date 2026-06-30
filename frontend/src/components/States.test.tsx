import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LoadingState, EmptyState, ErrorState } from './States';

describe('LoadingState', () => {
  it('renders a polite status with the default label', () => {
    render(<LoadingState />);
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute('aria-live', 'polite');
    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('renders a custom label', () => {
    render(<LoadingState label="Loading your board…" />);
    expect(screen.getByText('Loading your board…')).toBeInTheDocument();
  });
});

describe('EmptyState', () => {
  it('renders the title and optional message and action', () => {
    render(
      <EmptyState
        title="No teams yet"
        message="Create your first team to get started."
        action={<button type="button">New team</button>}
      />,
    );
    expect(screen.getByRole('heading', { name: 'No teams yet' })).toBeInTheDocument();
    expect(screen.getByText('Create your first team to get started.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New team' })).toBeInTheDocument();
  });

  it('renders without a message or action', () => {
    render(<EmptyState title="Nothing here" />);
    expect(screen.getByRole('heading', { name: 'Nothing here' })).toBeInTheDocument();
  });
});

describe('ErrorState', () => {
  it('renders an alert with the error message', () => {
    render(<ErrorState message="Could not load the board." />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('Could not load the board.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Something went wrong' })).toBeInTheDocument();
  });

  it('shows a retry button only when onRetry is provided and invokes it', async () => {
    const onRetry = vi.fn();
    const user = userEvent.setup();
    render(<ErrorState message="boom" onRetry={onRetry} />);
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('hides the retry button when onRetry is omitted', () => {
    render(<ErrorState message="boom" />);
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });
});
