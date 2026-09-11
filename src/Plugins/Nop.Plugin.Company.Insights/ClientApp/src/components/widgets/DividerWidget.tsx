export function DividerWidget({ label }: { label?: string }) {
  return (
    <div className="ins-divider">
      <span className="ins-divider-line" />
      {label ? <span className="ins-divider-label">{label}</span> : null}
      <span className="ins-divider-line" />
    </div>
  );
}
