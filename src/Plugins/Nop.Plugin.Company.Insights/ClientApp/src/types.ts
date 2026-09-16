// ---- Report (server data) ----

export interface ReportColumn {
  name: string;
  type: "string" | "number" | "date";
}

export interface ReportParam {
  name: string;
  label?: string;
  type?: string;
  default: number;
  min: number;
  max: number;
}

export interface ReportMeta {
  id: string;
  name: string;
  description: string;
  defaultChart: ChartKind;
  xField?: string;
  yField?: string;
  categoryField?: string;
  parameters?: ReportParam[];
}

/** Resolved parameter values for a report-backed widget (subset the UI exposes today). */
export interface ReportParams {
  days?: number;
  limit?: number;
  /** Delivery date range (ISO yyyy-MM-dd) + optional time slot (HH:mm) for date-range reports. */
  from?: string;
  to?: string;
  slot?: string;
}

export interface ReportTotal {
  label: string;
  value: unknown;
}

export interface ReportResult {
  id: string;
  columns: ReportColumn[];
  rows: Record<string, unknown>[];
  totals?: ReportTotal[] | null;
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
  /** Parameter values for a report-backed chart/table widget (days/limit). */
  reportParams?: ReportParams;
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
  persistence: boolean;
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
  days?: number | null;
  limit?: number | null;
  createdAt?: string;
}

// ---- Saved conversations (server persistence) ----

export interface ConversationHeader {
  id: string;
  title: string;
  agentId: string;
  createdAt: string;
  updatedAt: string;
}

export interface Conversation extends ConversationHeader {
  messages: ChatMessage[];
}

// ---- Agent memory management ----

export interface MemoryRow {
  id: number;
  kind: string;
  content: string;
  createdAt: string;
}

// ---- Background agents (automations) ----

/** As returned by /Agents (filter + outputSinks are raw JSON strings). */
export interface AgentConfig {
  id?: string;
  name: string;
  enabled: boolean;
  builtIn?: boolean;
  companyId?: number | null;
  triggerKind: "event" | "schedule";
  eventType?: string | null;
  cron?: string | null;
  filter?: string | null;
  systemPrompt?: string | null;
  instruction?: string | null;
  outputSinks?: string | null;
  outputTarget?: string | null;
  createdAt?: string;
  updatedAt?: string;
}

/** Shape sent to /SaveAgent (filter object + outputSinks array). */
export interface AgentConfigInput {
  id?: string;
  name: string;
  enabled: boolean;
  companyId?: number | null;
  triggerKind: "event" | "schedule";
  eventType?: string;
  cron?: string;
  filter?: Record<string, unknown>;
  systemPrompt?: string;
  instruction?: string;
  outputSinks?: string[];
  outputTarget?: string;
}

export interface AgentRun {
  id: number;
  agentId: string;
  agentName?: string;
  eventId?: number | null;
  triggerType?: string;
  input?: string;
  startedAt: string;
  finishedAt?: string | null;
  durationMs?: number | null;
  status: string;
  output?: string | null;
  error?: string | null;
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

// ---- Profiles (role-gated interactive context) ----

export interface ProfileInfo {
  id: string;
  name: string;
  description: string;
  companyScoped: boolean;
  /** True when this scoped profile needs an explicit company choice (admins with no own company). */
  needsCompany: boolean;
}

export interface CompanyOption {
  id: number;
  name: string;
}

export interface ProfilesResponse {
  defaultId: string;
  isAdmin: boolean;
  ownCompanyId: number | null;
  profiles: ProfileInfo[];
  companies: CompanyOption[];
}

export type ThemePref = "system" | "light" | "dark";
export type ChatDock = "bottom" | "right";

export interface WorkspaceState {
  tabs: Tab[];
  activeTabId: string;
  chatOpen: boolean;
  selectedAgentId: string;
  /** Active profile id (primary interactive context). */
  selectedProfileId: string;
  /** Company chosen by an admin using a company-scoped profile (null otherwise). */
  selectedCompanyId: number | null;
  theme: ThemePref;
  chatDock: ChatDock;
  /** Chat panel size in px: height when docked bottom, width when docked right. */
  chatSize: number;
}
