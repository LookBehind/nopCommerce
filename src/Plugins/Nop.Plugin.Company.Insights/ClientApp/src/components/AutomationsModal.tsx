import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { AgentConfig, AgentConfigInput, AgentRun } from "../types";
import { confirmDialog, toast } from "../ui/feedback";

const EVENT_TYPES = [
  "review-added",
  "review-triaged",
  "order-placed",
  "order-cancelled",
  "delivery-approaching",
  "day-closing",
  "product-created",
  "product-updated",
];
const SINKS = ["dashboard", "telegram", "memory"];

interface Editor {
  id?: string;
  name: string;
  enabled: boolean;
  triggerKind: "event" | "schedule";
  eventType: string;
  cron: string;
  maxRating: string;
  systemPrompt: string;
  instruction: string;
  sinks: string[];
  outputTarget: string;
}

function blankEditor(): Editor {
  return {
    name: "",
    enabled: true,
    triggerKind: "event",
    eventType: "review-added",
    cron: "0 9 * * *",
    maxRating: "",
    systemPrompt: "",
    instruction: "",
    sinks: ["dashboard"],
    outputTarget: "",
  };
}

function toEditor(a: AgentConfig): Editor {
  let maxRating = "";
  try {
    const f = a.filter ? JSON.parse(a.filter) : {};
    if (typeof f.maxRating === "number") maxRating = String(f.maxRating);
  } catch {
    /* ignore */
  }
  let sinks: string[] = ["dashboard"];
  try {
    if (a.outputSinks) sinks = JSON.parse(a.outputSinks);
  } catch {
    /* ignore */
  }
  return {
    id: a.id,
    name: a.name,
    enabled: a.enabled,
    triggerKind: a.triggerKind,
    eventType: a.eventType ?? "review-added",
    cron: a.cron ?? "0 9 * * *",
    maxRating,
    systemPrompt: a.systemPrompt ?? "",
    instruction: a.instruction ?? "",
    sinks: sinks.length ? sinks : ["dashboard"],
    outputTarget: a.outputTarget ?? "",
  };
}

function applyDraft(e: Editor, d: AgentConfigInput): Editor {
  const mr = d.filter && typeof (d.filter as Record<string, unknown>).maxRating === "number"
    ? String((d.filter as Record<string, unknown>).maxRating)
    : "";
  return {
    ...e,
    name: d.name ?? e.name,
    triggerKind: d.triggerKind ?? e.triggerKind,
    eventType: d.eventType ?? e.eventType,
    cron: d.cron ?? e.cron,
    maxRating: mr,
    systemPrompt: d.systemPrompt ?? e.systemPrompt,
    instruction: d.instruction ?? e.instruction,
    sinks: d.outputSinks && d.outputSinks.length ? d.outputSinks : e.sinks,
    outputTarget: d.outputTarget ?? e.outputTarget,
  };
}

function toInput(e: Editor): AgentConfigInput {
  const filter: Record<string, unknown> = {};
  if (e.triggerKind === "event" && e.maxRating.trim() && !isNaN(Number(e.maxRating)))
    filter.maxRating = Number(e.maxRating);
  return {
    id: e.id,
    name: e.name.trim(),
    enabled: e.enabled,
    triggerKind: e.triggerKind,
    eventType: e.triggerKind === "event" ? e.eventType : undefined,
    cron: e.triggerKind === "schedule" ? e.cron.trim() : undefined,
    filter,
    systemPrompt: e.systemPrompt.trim(),
    instruction: e.instruction.trim(),
    outputSinks: e.sinks,
    outputTarget: e.sinks.includes("telegram") ? e.outputTarget.trim() : "",
  };
}

/** A static left-to-right flow diagram of the automation. */
function FlowDiagram({ e }: { e: Editor }) {
  const trigger = e.triggerKind === "event" ? e.eventType : `cron ${e.cron || "…"}`;
  const filter = e.triggerKind === "event" && e.maxRating.trim() ? `rating ≤ ${e.maxRating}` : null;
  return (
    <div className="ins-flow">
      <div className="ins-flow-node trigger">
        <div className="ins-flow-kind">Trigger</div>
        <div className="ins-flow-title">{trigger}</div>
      </div>
      <div className="ins-flow-arrow">▶</div>
      {filter && (
        <>
          <div className="ins-flow-node filter">
            <div className="ins-flow-kind">Filter</div>
            <div className="ins-flow-title">{filter}</div>
          </div>
          <div className="ins-flow-arrow">▶</div>
        </>
      )}
      <div className="ins-flow-node agent">
        <div className="ins-flow-kind">Agent</div>
        <div className="ins-flow-title">{e.name.trim() || "(unnamed)"}</div>
      </div>
      <div className="ins-flow-arrow">▶</div>
      <div className="ins-flow-node output">
        <div className="ins-flow-kind">Output</div>
        <div className="ins-flow-title">{e.sinks.join(", ") || "—"}</div>
      </div>
    </div>
  );
}

