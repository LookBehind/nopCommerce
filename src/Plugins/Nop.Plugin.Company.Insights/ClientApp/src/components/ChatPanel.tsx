import { useRef, useState, useEffect } from "react";
import { useWorkspace } from "../store/workspace";
import { agentById } from "../agents";
import { api } from "../api/client";
import type { ChatMessage, ChatWidget, Dataset } from "../types";
import { ChartWidget } from "./widgets/ChartWidget";
import { TableWidget } from "./widgets/TableWidget";

export function ChatPanel() {
  const chatOpen = useWorkspace((s) => s.chatOpen);
  const toggleChat = useWorkspace((s) => s.toggleChat);
  const selectedAgentId = useWorkspace((s) => s.selectedAgentId);
  const agent = agentById(selectedAgentId);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messages, busy]);

  async function send() {
    const text = input.trim();
    if (!text || busy) return;

    const history: ChatMessage[] = [...messages, { role: "user", content: text }];
    setMessages(history);
    setInput("");
    setBusy(true);
    try {
      const res = await api.chat(
        selectedAgentId,
        history.map((m) => ({ role: m.role, content: m.content }))
      );
      setMessages((prev) => [
        ...prev,
        { role: "assistant", content: res.reply, widgets: res.widgets },
      ]);
    } catch (e) {
      setMessages((prev) => [
        ...prev,
        { role: "assistant", content: `Request failed: ${String(e)}`, error: true },
      ]);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={`ins-chat ${chatOpen ? "open" : "collapsed"}`}>
      <div className="ins-chat-bar">
        <button className="ins-chat-handle" onClick={toggleChat} title={chatOpen ? "Hide chat" : "Show chat"}>
          {chatOpen ? "▾ Hide chat" : `▴ Ask ${agent.name}`}
        </button>
        {chatOpen && messages.length > 0 && (
          <button className="ins-chat-reset" onClick={() => setMessages([])} title="Reset conversation">
            Reset
          </button>
        )}
      </div>

      {chatOpen && (
        <div className="ins-chat-body">
          <div className="ins-chat-messages" ref={scrollRef}>
            {messages.length === 0 && (
              <div className="ins-chat-empty">
                <p>
                  <strong>{agent.name}</strong> — {agent.description}
                </p>
                <p className="ins-muted">
                  Ask about orders, revenue or status. I'll fetch real data and can propose a
                  chart or table you pin to the canvas. Try: <em>"orders per day this month"</em> or{" "}
                  <em>"break down orders by status"</em>.
                </p>
              </div>
            )}
            {messages.map((m, i) => (
              <MessageBubble key={i} message={m} />
            ))}
            {busy && <div className="ins-chat-thinking">{agent.name} is thinking… (the model may be warming up)</div>}
          </div>

          <form
            className="ins-chat-input"
            onSubmit={(e) => {
              e.preventDefault();
              void send();
            }}
          >
            <input
              type="text"
              placeholder={`Ask ${agent.name}…`}
              value={input}
              disabled={busy}
              onChange={(e) => setInput(e.target.value)}
            />
            <button type="submit" className="ins-btn primary" disabled={busy || !input.trim()}>
              Send
            </button>
          </form>
        </div>
      )}
    </div>
  );
}

function MessageBubble({ message }: { message: ChatMessage }) {
  return (
    <div className={`ins-msg ${message.role}`}>
      <div className={`ins-msg-text ${message.error ? "err" : ""}`}>{message.content}</div>
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
  }

  return (
    <div className="ins-wcard">
      <div className="ins-wcard-head">
        <span className="ins-wcard-title">{widget.title}</span>
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
