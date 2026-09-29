import { useState, type CSSProperties } from "react";
import { useWorkspace } from "../store/workspace";
import { TabBar } from "./TabBar";
import { Canvas } from "./Canvas";
import { ChatPanel } from "./ChatPanel";
import { ProfilePicker } from "./ProfilePicker";
import { AddWidgetMenu } from "./AddWidgetMenu";
import { AutomationsModal } from "./AutomationsModal";
import { confirmDialog } from "../ui/feedback";
import type { ThemePref } from "../types";

const THEME_ICON: Record<ThemePref, string> = { system: "◐", light: "☀", dark: "☾" };
const THEME_NEXT: Record<ThemePref, ThemePref> = { system: "light", light: "dark", dark: "system" };

export function Workspace() {
  const tabs = useWorkspace((s) => s.tabs);
  const activeTabId = useWorkspace((s) => s.activeTabId);
  const resetWorkspace = useWorkspace((s) => s.resetWorkspace);
  const chatOpen = useWorkspace((s) => s.chatOpen);
  const chatDock = useWorkspace((s) => s.chatDock);
  const chatSize = useWorkspace((s) => s.chatSize);
  const theme = useWorkspace((s) => s.theme);
  const setTheme = useWorkspace((s) => s.setTheme);
  const caps = useWorkspace((s) => s.capabilities);
  const companyLinkRequired = useWorkspace((s) => s.profilesData?.companyLinkRequired ?? false);
  const [showAutomations, setShowAutomations] = useState(false);

  const activeTab = tabs.find((t) => t.id === activeTabId) ?? tabs[0];

  return (
    <div
      className={`ins-app ${chatOpen ? "chat-open" : ""} dock-${chatDock}`}
      style={{ "--chat-size": `${chatSize}px` } as CSSProperties}
    >
      <header className="ins-header">
        <div className="ins-brand">
          <span className="ins-brand-mark">◨</span>
          <span className="ins-brand-name">Company Insights</span>
        </div>
        <div className="ins-header-actions">
          <AddWidgetMenu tabId={activeTab.id} />
          {caps?.scheduling && (
            <button
              className="ins-btn"
              title="Background automations (scheduled + event-driven)"
              onClick={() => setShowAutomations(true)}
            >
              🤖 Automations
            </button>
          )}
          <button
            className="ins-btn subtle"
            title={`Theme: ${theme} (click to change)`}
            aria-label="Toggle theme"
            onClick={() => setTheme(THEME_NEXT[theme])}
          >
            {THEME_ICON[theme]}
          </button>
          <button
            className="ins-btn subtle"
            title="Reset workspace"
            onClick={async () => {
              const ok = await confirmDialog({
                title: "Reset workspace?",
                message: "All tabs and widgets will be cleared.",
                confirmLabel: "Reset",
                danger: true,
              });
              if (ok) resetWorkspace();
            }}
          >
            Reset
          </button>
          <ProfilePicker />
        </div>
      </header>

      {companyLinkRequired && (
        <div className="ins-banner" role="alert">
          <span className="ins-banner-icon">⚠</span>
          <span>
            Your account isn’t linked to a company yet, so Insights has no data to show. Ask an administrator
            to add you to your company (Admin → Companies), then reopen Insights.
          </span>
        </div>
      )}

      <TabBar />

      <main className="ins-main">
        <Canvas tab={activeTab} />
      </main>

      <ChatPanel />

      {showAutomations && <AutomationsModal onClose={() => setShowAutomations(false)} />}
    </div>
  );
}
