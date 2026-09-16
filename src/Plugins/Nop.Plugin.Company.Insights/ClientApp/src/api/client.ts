import type {
  AgentConfig,
  AgentConfigInput,
  AgentRun,
  Capabilities,
  ChatTurnResponse,
  Conversation,
  ConversationHeader,
  MemoryRow,
  ProfilesResponse,
  ReportMeta,
  ReportParams,
  ReportResult,
  Schedule,
} from "../types";

/** The active profile + optional company scope, appended to report/chat calls. */
export interface ProfileContext {
  profileId?: string;
  companyId?: number | null;
}

// The SPA is hosted at /Admin/Insights, so the gated JSON API shares that base.
const API_BASE = "/Admin/Insights";

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { Accept: "application/json" },
    credentials: "same-origin",
    signal,
  });
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

// nopCommerce admin gates POSTs with [AutoValidateAntiforgeryToken], reading the token from
// the "RequestVerificationToken" header. The host page embeds it in a meta tag.
function antiforgeryToken(): string {
  const el = document.querySelector('meta[name="request-verification-token"]');
  return el?.getAttribute("content") ?? "";
}

// POST as form-encoded: nopCommerce admin filters read Request.Form (a JSON body makes them
// throw), and antiforgery is satisfied by the __RequestVerificationToken form field. Complex
// payloads ride as a JSON string in a "payload" field.
async function postForm<T>(
  path: string,
  fields: Record<string, string>,
  signal?: AbortSignal
): Promise<T> {
  const body = new URLSearchParams();
  body.set("__RequestVerificationToken", antiforgeryToken());
  for (const [k, v] of Object.entries(fields)) body.set(k, v);

  const res = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/x-www-form-urlencoded",
      RequestVerificationToken: antiforgeryToken(),
    },
    credentials: "same-origin",
    body: body.toString(),
    signal,
  });
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

function reportQuery(params?: ReportParams, ctx?: ProfileContext): string {
  const qs = new URLSearchParams();
  if (params?.days != null) qs.set("days", String(params.days));
  if (params?.limit != null) qs.set("limit", String(params.limit));
  if (params?.from) qs.set("from", params.from);
  if (params?.to) qs.set("to", params.to);
  if (params?.slot) qs.set("slot", params.slot);
  if (ctx?.profileId) qs.set("profile", ctx.profileId);
  if (ctx?.companyId != null) qs.set("companyId", String(ctx.companyId));
  return qs.toString() ? `?${qs.toString()}` : "";
}

export interface PingResult {
  ok: boolean;
  service?: string;
  phase?: string;
  utc?: string;
  capabilities?: Capabilities;
}

