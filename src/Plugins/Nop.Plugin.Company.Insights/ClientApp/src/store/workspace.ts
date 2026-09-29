import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";
import type {
  Capabilities,
  ChartKind,
  ChatDock,
  CompanyOption,
  GridItem,
  ProfileInfo,
  ProfilesResponse,
  Tab,
  ThemePref,
  Widget,
  WidgetType,
  WorkspaceState,
} from "../types";

interface ProfilesData {
  profiles: ProfileInfo[];
  companies: CompanyOption[];
  isAdmin: boolean;
  defaultId: string;
  ownCompanyId: number | null;
  companyLinkRequired?: boolean;
}

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

/** The durable slice we persist locally AND sync to the server. */
export interface WorkspaceSnapshot {
  tabs: Tab[];
  activeTabId: string;
  selectedAgentId: string;
  selectedProfileId: string;
  selectedCompanyId: number | null;
  theme: ThemePref;
  chatDock: ChatDock;
  chatSize: number;
}

interface WorkspaceActions {
  addTab: (name?: string) => void;
  removeTab: (tabId: string) => void;
  renameTab: (tabId: string, name: string) => void;
  setActiveTab: (tabId: string) => void;
  resetWorkspace: () => void;
  addWidget: (tabId: string, widget: Omit<Widget, "id">) => string;
  removeWidget: (tabId: string, widgetId: string) => void;
  updateWidget: (tabId: string, widgetId: string, patch: Partial<Widget>) => void;
  duplicateWidget: (tabId: string, widgetId: string) => void;
  setChartKind: (tabId: string, widgetId: string, kind: ChartKind) => void;
  setLayout: (tabId: string, layout: GridItem[]) => void;
  toggleChat: () => void;
  setChatOpen: (open: boolean) => void;
  setAgent: (agentId: string) => void;
  setProfile: (profileId: string) => void;
  setCompany: (companyId: number | null) => void;
  setProfilesData: (data: ProfilesResponse) => void;
  setTheme: (theme: ThemePref) => void;
  setChatDock: (dock: ChatDock) => void;
  setChatSize: (size: number) => void;
  setCapabilities: (caps: Capabilities) => void;
  applySnapshot: (snap: Partial<WorkspaceSnapshot>) => void;
  snapshot: () => WorkspaceSnapshot;
}

const firstTab = emptyTab("Overview");

export const useWorkspace = create<
  WorkspaceState & { capabilities: Capabilities | null; profilesData: ProfilesData | null } & WorkspaceActions
