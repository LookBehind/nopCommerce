import { useCallback, useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useWorkspace } from "../store/workspace";
import { api } from "../api/client";
import type { ProfileContext } from "../api/client";
import type { ChatMessage, ChatWidget, CombinedReportRecipe, ConversationHeader, Dataset } from "../types";
import { ChartWidget } from "./widgets/ChartWidget";
import { TableWidget } from "./widgets/TableWidget";
import { MemoryPanel } from "./MemoryPanel";
import { useLlmStatus } from "../ui/llmStatus";
import { confirmDialog, toast } from "../ui/feedback";
import { executeRecipe } from "../utils/combine";

/** Compile each agent-authored combine recipe into a chart/table widget, entirely in the browser. */
async function buildCombinedWidgets(
  recipes: CombinedReportRecipe[] | undefined,
  ctx: ProfileContext
): Promise<ChatWidget[]> {
  if (!recipes?.length) return [];
  const out: ChatWidget[] = [];
  for (const recipe of recipes) {
    try {
      const dataset = await executeRecipe(recipe, (src) =>
        api.runReport(src.reportId, { days: src.days, limit: src.limit, from: src.from, to: src.to, slot: src.slot }, ctx)
      );
      const chart = recipe.chart;
      out.push({
        type: chart ? "chart" : "table",
        title: recipe.title || "Combined report",
        chartKind: chart?.chartKind,
        xField: chart?.xField,
        yField: chart?.yField,
        categoryField: chart?.categoryField,
        columns: dataset.columns,
        rows: dataset.rows,
        combinedRecipe: recipe,
      });
    } catch (e) {
      toast.error(`Couldn't compile "${recipe.title || "combined report"}": ${String(e instanceof Error ? e.message : e)}`);
    }
  }
  return out;
}

const SUGGESTIONS = [
  "orders per day this month",
  "break down orders by status",
  "revenue trend over the last 90 days",
  "recent product reviews needing triage",
];


