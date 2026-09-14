import { useEffect, useRef, useState } from "react";
import { useDialog, useToasts } from "./feedback";

/** Renders transient toasts (top-right). Mounted once at app root. */
export function Toaster() {
  const toasts = useToasts((s) => s.toasts);
  const dismiss = useToasts((s) => s.dismiss);
  return (
    <div className="ins-toaster" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className={`ins-toast ${t.kind}`} role="status" onClick={() => dismiss(t.id)}>
          {t.text}
        </div>
      ))}
    </div>
  );
}

/** Renders the active prompt/confirm dialog. Mounted once at app root. */
export function DialogHost() {
  const current = useDialog((s) => s.current);
  const close = useDialog((s) => s.close);
  const [value, setValue] = useState("");
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (current?.mode === "prompt") {
      setValue(current.defaultValue ?? "");
      // Focus + select on open.
      setTimeout(() => {
        inputRef.current?.focus();
        inputRef.current?.select();
      }, 0);
    }
  }, [current]);

  if (!current) return null;

  const finish = (result: string | boolean | null) => {
    current.resolve(result);
    close();
  };

  const onConfirm = () => finish(current.mode === "prompt" ? value : true);
  const onCancel = () => finish(current.mode === "prompt" ? null : false);

  return (
    <div
      className="ins-modal-backdrop"
      onClick={onCancel}
      onKeyDown={(e) => {
        if (e.key === "Escape") onCancel();
      }}
    >
      <div className="ins-dialog" role="dialog" aria-modal="true" aria-label={current.title} onClick={(e) => e.stopPropagation()}>
        <h3 className="ins-dialog-title">{current.title}</h3>
        {current.message && <p className="ins-dialog-msg ins-muted">{current.message}</p>}
        {current.mode === "prompt" && (
          <input
            ref={inputRef}
            className="ins-dialog-input"
            value={value}
            placeholder={current.placeholder}
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") onConfirm();
              if (e.key === "Escape") onCancel();
            }}
          />
        )}
        <div className="ins-dialog-actions">
          <button className="ins-btn subtle" onClick={onCancel}>
            {current.cancelLabel ?? "Cancel"}
          </button>
          <button className={`ins-btn ${current.danger ? "danger" : "primary"}`} onClick={onConfirm}>
            {current.confirmLabel ?? "OK"}
          </button>
        </div>
      </div>
    </div>
  );
}
