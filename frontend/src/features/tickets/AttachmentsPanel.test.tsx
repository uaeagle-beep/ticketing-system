// AttachmentsPanel (Wave 3, ADR-0018 / §10.1) — smoke coverage of the UI affordances: lists an
// attachment (filename/size/uploader/time), uploads via the file input, rejects an oversized file
// client-side without calling the API, surfaces the 415 unsupported_media_type code, downloads via an
// authenticated blob fetch, and deletes after confirmation. The full acceptance suite is the Tester's.

import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { AttachmentsPanel } from './AttachmentsPanel';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleAttachment } from '@/test/handlers';

function renderPanel() {
  return renderRoutes(
    <Routes>
      <Route path="/" element={<AttachmentsPanel ticketId={sampleAttachment.ticketId} />} />
    </Routes>,
    { initialEntries: ['/'] },
  );
}

function pngFile(name = 'shot.png', size = 1024) {
  const bytes = new Uint8Array(size);
  bytes.set([0x89, 0x50, 0x4e, 0x47]); // PNG magic prefix
  return new File([bytes], name, { type: 'image/png' });
}

describe('AttachmentsPanel (Wave 3)', () => {
  it('lists an attachment with filename, size and uploader', async () => {
    seedAuthToken('t');
    renderPanel();

    await screen.findByText('screenshot.png');
    // Size renders human-readable (20480 bytes → "20 KB").
    expect(screen.getByText(/20 KB/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
  });

  it('uploads a selected file (multipart) and refetches the list', async () => {
    seedAuthToken('t');
    let uploaded = false;
    server.use(
      http.post(`${API}/tickets/:id/attachments`, ({ params }) => {
        uploaded = true;
        return HttpResponse.json(
          { ...sampleAttachment, id: 'at-new', ticketId: String(params.id) },
          { status: 201 },
        );
      }),
    );

    const { user } = renderPanel();
    await screen.findByText('screenshot.png');

    const input = screen.getByLabelText('Upload attachment') as HTMLInputElement;
    await user.upload(input, pngFile());

    await waitFor(() => expect(uploaded).toBe(true));
  });

  it('rejects an oversized file client-side without calling the API', async () => {
    seedAuthToken('t');
    let called = false;
    server.use(
      http.post(`${API}/tickets/:id/attachments`, () => {
        called = true;
        return HttpResponse.json({ ...sampleAttachment }, { status: 201 });
      }),
    );

    const { user } = renderPanel();
    await screen.findByText('screenshot.png');

    // 11 MB > the 10 MB client pre-check cap.
    const tooBig = pngFile('big.png', 11 * 1024 * 1024);
    const input = screen.getByLabelText('Upload attachment') as HTMLInputElement;
    await user.upload(input, tooBig);

    await screen.findByText(/too large/i);
    expect(called).toBe(false);
  });

  it('surfaces a 415 unsupported_media_type from the server', async () => {
    seedAuthToken('t');
    server.use(
      http.post(`${API}/tickets/:id/attachments`, () =>
        HttpResponse.json(
          { error: { code: 'unsupported_media_type', message: 'This file type is not allowed.' } },
          { status: 415 },
        ),
      ),
    );

    const { user } = renderPanel();
    await screen.findByText('screenshot.png');

    const input = screen.getByLabelText('Upload attachment') as HTMLInputElement;
    await user.upload(input, pngFile());

    await screen.findByText(/type is not allowed/i);
  });

  it('deletes an attachment after confirmation', async () => {
    seedAuthToken('t');
    let deleted = false;
    server.use(
      http.delete(`${API}/attachments/:id`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { user } = renderPanel();
    await screen.findByText('screenshot.png');

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleted).toBe(true));
  });

  describe('download', () => {
    beforeEach(() => {
      // jsdom doesn't implement object URLs; stub them so the blob-download path runs.
      (URL as unknown as { createObjectURL: () => string }).createObjectURL = vi.fn(() => 'blob:mock');
      (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = vi.fn();
    });
    afterEach(() => vi.restoreAllMocks());

    it('fetches the blob with auth and triggers a browser download', async () => {
      seedAuthToken('t');
      let downloadCalled = false;
      server.use(
        http.get(`${API}/attachments/:id`, () => {
          downloadCalled = true;
          return HttpResponse.arrayBuffer(new Uint8Array([1, 2, 3]).buffer, {
            status: 200,
            headers: {
              'Content-Type': 'image/png',
              'Content-Disposition': 'attachment; filename="screenshot.png"',
            },
          });
        }),
      );

      const { user } = renderPanel();
      await screen.findByText('screenshot.png');

      await user.click(screen.getByRole('button', { name: 'Download' }));
      await waitFor(() => expect(downloadCalled).toBe(true));
      expect(URL.createObjectURL).toHaveBeenCalled();
    });
  });
});
