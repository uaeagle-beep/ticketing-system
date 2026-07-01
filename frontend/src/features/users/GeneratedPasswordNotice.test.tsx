import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GeneratedPasswordNotice } from './GeneratedPasswordNotice';

// Unit tests for the one-time generated-password display ([ПРИПУЩЕННЯ UM-5], SEC-4). The password is
// shown verbatim exactly once and a Copy button writes it to the clipboard. Copy behaviour is only
// exercised indirectly elsewhere; here we assert the clipboard write and the transient "Copied" state.

describe('GeneratedPasswordNotice', () => {
  const writeText = vi.fn().mockResolvedValue(undefined);

  function stubClipboard(fn: (text: string) => Promise<void>) {
    // jsdom's navigator.clipboard is a getter-only property, so assign via defineProperty.
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: fn },
    });
  }

  beforeEach(() => {
    writeText.mockClear();
    stubClipboard(writeText);
  });

  it('shows the password once with the one-time warning', () => {
    render(<GeneratedPasswordNotice password="Xk9$mPq2vLr7Wn4t" />);
    expect(screen.getByTestId('generated-password')).toHaveTextContent('Xk9$mPq2vLr7Wn4t');
    expect(screen.getByText(/shown only once/i)).toBeInTheDocument();
  });

  it('copies the password to the clipboard and flips the button to "Copied"', async () => {
    // userEvent.setup() installs its own clipboard stub; disable that so our spy is the one used.
    const user = userEvent.setup({ writeToClipboard: false });
    stubClipboard(writeText);
    render(<GeneratedPasswordNotice password="Nw7&pQz3xKr9Vm2t" />);

    await user.click(screen.getByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenCalledWith('Nw7&pQz3xKr9Vm2t');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument());
  });

  it('keeps the value visible when the clipboard write fails (insecure context)', async () => {
    const user = userEvent.setup({ writeToClipboard: false });
    const failing = vi.fn().mockRejectedValue(new Error('denied'));
    stubClipboard(failing);
    render(<GeneratedPasswordNotice password="StillVisible123" />);

    await user.click(screen.getByRole('button', { name: 'Copy' }));

    // The clipboard rejected, but the password stays on screen so the admin can copy it manually.
    expect(screen.getByTestId('generated-password')).toHaveTextContent('StillVisible123');
    // The label does not get stuck on "Copied" after a failure.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument());
  });
});