export function ChatPanel() {
  const chatOpen = useWorkspace((s) => s.chatOpen);
  const toggleChat = useWorkspace((s) => s.toggleChat);
  const selectedProfileId = useWorkspace((s) => s.selectedProfileId);
  const selectedCompanyId = useWorkspace((s) => s.selectedCompanyId);
  const profilesData = useWorkspace((s) => s.profilesData);
  const chatDock = useWorkspace((s) => s.chatDock);
  const chatSize = useWorkspace((s) => s.chatSize);
  const setChatSize = useWorkspace((s) => s.setChatSize);
  const setChatDock = useWorkspace((s) => s.setChatDock);
  const caps = useWorkspace((s) => s.capabilities);
  const setLlmStatus = useLlmStatus((s) => s.setStatus);

  const activeProfile = profilesData?.profiles.find((p) => p.id === selectedProfileId);
  const profileName = activeProfile?.name ?? "Insights";
  const profileDesc = activeProfile?.description ?? "Ask about orders, revenue, reviews or delivery.";
  const ctx: ProfileContext = { profileId: selectedProfileId || undefined, companyId: selectedCompanyId };

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string>(""); // live progress note streamed from the agent
  const [convId, setConvId] = useState<string | null>(null);
  const [showHistory, setShowHistory] = useState(false);
  const [history, setHistory] = useState<ConversationHeader[]>([]);
  const [showMemory, setShowMemory] = useState(false);

  const scrollRef = useRef<HTMLDivElement | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const persistence = !!caps?.persistence;

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messages, busy, status]);

  const refreshHistory = useCallback(async () => {
    if (!persistence) return;
    try {
      setHistory(await api.conversations());
    } catch {
      /* ignore */
    }
  }, [persistence]);

  // Persist the current thread after each completed exchange.
  const persist = useCallback(
    async (msgs: ChatMessage[]) => {
      if (!persistence || msgs.length === 0) return;
      const title = msgs.find((m) => m.role === "user")?.content?.slice(0, 60) || "Conversation";
      try {
        const res = await api.saveConversation({
          id: convId ?? undefined,
          title,
          agentId: selectedProfileId,
          messages: msgs,
        });
        if (res.ok && res.id && !convId) setConvId(res.id);
      } catch {
        /* best effort */
      }
    },
    [persistence, convId, selectedProfileId]
  );

  async function send(text: string) {
    const trimmed = text.trim();
    if (!trimmed || busy) return;

    const thread: ChatMessage[] = [...messages, { role: "user", content: trimmed }];
    setMessages(thread);
    setInput("");
    setBusy(true);

    const ac = new AbortController();
    abortRef.current = ac;
    setStatus("");
    try {
      const res = await api.chat(
        ctx,
        thread.map((m) => ({ role: m.role, content: m.content })),
        (note) => setStatus(note),
        ac.signal
      );
      setLlmStatus("ready");
      // Combined reports are compiled here in the browser (join existing reports, no backend compute).
      const combinedWidgets = await buildCombinedWidgets(res.combinedReports, ctx);
      const next: ChatMessage[] = [
        ...thread,
        { role: "assistant", content: res.reply, widgets: [...(res.widgets || []), ...combinedWidgets] },
      ];
      setMessages(next);
      void persist(next);
    } catch (e) {
      if (ac.signal.aborted) {
        setMessages((prev) => [...prev, { role: "assistant", content: "⏹ Stopped.", error: true }]);
      } else {
        setLlmStatus("cold");
        setMessages((prev) => [
          ...prev,
          { role: "assistant", content: `Request failed: ${String(e)}`, error: true },
        ]);
      }
    } finally {
      setBusy(false);
      setStatus("");
      abortRef.current = null;
    }
  }

  function stop() {
    abortRef.current?.abort();
  }

  function newChat() {
    stop();
    setMessages([]);
    setConvId(null);
  }

  async function openConversation(id: string) {
    try {
      const c = await api.conversation(id);
      setMessages(c.messages ?? []);
      setConvId(c.id);
      setShowHistory(false);
    } catch {
      toast.error("Couldn't load that conversation");
    }
  }

  async function deleteConversation(id: string) {
    const ok = await confirmDialog({ title: "Delete conversation?", confirmLabel: "Delete", danger: true });
    if (!ok) return;
    await api.deleteConversation(id);
    if (id === convId) newChat();
    void refreshHistory();
  }

  const sizeStyle =
    chatDock === "right" ? { width: chatSize } : { height: chatSize + 40 };

  return (
    <div className={`ins-chat dock-${chatDock} ${chatOpen ? "open" : "collapsed"}`} style={chatOpen ? sizeStyle : undefined}>
      {chatOpen && <ResizeHandle dock={chatDock} size={chatSize} onResize={setChatSize} />}

      <div className="ins-chat-bar">
        <button className="ins-chat-handle" onClick={toggleChat} title={chatOpen ? "Hide chat" : "Show chat"}>
          {chatOpen ? "▾ Hide chat" : `▴ Ask ${profileName}`}
        </button>
        {chatOpen && (
          <div className="ins-chat-bar-actions">
            <button className="ins-icon-btn" title="New conversation" aria-label="New conversation" onClick={newChat}>
              ✎
            </button>
            {persistence && (
              <button
                className="ins-icon-btn"
                title="Conversation history"
                aria-label="Conversation history"
                onClick={() => {
                  setShowHistory((v) => !v);
                  void refreshHistory();
                }}
              >
                🕘
              </button>
            )}
            {caps?.memory && (
              <button className="ins-icon-btn" title="Agent memory" aria-label="Agent memory" onClick={() => setShowMemory(true)}>
                🧠
              </button>
            )}
            <button
              className="ins-icon-btn"
              title={chatDock === "bottom" ? "Dock to right" : "Dock to bottom"}
              aria-label="Toggle chat dock"
              onClick={() => setChatDock(chatDock === "bottom" ? "right" : "bottom")}
            >
              {chatDock === "bottom" ? "⇥" : "⤓"}
            </button>
          </div>
        )}
      </div>

      {chatOpen && (
        <div className="ins-chat-body">
          {showHistory && (
            <div className="ins-chat-history">
              {history.length === 0 && <div className="ins-muted" style={{ padding: 8 }}>No saved conversations.</div>}
              {history.map((h) => (
                <div key={h.id} className={`ins-hist-row ${h.id === convId ? "active" : ""}`}>
                  <button className="ins-hist-open" onClick={() => openConversation(h.id)} title={h.title}>
                    <span className="ins-hist-title">{h.title}</span>
                    <span className="ins-muted ins-hist-date">{new Date(h.updatedAt).toLocaleDateString()}</span>
                  </button>
                  <button className="ins-icon-btn" title="Delete" aria-label="Delete conversation" onClick={() => void deleteConversation(h.id)}>
                    ✕
                  </button>
                </div>
              ))}
            </div>
          )}

          <div className="ins-chat-messages" ref={scrollRef}>
            {messages.length === 0 && (
              <div className="ins-chat-empty">
                <p>
                  <strong>{profileName}</strong> — {profileDesc}
                </p>
                <p className="ins-muted">
                  Ask about orders, revenue, reviews or status. I'll fetch real data and can propose a
                  chart or table you pin to the canvas.
                </p>
                <div className="ins-suggestions">
                  {SUGGESTIONS.map((s) => (
                    <button key={s} className="ins-chip" onClick={() => void send(s)}>
                      {s}
                    </button>
                  ))}
                </div>
              </div>
            )}
            {messages.map((m, i) => (
              <MessageBubble key={i} message={m} />
            ))}
            {busy && (
              <div className="ins-chat-thinking">{status || `${profileName} is thinking…`}</div>
            )}
          </div>

          <form
            className="ins-chat-input"
            onSubmit={(e) => {
              e.preventDefault();
              void send(input);
            }}
          >
            <textarea
              rows={1}
              placeholder={`Ask ${profileName}…  (Enter to send, Shift+Enter for a new line)`}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  void send(input);
                }
              }}
            />
            {busy ? (
              <button type="button" className="ins-btn danger" onClick={stop}>
                Stop
              </button>
            ) : (
              <button type="submit" className="ins-btn primary" disabled={!input.trim()}>
                Send
              </button>
            )}
          </form>
        </div>
      )}

      {showMemory && <MemoryPanel agentId={selectedProfileId} profileName={profileName} onClose={() => setShowMemory(false)} />}
    </div>
  );
}

