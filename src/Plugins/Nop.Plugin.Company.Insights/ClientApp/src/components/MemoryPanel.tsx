import { useEffect, useState } from "react";
import { api } from "../api/client";
import { agentById, AGENTS } from "../agents";
import type { MemoryRow } from "../types";
import { confirmDialog, toast } from "../ui/feedback";

/** Lists and manages the agent's saved long-term memories (pgvector notes). */
export function MemoryPanel({ agentId, onClose }: { agentId: string; onClose: () => void }) {
  const [selected, setSelected] = useState(agentId);
  const [rows, setRows] = useState<MemoryRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load(id: string) {
    setLoading(true);
    setError(null);
    try {
      setRows(await api.memories(id));
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(selected);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected]);

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
      <div className="ins-modal" role="dialog" aria-modal="true" aria-label="Agent memory" onClick={(e) => e.stopPropagation()}>
        <div className="ins-modal-head">
          <h2>Agent memory — {agentById(selected).name}</h2>
          <button className="ins-icon-btn" onClick={onClose} title="Close" aria-label="Close">
            ✕
          </button>
        </div>
        <div className="ins-modal-body">
          <label className="ins-muted" style={{ fontSize: 12 }}>
            Agent
            <select
              value={selected}
              onChange={(e) => setSelected(e.target.value)}
              style={{ marginLeft: 8 }}
            >
              {AGENTS.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name}
                </option>
              ))}
            </select>
          </label>

          {loading && <div className="ins-muted">Loading…</div>}
          {error && <div className="ins-modal-warn err">{error}</div>}
          {!loading && !error && rows.length === 0 && (
            <div className="ins-muted">
              No saved notes yet. The agent stores durable learnings here as you work with it.
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
