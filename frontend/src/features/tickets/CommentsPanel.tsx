// Comments panel (Wireframe 3). Lists comments oldest-first (the API returns
// them in createdAt ASC order), shows author + time + body, and an add-comment
// textarea. Adding a comment must NOT reorder the board (it does not touch the
// ticket's modifiedAt — V21), so we only invalidate the comments query.
//
// Wave 2 (F-12, ADR-0015): an author may edit/delete their own comment (inline edit); delete is
// also available to admins (moderation). An "edited" indicator shows when editedAt is set. Edit and
// delete wire to PUT/DELETE /api/comments/{id} and invalidate the comments query only (edit/delete
// raise no board-affecting change in Phase 1).

import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { commentsApi } from '@/api/endpoints';
import type { Comment } from '@/api/types';
import { queryKeys } from '@/lib/queryKeys';
import { formatUtc } from '@/lib/time';
import { displayName } from '@/lib/displayName';
import { errorMessage } from '@/lib/errors';
import { useAuth } from '@/auth/AuthContext';
import { CountBadge } from '@/components/Badges';
import { LoadingState } from '@/components/States';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { useToast } from '@/components/toast/ToastContext';

export function CommentsPanel({ ticketId }: { ticketId: string }) {
  const { t } = useTranslation('comments');
  const queryClient = useQueryClient();
  const toast = useToast();
  const { user } = useAuth();
  const [body, setBody] = useState('');

  const commentsQuery = useQuery({
    queryKey: queryKeys.comments(ticketId),
    queryFn: ({ signal }) => commentsApi.list(ticketId, signal),
  });

  const addMutation = useMutation({
    mutationFn: (text: string) => commentsApi.create(ticketId, { body: text }),
    onSuccess: () => {
      setBody('');
      queryClient.invalidateQueries({ queryKey: queryKeys.comments(ticketId) });
      // A comment writes a comment_added activity entry (Wave 2 §9.3).
      queryClient.invalidateQueries({ queryKey: queryKeys.activity(ticketId) });
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const comments = commentsQuery.data ?? [];

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (!body.trim()) return;
    addMutation.mutate(body.trim());
  };

  return (
    <div className="panel">
      <div className="row" style={{ marginBottom: 12 }}>
        <h3 style={{ fontSize: 16 }}>{t('title')}</h3>
        <CountBadge count={comments.length} />
      </div>

      {commentsQuery.isLoading ? (
        <LoadingState label={t('loading')} />
      ) : commentsQuery.isError ? (
        <div className="banner banner-error">{errorMessage(commentsQuery.error)}</div>
      ) : comments.length === 0 ? (
        <p className="muted">{t('empty')}</p>
      ) : (
        <div className="comment-list">
          {comments.map((c) => (
            <CommentItem
              key={c.id}
              comment={c}
              canEdit={user?.id === c.authorId}
              canDelete={user?.id === c.authorId || Boolean(user?.isAdmin)}
              ticketId={ticketId}
            />
          ))}
        </div>
      )}

      <form onSubmit={handleSubmit} style={{ marginTop: 12 }}>
        <div className="field" style={{ marginBottom: 8 }}>
          <label htmlFor="new-comment">{t('add.label')}</label>
          <textarea
            id="new-comment"
            className="textarea"
            value={body}
            onChange={(e) => setBody(e.target.value)}
            placeholder={t('add.placeholder')}
            disabled={addMutation.isPending}
          />
        </div>
        <button
          type="submit"
          className="btn btn-primary"
          disabled={addMutation.isPending || !body.trim()}
        >
          {addMutation.isPending ? t('add.posting') : t('add.post')}
        </button>
      </form>
    </div>
  );
}

function CommentItem({
  comment,
  canEdit,
  canDelete,
  ticketId,
}: {
  comment: Comment;
  canEdit: boolean;
  canDelete: boolean;
  ticketId: string;
}) {
  const { t } = useTranslation('comments');
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(comment.body);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const editMutation = useMutation({
    mutationFn: (text: string) => commentsApi.update(comment.id, { body: text }),
    onSuccess: () => {
      setEditing(false);
      queryClient.invalidateQueries({ queryKey: queryKeys.comments(ticketId) });
      // comment_edited writes an activity entry (activity-only, ADR-0015).
      queryClient.invalidateQueries({ queryKey: queryKeys.activity(ticketId) });
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const deleteMutation = useMutation({
    mutationFn: () => commentsApi.remove(comment.id),
    onSuccess: () => {
      setConfirmOpen(false);
      queryClient.invalidateQueries({ queryKey: queryKeys.comments(ticketId) });
      // comment_deleted writes an activity entry (activity-only, ADR-0015).
      queryClient.invalidateQueries({ queryKey: queryKeys.activity(ticketId) });
    },
    onError: (err) => {
      setConfirmOpen(false);
      toast.showError(errorMessage(err));
    },
  });

  const startEdit = () => {
    setDraft(comment.body);
    setEditing(true);
  };

  const saveEdit = () => {
    const text = draft.trim();
    if (!text) return; // blank is rejected client-side (mirrors the 400 the server would return)
    editMutation.mutate(text);
  };

  return (
    <div className="comment">
      <div className="comment-head">
        <span className="comment-author">{displayName(comment.authorName, comment.authorEmail)}</span>
        <span className="comment-time">
          {formatUtc(comment.createdAt)}
          {comment.edited && comment.editedAt ? (
            <span className="comment-edited" title={t('editedTitle', { date: formatUtc(comment.editedAt) })}>
              {' '}
              {t('edited')}
            </span>
          ) : null}
        </span>
      </div>

      {editing ? (
        <div className="comment-edit">
          <textarea
            className="textarea"
            aria-label={t('edit.label')}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            disabled={editMutation.isPending}
          />
          <div className="row" style={{ gap: 8, marginTop: 6 }}>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={saveEdit}
              disabled={editMutation.isPending || !draft.trim()}
            >
              {editMutation.isPending ? t('edit.saving') : t('edit.save')}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => setEditing(false)}
              disabled={editMutation.isPending}
            >
              {t('edit.cancel')}
            </button>
          </div>
        </div>
      ) : (
        <>
          <div className="comment-body">{comment.body}</div>
          {canEdit || canDelete ? (
            <div className="comment-actions row" style={{ gap: 8, marginTop: 6 }}>
              {canEdit ? (
                <button type="button" className="btn btn-secondary btn-sm" onClick={startEdit}>
                  {t('actions.edit')}
                </button>
              ) : null}
              {canDelete ? (
                <button
                  type="button"
                  className="btn btn-danger btn-sm"
                  onClick={() => setConfirmOpen(true)}
                  disabled={deleteMutation.isPending}
                >
                  {t('actions.delete')}
                </button>
              ) : null}
            </div>
          ) : null}
        </>
      )}

      <ConfirmDialog
        open={confirmOpen}
        title={t('deleteConfirm.title')}
        message={t('deleteConfirm.message')}
        confirmLabel={t('deleteConfirm.confirm')}
        danger
        busy={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}