function ResizeHandle({
  dock,
  size,
  onResize,
}: {
  dock: "bottom" | "right";
  size: number;
  onResize: (n: number) => void;
}) {
  function onPointerDown(e: React.PointerEvent) {
    e.preventDefault();
    const startPos = dock === "right" ? e.clientX : e.clientY;
    const startSize = size;
    const move = (ev: PointerEvent) => {
      const cur = dock === "right" ? ev.clientX : ev.clientY;
      const delta = dock === "right" ? startPos - cur : startPos - cur;
      onResize(startSize + delta);
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }
  return <div className={`ins-chat-resize ${dock}`} onPointerDown={onPointerDown} title="Drag to resize" />;
}

function MessageBubble({ message }: { message: ChatMessage }) {
  // Assistant replies are Markdown (bullets/bold/tables); user text and errors stay literal.
  const asMarkdown = message.role === "assistant" && !message.error;
  return (
    <div className={`ins-msg ${message.role}`}>
      <div className={`ins-msg-text ${message.error ? "err" : ""}`}>
        {asMarkdown ? (
          <div className="ins-md">
            <ReactMarkdown
              remarkPlugins={[remarkGfm]}
              components={{
                a: ({ node, ...props }) => <a {...props} target="_blank" rel="noreferrer noopener" />,
              }}
            >
              {message.content || ""}
            </ReactMarkdown>
          </div>
        ) : (
          message.content
        )}
      </div>
      {message.widgets && message.widgets.length > 0 && (
        <div className="ins-msg-widgets">
          {message.widgets.map((w, i) => (
            <WidgetCard key={i} widget={w} />
          ))}
        </div>
      )}
    </div>
  );
}

function WidgetCard({ widget }: { widget: ChatWidget }) {
  const addWidget = useWorkspace((s) => s.addWidget);
  const activeTabId = useWorkspace((s) => s.activeTabId);

  const dataset: Dataset = { columns: widget.columns, rows: widget.rows };

  function pin() {
    if (widget.type === "chart") {
      addWidget(activeTabId, {
        type: "chart",
        title: widget.title,
        chart: {
          chartKind: widget.chartKind ?? "line",
          xField: widget.xField,
          yField: widget.yField,
          categoryField: widget.categoryField,
        },
        dataset,
      });
    } else {
      addWidget(activeTabId, { type: "table", title: widget.title, dataset });
    }
    toast.success("Pinned to canvas");
  }

  return (
    <div className="ins-wcard">
      <div className="ins-wcard-head">
        <span className="ins-wcard-title">
          {widget.title}
          {widget.combinedRecipe && (
            <span
              className="ins-muted"
              style={{ marginLeft: 6, fontSize: 11, fontWeight: 400 }}
              title={`Compiled in your browser by joining ${widget.combinedRecipe.sources.length} reports`}
            >
              🔗 combined
            </span>
          )}
        </span>
        <button className="ins-chip" onClick={pin} title="Pin to canvas">
          📌 Pin
        </button>
      </div>
      <div className="ins-wcard-preview">
        {widget.type === "chart" ? (
          <ChartWidget
            config={{
              chartKind: widget.chartKind ?? "line",
              xField: widget.xField,
              yField: widget.yField,
              categoryField: widget.categoryField,
            }}
            dataset={dataset}
          />
        ) : (
          <TableWidget dataset={dataset} />
        )}
      </div>
    </div>
  );
}
