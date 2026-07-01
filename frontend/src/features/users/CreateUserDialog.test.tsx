import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { QueryClientProvider } from '@tanstack/react-query';
import { CreateUserDialog } from './CreateUserDialog';
import { ToastProvider } from '@/components/toast/ToastContext';
import { makeTestQueryClient } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleMemberUser, sampleTeam } from '@/test/handlers';
import type { CreateUserRequest, Team } from '@/api/types';

// Direct-mount tests for the CreateUserDialog (API_CONTRACT §8.2). The UsersPage suite only proves
// the happy auto-generated path; these close the gaps: client validation, admin toggle + team
// selection + name are actually sent, the chosen-password path closes with a toast (no password
// screen), and server errors (email_in_use / validation_error) surface. The request body captured by
// MSW is asserted so we prove the DIALOG maps the form to the contract, not just that a call happened.

const platform: Team = { ...sampleTeam };
const payments: Team = { ...sampleTeam, id: 'team-payments', name: 'Payments' };

function mount(teams: Team[] = [platform, payments]) {
  const client = makeTestQueryClient();
  const user = userEvent.setup();
  const onClose = vi.fn();
  render(
    <QueryClientProvider client={client}>
      <ToastProvider>
        <CreateUserDialog teams={teams} onClose={onClose} />
      </ToastProvider>
    </QueryClientProvider>,
  );
  return { user, onClose };
}

describe('CreateUserDialog', () => {
  it('disables the submit button while the email is blank (cannot create without an email)', async () => {
    const { user } = mount();
    const submit = screen.getByRole('button', { name: 'Create user' });

    // Empty email → disabled.
    expect(submit).toBeDisabled();
    // Whitespace-only email → still disabled (email is required after trim, §8.2).
    await user.type(screen.getByLabelText('Email'), '   ');
    expect(submit).toBeDisabled();
    // A real email enables it.
    await user.type(screen.getByLabelText('Email'), 'newdev@dataart.com');
    expect(submit).toBeEnabled();
  });

  it('shows a client-side error when a chosen password is shorter than 8 and does not call the API', async () => {
    let called = false;
    server.use(
      http.post(`${API}/admin/users`, () => {
        called = true;
        return HttpResponse.json({ user: sampleMemberUser, generatedPassword: null }, { status: 201 });
      }),
    );
    const { user } = mount();

    await user.type(screen.getByLabelText('Email'), 'newdev@dataart.com');
    // Turn off auto-generate → the password field appears.
    await user.click(screen.getByRole('checkbox', { name: /Generate a strong password/ }));
    await user.type(screen.getByLabelText('Password'), 'short');
    await user.click(screen.getByRole('button', { name: 'Create user' }));

    expect(
      await screen.findByText('Password must be at least 8 characters, or choose auto-generate.'),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('sends name, isAdmin and selected teamIds and closes with a toast on a chosen-password success', async () => {
    let captured: CreateUserRequest | null = null;
    server.use(
      http.post(`${API}/admin/users`, async ({ request }) => {
        captured = (await request.json()) as CreateUserRequest;
        // Admin supplied the password → generatedPassword is null (no one-time-password screen).
        return HttpResponse.json({ user: sampleMemberUser, generatedPassword: null }, { status: 201 });
      }),
    );
    const { user, onClose } = mount();

    await user.type(screen.getByLabelText('Email'), 'chosen@dataart.com');
    await user.type(screen.getByLabelText('Name'), 'Chosen Person');
    await user.click(screen.getByRole('checkbox', { name: /Generate a strong password/ }));
    await user.type(screen.getByLabelText('Password'), 'a-chosen-password-1');
    await user.click(screen.getByRole('checkbox', { name: /Administrator/ }));
    // Select the Payments team checkbox.
    const teamGroup = screen.getByRole('group', { name: 'Teams' });
    await user.click(within(teamGroup).getByLabelText('Payments'));

    await user.click(screen.getByRole('button', { name: 'Create user' }));

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(captured).toEqual({
      email: 'chosen@dataart.com',
      name: 'Chosen Person',
      password: 'a-chosen-password-1',
      isAdmin: true,
      teamIds: [payments.id],
    });
    // No one-time password screen for a chosen password.
    expect(screen.queryByTestId('generated-password')).not.toBeInTheDocument();
  });

  it('sends password:null and name:null when auto-generate is on and the name is blank', async () => {
    let captured: CreateUserRequest | null = null;
    server.use(
      http.post(`${API}/admin/users`, async ({ request }) => {
        captured = (await request.json()) as CreateUserRequest;
        return HttpResponse.json(
          { user: sampleMemberUser, generatedPassword: 'Xk9$mPq2vLr7Wn4t' },
          { status: 201 },
        );
      }),
    );
    const { user } = mount();

    await user.type(screen.getByLabelText('Email'), '  spaced@dataart.com  ');
    await user.click(screen.getByRole('button', { name: 'Create user' }));

    // The one-time password screen appears for a generated password.
    expect(await screen.findByTestId('generated-password')).toHaveTextContent('Xk9$mPq2vLr7Wn4t');
    expect(captured).toEqual({
      email: 'spaced@dataart.com', // trimmed
      name: null, // blank → null
      password: null, // auto-generate → null
      isAdmin: false,
      teamIds: [],
    });
  });

  it('surfaces a server email_in_use (409) via a toast without a field-error banner', async () => {
    server.use(
      http.post(`${API}/admin/users`, () =>
        HttpResponse.json(
          { error: { code: 'email_in_use', message: 'A user with this email already exists.' } },
          { status: 409 },
        ),
      ),
    );
    const { user, onClose } = mount();

    await user.type(screen.getByLabelText('Email'), 'dup@dataart.com');
    await user.click(screen.getByRole('button', { name: 'Create user' }));

    // Surfaced as a toast (role=alert), dialog stays open (not closed).
    expect(await screen.findByText('A user with this email already exists.')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('surfaces a server validation_error as an inline field-error banner', async () => {
    server.use(
      http.post(`${API}/admin/users`, () =>
        HttpResponse.json(
          {
            error: {
              code: 'validation_error',
              message: 'One or more fields are invalid.',
              errors: { name: ['Name must be at most 100 characters.'] },
            },
          },
          { status: 400 },
        ),
      ),
    );
    const { user } = mount();

    await user.type(screen.getByLabelText('Email'), 'x@dataart.com');
    await user.click(screen.getByRole('button', { name: 'Create user' }));

    // The per-field text is shown inside the dialog's error banner.
    expect(await screen.findByText('Name must be at most 100 characters.')).toBeInTheDocument();
  });

  it('hides the password field while auto-generate is checked', async () => {
    mount();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });
});
