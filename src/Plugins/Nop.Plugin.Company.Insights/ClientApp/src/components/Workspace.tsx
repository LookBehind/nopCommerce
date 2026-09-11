import { useState } from "react";
import { useWorkspace } from "../store/workspace";
import { TabBar } from "./TabBar";
import { Canvas } from "./Canvas";
import { ChatPanel } from "./ChatPanel";
import { AgentPicker } from "./AgentPicker";
import { AddWidgetMenu } from "./AddWidgetMenu";
import { ScheduleModal } from "./ScheduleModal";

export function Workspace() {
  const tabs = useWorkspace((s) => s.tabs);
  const activeTabId = useWorkspace((s) => s.activeTabId);
  const resetWorkspace = useWorkspace((s) => s.resetWorkspace);
  const chatOpen = useWorkspace((s) => s.chatOpen);
  const [showSchedule, setShowSchedule] = useState(false);

  const activeTab = tabs.find((t) => t.id === activeTabId) ?? tabs[0];

  return (
    <div className={`ins-app ${chatOpen ? "chat-open" : ""}`}>
      <header className="ins-header">
        <div className="ins-brand">
          <span className="ins-brand-mark">◨</span>
          <span className="ins-brand-name">Company Insights</span>
        </div>
        <div className="ins-header-actions">
          <AddWidgetMenu tabId={activeTab.id} />
          <button
            className="ins-btn"
            title="Scheduled reports to Telegram"
            onClick={() => setShowSchedule(true)}
          >
            ⏰ Schedule
          </button>
          <button
            className="ins-btn subtle"
            title="Reset workspace"
            onClick={() => {
              if (window.confirm("Reset the workspace? Tabs and widgets will be cleared."))
                resetWorkspace();
            }}
          >
            Reset
          </button>
          <AgentPicker />
        </div>
      </header>

      <TabBar />

      <main className="ins-main">
        <Canvas tab={activeTab} />
      </main>

      <ChatPanel />

      {showSchedule && <ScheduleModal onClose={() => setShowSchedule(false)} />}
    </div>
  );
}
