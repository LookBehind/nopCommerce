import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { MemoryRow } from "../types";
import { confirmDialog, toast } from "../ui/feedback";

/** Lists and manages the assistant's saved long-term memories (pgvector notes) for a profile. */
export function MemoryPanel({
  agentId,
  profileName,
  onClose,
}: {
  agentId: string;
  profileName: string;
  onClose: () => void;
}) {
  const [rows, setRows] = useState<MemoryRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setRows(await api.memories(agentId));
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [agentId]);

  async function remove(row: MemoryRow) {
    const ok = await confirmDialog({
      title: "Forget this note?",
      message: row.content,
      confirmLabel: "Forget",
      danger: true,
    });
    if (!ok) return;
    await api.deleteMemory(row.id);
    setRows((rs) => rs.filter((r) => r.id !== row.id));
    toast.success("Note forgotten");
  }

  return (
    <div className="ins-modal-backdrop" onClick={onClose} onKeyDown={(e) => e.key === "Escape" && onClose()}>
      <div className="ins-modal" role="dialog" aria-modal="true" aria-label="Assistant memory" onClick={(e) => e.stopPropagation()}>
        <div className="ins-modal-head">
          <h2>Memory — {profileName}</h2>
          <button className="ins-icon-btn" onClick={onClose} title="Close" aria-label="Close">
            ✕
          </button>
        </div>
        <div className="ins-modal-body">
          {loading && <div className="ins-muted">Loading…</div>}
          {error && <div className="ins-modal-warn err">{error}</div>}
          {!loading && !error && rows.length === 0 && (
            <div className="ins-muted">
              No saved notes yet. The assistant stores durable learnings here as you work with it.
            </div>
          )}

          <div className="ins-sched-list">
            {rows.map((r) => (
              <div key={r.id} className="ins-sched-row">
                <div className="ins-sched-main">
                  <div className="ins-mem-content">{r.content}</div>
                  <div className="ins-muted ins-sched-sub">
                    {r.kind} · {new Date(r.createdAt).toLocaleString()}
                  </div>
                </div>
                <div className="ins-sched-actions">
                  <button className="ins-chip" onClick={() => void remove(r)}>
                    Forget
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
