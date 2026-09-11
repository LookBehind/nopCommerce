import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";
import type { GridItem, Tab, Widget, WidgetType, WorkspaceState } from "../types";

function uid(prefix: string): string {
  const rnd =
    typeof crypto !== "undefined" && "randomUUID" in crypto
      ? crypto.randomUUID().slice(0, 8)
      : Math.random().toString(36).slice(2, 10);
  return `${prefix}_${rnd}`;
}

function emptyTab(name: string): Tab {
  return { id: uid("tab"), name, widgets: [], layout: [] };
}

const DEFAULT_SIZE: Record<WidgetType, { w: number; h: number }> = {
  chart: { w: 6, h: 8 },
  table: { w: 6, h: 8 },
  note: { w: 4, h: 4 },
  divider: { w: 12, h: 1 },
};

/** Place a new widget at the bottom of the current grid. */
function nextPosition(layout: GridItem[], w: number, h: number): GridItem {
  const bottom = layout.reduce((max, it) => Math.max(max, it.y + it.h), 0);
  return { i: "", x: 0, y: bottom, w, h };
}

interface WorkspaceActions {
  addTab: (name?: string) => void;
  removeTab: (tabId: string) => void;
  renameTab: (tabId: string, name: string) => void;
  setActiveTab: (tabId: string) => void;
  resetWorkspace: () => void;
  addWidget: (tabId: string, widget: Omit<Widget, "id">) => void;
  removeWidget: (tabId: string, widgetId: string) => void;
  updateWidget: (tabId: string, widgetId: string, patch: Partial<Widget>) => void;
  setLayout: (tabId: string, layout: GridItem[]) => void;
  toggleChat: () => void;
  setAgent: (agentId: string) => void;
}

const firstTab = emptyTab("Overview");

export const useWorkspace = create<WorkspaceState & WorkspaceActions>()(
  persist(
    (set) => ({
      tabs: [firstTab],
      activeTabId: firstTab.id,
      chatOpen: false,
      selectedAgentId: "analyst",

      addTab: (name) =>
        set((s) => {
          const tab = emptyTab(name?.trim() || `Tab ${s.tabs.length + 1}`);
          return { tabs: [...s.tabs, tab], activeTabId: tab.id };
        }),

      removeTab: (tabId) =>
        set((s) => {
          if (s.tabs.length <= 1) return s;
          const tabs = s.tabs.filter((t) => t.id !== tabId);
          const activeTabId =
            s.activeTabId === tabId ? tabs[tabs.length - 1].id : s.activeTabId;
          return { tabs, activeTabId };
        }),

      renameTab: (tabId, name) =>
        set((s) => ({
          tabs: s.tabs.map((t) => (t.id === tabId ? { ...t, name } : t)),
        })),

      setActiveTab: (tabId) => set({ activeTabId: tabId }),

      resetWorkspace: () =>
        set(() => {
          const t = emptyTab("Overview");
          return { tabs: [t], activeTabId: t.id, chatOpen: false };
        }),

      addWidget: (tabId, widget) =>
        set((s) => ({
          tabs: s.tabs.map((t) => {
            if (t.id !== tabId) return t;
            const id = uid("w");
            const size = DEFAULT_SIZE[widget.type];
            const pos = nextPosition(t.layout, size.w, size.h);
            return {
              ...t,
              widgets: [...t.widgets, { ...widget, id }],
              layout: [...t.layout, { ...pos, i: id }],
            };
          }),
        })),

      removeWidget: (tabId, widgetId) =>
        set((s) => ({
          tabs: s.tabs.map((t) =>
            t.id !== tabId
              ? t
              : {
                  ...t,
                  widgets: t.widgets.filter((w) => w.id !== widgetId),
                  layout: t.layout.filter((l) => l.i !== widgetId),
                }
          ),
        })),

      updateWidget: (tabId, widgetId, patch) =>
        set((s) => ({
          tabs: s.tabs.map((t) =>
            t.id !== tabId
              ? t
              : {
                  ...t,
                  widgets: t.widgets.map((w) =>
                    w.id === widgetId ? { ...w, ...patch } : w
                  ),
                }
          ),
        })),

      setLayout: (tabId, layout) =>
        set((s) => ({
          tabs: s.tabs.map((t) => (t.id === tabId ? { ...t, layout } : t)),
        })),

      toggleChat: () => set((s) => ({ chatOpen: !s.chatOpen })),
      setAgent: (agentId) => set({ selectedAgentId: agentId }),
    }),
    {
      name: "company-insights-workspace-v1",
      storage: createJSONStorage(() => localStorage),
      version: 1,
    }
  )
);