>()(
  persist(
    (set, get) => ({
      tabs: [firstTab],
      activeTabId: firstTab.id,
      chatOpen: false,
      selectedAgentId: "analyst",
      selectedProfileId: "",
      selectedCompanyId: null,
      theme: "system",
      chatDock: "bottom",
      chatSize: 380,
      capabilities: null,
      profilesData: null,

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

      addWidget: (tabId, widget) => {
        const id = uid("w");
        set((s) => ({
          tabs: s.tabs.map((t) => {
            if (t.id !== tabId) return t;
            const size = DEFAULT_SIZE[widget.type];
            const pos = nextPosition(t.layout, size.w, size.h);
            return {
              ...t,
              widgets: [...t.widgets, { ...widget, id }],
              layout: [...t.layout, { ...pos, i: id }],
            };
          }),
        }));
        return id;
      },

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

      duplicateWidget: (tabId, widgetId) =>
        set((s) => ({
          tabs: s.tabs.map((t) => {
            if (t.id !== tabId) return t;
            const src = t.widgets.find((w) => w.id === widgetId);
            const srcLayout = t.layout.find((l) => l.i === widgetId);
            if (!src || !srcLayout) return t;
            const id = uid("w");
            const pos = nextPosition(t.layout, srcLayout.w, srcLayout.h);
            return {
              ...t,
              widgets: [...t.widgets, { ...src, id, title: `${src.title} (copy)` }],
              layout: [...t.layout, { ...pos, i: id }],
            };
          }),
        })),

      setChartKind: (tabId, widgetId, kind) =>
        set((s) => ({
          tabs: s.tabs.map((t) =>
            t.id !== tabId
              ? t
              : {
                  ...t,
                  widgets: t.widgets.map((w) =>
                    w.id === widgetId && w.chart
                      ? { ...w, chart: { ...w.chart, chartKind: kind } }
                      : w
                  ),
                }
          ),
        })),

      setLayout: (tabId, layout) =>
        set((s) => ({
          tabs: s.tabs.map((t) => (t.id === tabId ? { ...t, layout } : t)),
        })),

      toggleChat: () => set((s) => ({ chatOpen: !s.chatOpen })),
      setChatOpen: (open) => set({ chatOpen: open }),
      setAgent: (agentId) => set({ selectedAgentId: agentId }),
      setProfile: (selectedProfileId) => set({ selectedProfileId }),
      setCompany: (selectedCompanyId) => set({ selectedCompanyId }),
      setProfilesData: (data) =>
        set((s) => {
          const ids = data.profiles.map((p) => p.id);
          // Keep the current selection if still valid, else fall back to the server default.
          const selectedProfileId = ids.includes(s.selectedProfileId) ? s.selectedProfileId : data.defaultId;
          return {
            profilesData: {
              profiles: data.profiles,
              companies: data.companies,
              isAdmin: data.isAdmin,
              defaultId: data.defaultId,
              ownCompanyId: data.ownCompanyId,
              companyLinkRequired: data.companyLinkRequired,
            },
            selectedProfileId,
          };
        }),
      setTheme: (theme) => set({ theme }),
      setChatDock: (chatDock) => set({ chatDock }),
      setChatSize: (chatSize) => set({ chatSize: Math.max(220, Math.min(900, Math.round(chatSize))) }),
      setCapabilities: (capabilities) => set({ capabilities }),

      applySnapshot: (snap) =>
        set((s) => ({
          tabs: Array.isArray(snap.tabs) && snap.tabs.length ? snap.tabs : s.tabs,
          activeTabId: snap.activeTabId ?? s.activeTabId,
          selectedAgentId: snap.selectedAgentId ?? s.selectedAgentId,
          selectedProfileId: snap.selectedProfileId ?? s.selectedProfileId,
          selectedCompanyId: snap.selectedCompanyId ?? s.selectedCompanyId,
          theme: snap.theme ?? s.theme,
          chatDock: snap.chatDock ?? s.chatDock,
          chatSize: snap.chatSize ?? s.chatSize,
        })),

      snapshot: () => {
        const s = get();
        return {
          tabs: s.tabs,
          activeTabId: s.activeTabId,
          selectedAgentId: s.selectedAgentId,
          selectedProfileId: s.selectedProfileId,
          selectedCompanyId: s.selectedCompanyId,
          theme: s.theme,
          chatDock: s.chatDock,
          chatSize: s.chatSize,
        };
      },
    }),
    {
      name: "company-insights-workspace-v1",
      storage: createJSONStorage(() => localStorage),
      version: 2,
      // Persist only the durable slice; capabilities/chatOpen are transient.
      partialize: (s) => ({
        tabs: s.tabs,
        activeTabId: s.activeTabId,
        selectedAgentId: s.selectedAgentId,
        selectedProfileId: s.selectedProfileId,
        selectedCompanyId: s.selectedCompanyId,
        theme: s.theme,
        chatDock: s.chatDock,
        chatSize: s.chatSize,
      }),
      migrate: (persisted: unknown, version: number) => {
        // v1 -> v2: add theme/dock/size defaults; tolerate any missing/renamed fields.
        const p = (persisted ?? {}) as Record<string, unknown>;
        if (version < 2) {
          p.theme = p.theme ?? "system";
          p.chatDock = p.chatDock ?? "bottom";
          p.chatSize = p.chatSize ?? 380;
        }
        return p as unknown as WorkspaceState;
      },
    }
  )
);
