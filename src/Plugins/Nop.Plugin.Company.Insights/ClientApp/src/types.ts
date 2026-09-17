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

// ---- Combined reports (client-side report joining) ----

export interface CombineSource {
  reportId: string;
  /** Short alias used to disambiguate colliding column names (e.g. "a", "reviews"). */
  alias?: string;
  days?: number;
  limit?: number;
  from?: string;
  to?: string;
  slot?: string;
}

export interface CombineJoin {
  /** Column in the running (already-joined) result. */
  leftField: string;
  /** Column in the next source being joined in. */
  rightField: string;
  type?: "inner" | "left" | "full";
}

export interface CombineComputed {
  name: string;
  /** Arithmetic over columns: + - * / %, parentheses, numbers, and [Column Name] refs. */
  expr: string;
}

/** A recipe for compiling a new report by joining existing ones — computed entirely in the browser. */
export interface CombinedReportRecipe {
  title?: string;
  sources: CombineSource[];
  /** joins[k] joins the running result with sources[k+1]; omit for a single source. */
  joins?: CombineJoin[];
  computed?: CombineComputed[];
  /** Output columns, in order (defaults to all). */
  select?: string[];
  sort?: { by: string; dir?: "asc" | "desc" };
  /** Optional default visualization for the result. */
  chart?: { chartKind?: ChartKind; xField?: string; yField?: string; categoryField?: string } | null;
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
  /** When present, this widget is a client-compiled combined report (recomputable from the recipe). */
  combinedRecipe?: CombinedReportRecipe;
}

export interface ChatTurnResponse {
  reply: string;
  widgets: ChatWidget[];
  /** Recipes to compile into new report widgets in the browser (joined from existing reports). */
  combinedReports?: CombinedReportRecipe[];
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
