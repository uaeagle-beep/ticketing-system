// Attachments panel on the ticket detail page (Wave 3, ADR-0018 / §10.1). Lists a ticket's
// attachments (filename, size, uploaded-by, time) with a download and delete action, plus an upload
// control with a friendly client-side pre-check (size/type) — the server stays authoritative (413/415).
//
// Download is authenticated + forced-download (Content-Disposition: attachment), so it cannot be a
// plain <a href>: we fetch the blob with the bearer token, then trigger a browser download via an
// object URL. Upload/delete invalidate the attachments query; the events they raise also touch the
// activity timeline, so we invalidate that too (mirrors CommentsPanel).

import { useRef, useState, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { attachmentsApi } from '@/api/endpoints';
import type { Attachment } from '@/api/types';
import { queryKeys } from '@/lib/queryKeys';
import { formatUtc } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { ATTACHMENT_ACCEPT, formatBytes, precheckFile } from '@/lib/attachments';
import { CountBadge } from '@/components/Badges';
import { LoadingState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';

export function AttachmentsPanel({ ticketId }: { ticketId: string }) {
  const { t } = useTranslation('tickets');
  const queryClient = useQueryClient();
  const toast = useToast();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [downloadingId, setDownloadingId] = useState<string | null>(null);

  const attachmentsQuery = useQuery({
    queryKey: queryKeys.attachments(ticketId),
    queryFn: ({ signal }) => attachmentsApi.list(ticketId, signal),
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => attachmentsApi.upload(ticketId, file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.attachments(ticketId) });
      // attachment_added writes an activity entry (and notifies watchers) — refresh the timeline.
      queryClient.invalidateQueries({ queryKey: queryKeys.activity(ticketId) });
    },
    onError: (err) => toast.showError(errorMessage(err)),
    onSettled: () => {
      // Reset the input so re-selecting the same file fires onChange again.
      if (fileInputRef.current) fileInputRef.current.value = '';
    },
  });

  const attachments = attachmentsQuery.data ?? [];

  const handleFileChange = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const precheckError = precheckFile(file);
    if (precheckError) {
      toast.showError(precheckError);
      if (fileInputRef.current) fileInputRef.current.value = '';
      return;
    }
    uploadMutation.mutate(file);
  };

  const handleDownload = async (att: Attachment) => {
    setDownloadingId(att.id);
    try {
      const blob = await attachmentsApi.download(att.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = att.filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      toast.showError(errorMessage(err));
    } finally {
      setDownloadingId(null);
    }
  };

  return (
    <div className="panel">
      <div className="row" style={{ marginBottom: 12 }}>
        <h3 style={{ fontSize: 16 }}>{t('attachments.title')}</h3>
        <CountBadge count={attachments.length} />
      </div>

      {attachmentsQuery.isLoading ? (
        <LoadingState label={t('attachments.loading')} />
      ) : attachmentsQuery.isError ? (
        <div className="banner banner-error">{errorMessage(attachmentsQuery.error)}</div>
      ) : attachments.length === 0 ? (
        <p className="muted">{t('attachments.empty')}</p>
      ) : (
        <ul className="attachment-list">
          {attachments.map((att) => (
            <AttachmentItem
              key={att.id}
              attachment={att}
              ticketId={ticketId}
              downloading={downloadingId === att.id}
              onDownload={() => handleDownload(att)}
            />
          ))}
        </ul>
      )}

      <div style={{ marginTop: 12 }}>
        <input
          ref={fileInputRef}
          type="file"
          aria-label={t('attachments.uploadAria')}
          accept={ATTACHMENT_ACCEPT}
          onChange={handleFileChange}
          disabled={uploadMutation.isPending}
          style={{ display: 'none' }}
          id={`attachment-input-${ticketId}`}
        />
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() => fileInputRef.current?.click()}
          disabled={uploadMutation.isPending}
        >
          {uploadMutation.isPending ? t('attachments.uploading') : t('attachments.upload')}
        </button>
        <p className="muted" style={{ marginTop: 6, fontSize: 12 }}>
          {t('attachments.uploadHint')}
        </p>
      </div>
    </div>
  );
}

function AttachmentItem({
  attachment,
  ticketId,
  downloading,
  onDownload,
}: {
  attachment: Attachment;
  ticketId: string;
  downloading: boolean;
  onDownload: () => void;
}) {
  const { t } = useTranslation('tickets');
  const queryClient = useQueryClient();
  const toast = useToast();
  const [confirmOpen, setConfirmOpen] = useState(false);

  const deleteMutation = useMutation({
    mutationFn: () => attachmentsApi.remove(attachment.id),
    onSuccess: () => {
      setConfirmOpen(false);
      queryClient.invalidateQueries({ queryKey: queryKeys.attachments(ticketId) });
      // attachment_deleted writes an activity entry (activity-only) — refresh the timeline.
      queryClient.invalidateQueries({ queryKey: queryKeys.activity(ticketId) });
    },
    onError: (err) => {
      setConfirmOpen(false);
      toast.showError(errorMessage(err));
    },
  });

  return (
    <li className="attachment-item">
      <div className="attachment-meta">
        <span className="attachment-name">{attachment.filename}</span>
        <span className="attachment-sub muted">
          {formatBytes(attachment.sizeBytes)} · {attachment.uploadedByDisplayName} ·{' '}
          {formatUtc(attachment.createdAt)}
        </span>
      </div>
      <div className="attachment-actions row" style={{ gap: 8 }}>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={onDownload}
          disabled={downloading}
        >
          {downloading ? t('attachments.downloading') : t('attachments.download')}
        </button>
        <button
          type="button"
          className="btn btn-danger btn-sm"
          onClick={() => setConfirmOpen(true)}
          disabled={deleteMutation.isPending}
        >
          {t('attachments.delete')}
        </button>
      </div>

      <ConfirmDialog
        open={confirmOpen}
        title={t('attachments.deleteConfirm.title')}
        message={t('attachments.deleteConfirm.message', { filename: attachment.filename })}
        confirmLabel={t('attachments.deleteConfirm.confirm')}
        danger
        busy={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setConfirmOpen(false)}
      />
    </li>
  );
}
