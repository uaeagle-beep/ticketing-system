// Comments panel (Wireframe 3). Lists comments oldest-first (the API returns
// them in createdAt ASC order), shows author + time + body, and an add-comment
// textarea. Adding a comment must NOT reorder the board (it does not touch the
// ticket's modifiedAt — V21), so we only invalidate the comments query.

import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { commentsApi } from '@/api/endpoints';
import { queryKeys } from '@/lib/queryKeys';
import { formatUtc } from '@/lib/time';
import { errorMessage } from '@/lib/errors';
import { CountBadge } from '@/components/Badges';
import { LoadingState } from '@/components/States';
import { useToast } from '@/components/toast/ToastContext';

export function CommentsPanel({ ticketId }: { ticketId: string }) {
  const queryClient = useQueryClient();
  const toast = useToast();
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
        <h3 style={{ fontSize: 16 }}>Comments</h3>
        <CountBadge count={comments.length} />
      </div>

      {commentsQuery.isLoading ? (
        <LoadingState label="Loading comments…" />
      ) : commentsQuery.isError ? (
        <div className="banner banner-error">{errorMessage(commentsQuery.error)}</div>
      ) : comments.length === 0 ? (
        <p className="muted">No comments yet. Be the first to add one.</p>
      ) : (
        <div className="comment-list">
          {comments.map((c) => (
            <div className="comment" key={c.id}>
              <div className="comment-head">
                <span className="comment-author">{c.authorEmail}</span>
                <span className="comment-time">{formatUtc(c.createdAt)}</span>
              </div>
              <div className="comment-body">{c.body}</div>
            </div>
          ))}
        </div>
      )}

      <form onSubmit={handleSubmit} style={{ marginTop: 12 }}>
        <div className="field" style={{ marginBottom: 8 }}>
          <label htmlFor="new-comment">Add comment</label>
          <textarea
            id="new-comment"
            className="textarea"
            value={body}
            onChange={(e) => setBody(e.target.value)}
            placeholder="Write a comment…"
            disabled={addMutation.isPending}
          />
        </div>
        <button
          type="submit"
          className="btn btn-primary"
          disabled={addMutation.isPending || !body.trim()}
        >
          {addMutation.isPending ? 'Posting…' : 'Post comment'}
        </button>
      </form>
    </div>
  );
}
