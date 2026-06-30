export function FullPageLoader({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="full-loader" role="status" aria-live="polite">
      <div className="spinner" aria-hidden />
      <span>{label}</span>
    </div>
  );
}