export function AutomationsModal({ onClose }: { onClose: () => void }) {
  const [tab, setTab] = useState<"agents" | "runs">("agents");
  const [agents, setAgents] = useState<AgentConfig[]>([]);
  const [runs, setRuns] = useState<AgentRun[]>([]);
  const [editor, setEditor] = useState<Editor | null>(null);
  const [describe, setDescribe] = useState("");
  const [drafting, setDrafting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    try {
      setAgents(await api.agents());
    } catch (e) {
      setError(String(e));
    }
  }
  async function refreshRuns() {
    try {
      setRuns(await api.agentRuns(undefined, 100));
    } catch {
      /* ignore */
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => {
    if (tab === "runs") void refreshRuns();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab]);

  async function save() {
    if (!editor) return;
    if (!editor.name.trim()) {
      toast.error("Give the automation a name");
      return;
    }
    setBusy(true);
    try {
      const res = await api.saveAgent(toInput(editor));
      if (!res.ok) toast.error(res.error ?? "Save failed");
      else {
        toast.success("Automation saved");
        setEditor(null);
        await refresh();
      }
    } finally {
      setBusy(false);
    }
  }

  async function remove(a: AgentConfig) {
    const ok = await confirmDialog({ title: `Delete “${a.name}”?`, confirmLabel: "Delete", danger: true });
    if (!ok || !a.id) return;
    await api.deleteAgent(a.id);
    await refresh();
    toast.success("Deleted");
  }

  async function toggle(a: AgentConfig) {
    await api.saveAgent({ ...toInput(toEditor(a)), enabled: !a.enabled });
    await refresh();
  }

  async function runDraft() {
    if (!describe.trim()) return;
    setDrafting(true);
    try {
      const res = await api.draftAgent(describe.trim());
      if (res.ok && res.draft) {
        setEditor((cur) => applyDraft(cur ?? blankEditor(), res.draft!));
        toast.success("Draft ready — review and save");
      } else {
        toast.error("The assistant couldn't draft that (the model may be warming up) — fill the form manually.");
        setEditor((cur) => cur ?? blankEditor());
      }
    } catch {
      toast.error("Draft request failed");
    } finally {
      setDrafting(false);
    }
  }

  const triggerLabel = useMemo(
    () => (a: AgentConfig) => (a.triggerKind === "event" ? a.eventType : `cron ${a.cron}`),
    []
  );

  return (
    <div className="ins-modal-backdrop" onClick={onClose} onKeyDown={(e) => e.key === "Escape" && onClose()}>
      <div className="ins-modal ins-modal-wide" role="dialog" aria-modal="true" aria-label="Automations" onClick={(e) => e.stopPropagation()}>
        <div className="ins-modal-head">
          <h2>Automations</h2>
          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <div className="ins-tabbar" style={{ background: "transparent", border: "none", padding: 0 }}>
              <button className={`ins-tab ${tab === "agents" ? "active" : ""}`} onClick={() => setTab("agents")}>
                Agents
              </button>
              <button className={`ins-tab ${tab === "runs" ? "active" : ""}`} onClick={() => setTab("runs")}>
                Runs
              </button>
            </div>
            <button className="ins-icon-btn" onClick={onClose} title="Close" aria-label="Close">
              ✕
            </button>
          </div>
        </div>

        {error && <div className="ins-modal-warn err">{error}</div>}

        <div className="ins-modal-body">
          {tab === "agents" ? (
            <div className="ins-auto-grid">
              <div className="ins-auto-list">
                <button className="ins-btn primary" onClick={() => setEditor(blankEditor())}>
                  + New automation
                </button>
                {agents.length === 0 && <div className="ins-muted" style={{ marginTop: 10 }}>No automations yet.</div>}
                {agents.map((a) => (
                  <div key={a.id} className={`ins-auto-row ${editor?.id === a.id ? "active" : ""}`}>
                    <button className="ins-auto-open" onClick={() => setEditor(toEditor(a))} title="Edit">
                      <span className="ins-auto-name">{a.name}</span>
                      <span className="ins-muted ins-auto-sub">
                        {a.triggerKind === "event" ? "⚡" : "⏰"} {triggerLabel(a)} {!a.enabled && "· (off)"}
                      </span>
                    </button>
                    <div className="ins-auto-actions">
                      <button className="ins-chip" onClick={() => void toggle(a)}>
                        {a.enabled ? "Disable" : "Enable"}
                      </button>
                      <button className="ins-chip" onClick={() => void remove(a)}>
                        Delete
                      </button>
                    </div>
                  </div>
                ))}
              </div>

              <div className="ins-auto-editor">
                {!editor ? (
                  <div className="ins-muted">Select an automation to edit, or create a new one. You can also describe it to the assistant below.</div>
                ) : (
                  <>
                    <FlowDiagram e={editor} />

                    <div className="ins-describe">
                      <input
                        placeholder="Describe an automation… e.g. “alert the vendor when a 1-star review comes in”"
                        value={describe}
                        onChange={(e) => setDescribe(e.target.value)}
                        onKeyDown={(e) => e.key === "Enter" && void runDraft()}
                      />
                      <button className="ins-btn" disabled={drafting || !describe.trim()} onClick={() => void runDraft()}>
                        {drafting ? "Drafting…" : "Draft with assistant"}
                      </button>
                    </div>

                    <div className="ins-form">
                      <label>
                        Name
                        <input value={editor.name} onChange={(e) => setEditor({ ...editor, name: e.target.value })} />
                      </label>
                      <label>
                        Trigger
                        <select value={editor.triggerKind} onChange={(e) => setEditor({ ...editor, triggerKind: e.target.value as Editor["triggerKind"] })}>
                          <option value="event">On event</option>
                          <option value="schedule">On schedule</option>
                        </select>
                      </label>
                      {editor.triggerKind === "event" ? (
                        <>
                          <label>
                            Event
                            <select value={editor.eventType} onChange={(e) => setEditor({ ...editor, eventType: e.target.value })}>
                              {EVENT_TYPES.map((t) => (
                                <option key={t} value={t}>
                                  {t}
                                </option>
                              ))}
                            </select>
                          </label>
                          <label>
                            Filter: max rating (reviews, optional)
                            <input type="number" min={1} max={5} value={editor.maxRating} onChange={(e) => setEditor({ ...editor, maxRating: e.target.value })} placeholder="—" />
                          </label>
                        </>
                      ) : (
                        <label>
                          Cron (5-field, Yerevan)
                          <input value={editor.cron} onChange={(e) => setEditor({ ...editor, cron: e.target.value })} placeholder="0 9 * * *" />
                        </label>
                      )}
                      <label style={{ gridColumn: "1 / -1" }}>
                        System prompt (the agent's persona)
                        <textarea rows={2} value={editor.systemPrompt} onChange={(e) => setEditor({ ...editor, systemPrompt: e.target.value })} />
                      </label>
                      <label style={{ gridColumn: "1 / -1" }}>
                        Instruction (what it does each run)
                        <textarea rows={2} value={editor.instruction} onChange={(e) => setEditor({ ...editor, instruction: e.target.value })} />
                      </label>
                      <div style={{ gridColumn: "1 / -1" }}>
                        <div className="ins-param-label">Outputs</div>
                        <div className="ins-param-quick">
                          {SINKS.map((s) => (
                            <button
                              key={s}
                              type="button"
                              className={`ins-chip ${editor.sinks.includes(s) ? "on" : ""}`}
                              onClick={() =>
                                setEditor({
                                  ...editor,
                                  sinks: editor.sinks.includes(s) ? editor.sinks.filter((x) => x !== s) : [...editor.sinks, s],
                                })
                              }
                            >
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                      {editor.sinks.includes("telegram") && (
                        <label style={{ gridColumn: "1 / -1" }}>
                          Telegram chat id
                          <input value={editor.outputTarget} onChange={(e) => setEditor({ ...editor, outputTarget: e.target.value })} placeholder="-1001234567890" />
                        </label>
                      )}
                      <div className="ins-form-actions">
                        <label className="ins-muted" style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
                          <input type="checkbox" checked={editor.enabled} onChange={(e) => setEditor({ ...editor, enabled: e.target.checked })} />
                          Enabled
                        </label>
                        <div style={{ display: "flex", gap: 8 }}>
                          <button className="ins-btn subtle" onClick={() => setEditor(null)}>
                            Cancel
                          </button>
                          <button className="ins-btn primary" disabled={busy} onClick={() => void save()}>
                            Save automation
                          </button>
                        </div>
                      </div>
                    </div>
                  </>
                )}
              </div>
            </div>
          ) : (
            <div className="ins-runs">
              {runs.length === 0 && <div className="ins-muted">No agent runs yet.</div>}
              {runs.map((r) => (
                <div key={r.id} className="ins-run-row">
                  <span className={`ins-run-status ${r.status}`}>{r.status}</span>
                  <span className="ins-run-agent">{r.agentName ?? r.agentId}</span>
                  <span className="ins-muted ins-run-trigger">{r.triggerType}</span>
                  <span className="ins-muted ins-run-time">{new Date(r.startedAt).toLocaleString()}</span>
                  <span className="ins-muted ins-run-dur">{r.durationMs != null ? `${r.durationMs} ms` : "—"}</span>
                  <span className="ins-run-out" title={r.error || r.output || ""}>
                    {r.error ? `⚠ ${r.error}` : summarizeOutput(r.output)}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function summarizeOutput(output?: string | null): string {
  if (!output) return "";
  try {
    const o = JSON.parse(output);
    return typeof o.text === "string" ? o.text.slice(0, 120) : "";
  } catch {
    return output.slice(0, 120);
  }
}