export const api = {
  ping: () => getJson<PingResult>("/Ping"),
  profiles: () => getJson<ProfilesResponse>("/Profiles"),

  // reports
  reports: (ctx?: ProfileContext) => getJson<ReportMeta[]>(`/Reports${reportQuery(undefined, ctx)}`),
  runReport: (id: string, params?: ReportParams, ctx?: ProfileContext) =>
    getJson<ReportResult>(`/Reports/${encodeURIComponent(id)}${reportQuery(params, ctx)}`),

  // agent chat — streamed as Server-Sent Events so long turns survive the proxy (Cloudflare ~100s).
  // `onStatus` receives progress notes ("Running a report…"); the final `done` event carries the result.
  chat: async (
    ctx: ProfileContext,
    messages: { role: string; content: string }[],
    onStatus?: (note: string) => void,
    signal?: AbortSignal
  ): Promise<ChatTurnResponse> => {
    const body = new URLSearchParams();
    body.set("__RequestVerificationToken", antiforgeryToken());
    body.set("payload", JSON.stringify({ profileId: ctx.profileId, companyId: ctx.companyId, messages }));

    const res = await fetch(`${API_BASE}/Chat`, {
      method: "POST",
      headers: {
        Accept: "text/event-stream",
        "Content-Type": "application/x-www-form-urlencoded",
        RequestVerificationToken: antiforgeryToken(),
      },
      credentials: "same-origin",
      body: body.toString(),
      signal,
    });
    if (!res.ok || !res.body) throw new Error(`${res.status} ${res.statusText}`);

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = "";
    let result: ChatTurnResponse | null = null;
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });
      // SSE frames are separated by a blank line.
      let sep: number;
      while ((sep = buf.indexOf("\n\n")) >= 0) {
        const frame = buf.slice(0, sep);
        buf = buf.slice(sep + 2);
        let event = "message";
        const dataLines: string[] = [];
        for (const raw of frame.split("\n")) {
          const line = raw.replace(/\r$/, "");
          if (!line || line.startsWith(":")) continue; // comment / heartbeat
          if (line.startsWith("event:")) event = line.slice(6).trim();
          else if (line.startsWith("data:")) dataLines.push(line.slice(5).replace(/^ /, ""));
        }
        const data = dataLines.join("\n");
        if (event === "status") onStatus?.(data);
        else if (event === "done") result = JSON.parse(data) as ChatTurnResponse;
        else if (event === "error") throw new Error(data || "agent error");
      }
    }
    if (!result) throw new Error("stream ended without a result");
    return result;
  },
  warmup: () => postForm<{ ready: boolean }>("/Warmup", {}),

  // workspace persistence (per user)
  getWorkspace: () => getJson<unknown | null>("/Workspace"),
  saveWorkspace: (data: unknown) =>
    postForm<{ ok: boolean }>("/SaveWorkspace", { payload: JSON.stringify(data) }),

  // conversations (per user)
  conversations: () => getJson<ConversationHeader[]>("/Conversations"),
  conversation: (id: string) => getJson<Conversation>(`/Conversation?id=${encodeURIComponent(id)}`),
  saveConversation: (c: { id?: string; title: string; agentId: string; messages: unknown[] }) =>
    postForm<{ ok: boolean; id?: string }>("/SaveConversation", { payload: JSON.stringify(c) }),
  deleteConversation: (id: string) => postForm<{ ok: boolean }>("/DeleteConversation", { id }),

  // agent memory management
  memories: (agentId: string) =>
    getJson<MemoryRow[]>(`/Memories?agentId=${encodeURIComponent(agentId)}`),
  deleteMemory: (id: number) => postForm<{ ok: boolean }>("/DeleteMemory", { id: String(id) }),

  // schedules
  schedules: () => getJson<Schedule[]>("/Schedules"),
  saveSchedule: (s: Schedule) =>
    postForm<{ ok: boolean; schedule?: Schedule; error?: string }>("/SaveSchedule", {
      payload: JSON.stringify(s),
    }),
  deleteSchedule: (id: string) => postForm<{ ok: boolean }>("/DeleteSchedule", { id }),
  testSchedule: (s: Partial<Schedule>) =>
    postForm<{ ok: boolean; error?: string }>("/TestSchedule", { payload: JSON.stringify(s) }),

  // background agents (automations) — profile context scopes them (WM: own company; backoffice: all)
  agents: (ctx?: ProfileContext) => getJson<AgentConfig[]>(`/Agents${ctxQuery(ctx)}`),
  saveAgent: (a: AgentConfigInput, ctx?: ProfileContext) =>
    postForm<{ ok: boolean; agent?: AgentConfig; error?: string }>("/SaveAgent", {
      payload: JSON.stringify(a),
      ...ctxFields(ctx),
    }),
  deleteAgent: (id: string, ctx?: ProfileContext) =>
    postForm<{ ok: boolean; error?: string }>("/DeleteAgent", { id, ...ctxFields(ctx) }),
  draftAgent: (description: string) =>
    postForm<{ ok: boolean; draft?: AgentConfigInput; error?: string }>("/DraftAgent", {
      payload: JSON.stringify({ description }),
    }),
  agentRuns: (agentId?: string, limit = 50, ctx?: ProfileContext) =>
    getJson<AgentRun[]>(
      `/AgentRuns?limit=${limit}${agentId ? `&agentId=${encodeURIComponent(agentId)}` : ""}${ctxQuery(ctx, true)}`
    ),

  // Telegram target discovery (pick a group by name instead of a numeric chat id)
  telegramChats: () => getJson<{ enabled: boolean; chats: TelegramChatOption[] }>("/TelegramChats"),
  telegramDiscover: () =>
    postForm<{ enabled: boolean; chats: TelegramChatOption[] }>("/TelegramDiscover", {}),
};

export interface TelegramChatOption {
  id: string;
  title: string;
  type: string;
  username?: string | null;
}

/** Profile context as a query-string fragment (leading "?" unless `append` prefixes with "&"). */
function ctxQuery(ctx?: ProfileContext, append = false): string {
  const qs = new URLSearchParams();
  if (ctx?.profileId) qs.set("profile", ctx.profileId);
  if (ctx?.companyId != null) qs.set("companyId", String(ctx.companyId));
  const s = qs.toString();
  return s ? `${append ? "&" : "?"}${s}` : "";
}

/** Profile context as form fields for POSTs. */
function ctxFields(ctx?: ProfileContext): Record<string, string> {
  const f: Record<string, string> = {};
  if (ctx?.profileId) f.profile = ctx.profileId;
  if (ctx?.companyId != null) f.companyId = String(ctx.companyId);
  return f;
}
