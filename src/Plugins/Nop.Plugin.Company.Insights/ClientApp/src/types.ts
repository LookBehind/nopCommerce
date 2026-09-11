// ---- Report (server data) ----

export interface ReportColumn {
  name: string;
  type: "string" | "number" | "date";
}

export interface ReportMeta {
  id: string;
  name: string;
  description: string;
  defaultChart: ChartKind;
  xField?: string;
  yField?: string;
  categoryField?: string;
}

export interface ReportResult {
  id: string;
  columns: ReportColumn[];
  rows: Record<string, unknown>[];
}

// ---- Widgets & canvas ----

export type WidgetType = "chart" | "table" | "note" | "divider";
export type ChartKind = "line" | "area" | "bar" | "pie";

export interface ChartConfig {
  reportId?: string;
  chartKind: ChartKind;
  xField?: string;
  yField?: string;
  categoryField?: string;
}

/** Inline data snapshot (used by agent-generated widgets that aren't backed by a live report). */
export interface Dataset {
  columns: ReportColumn[];
  rows: Record<string, unknown>[];
}

export interface Widget {
  id: string;
  type: WidgetType;
  title: string;
  // chart / table
  chart?: ChartConfig;
  tableReportId?: string;
  dataset?: Dataset;
  // note
  noteText?: string;
  // divider
  dividerLabel?: string;
}

// ---- Agent chat ----

export interface ChatWidget {
  type: "chart" | "table";
  title: string;
  chartKind?: ChartKind;
  xField?: string;
  yField?: string;
  categoryField?: string;
  columns: ReportColumn[];
  rows: Record<string, unknown>[];
}

export interface ChatTurnResponse {
  reply: string;
  widgets: ChatWidget[];
}

export interface ChatMessage {
  role: "user" | "assistant";
  content: string;
  widgets?: ChatWidget[];
  error?: boolean;
}

// ---- Scheduling / capabilities ----

export interface Capabilities {
  memory: boolean;
  scheduling: boolean;
  telegram: boolean;
}

export interface Schedule {
  id?: string;
  name: string;
  reportId: string;
  cron: string;
  telegramChatId: string;
  enabled: boolean;
  createdAt?: string;
}

/** react-grid-layout item (subset we persist). */
export interface GridItem {
  i: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface Tab {
  id: string;
  name: string;
  widgets: Widget[];
  layout: GridItem[];
}

export interface Agent {
  id: string;
  name: string;
  description: string;
}

export interface WorkspaceState {
  tabs: Tab[];
  activeTabId: string;
  chatOpen: boolean;
  selectedAgentId: string;
}
