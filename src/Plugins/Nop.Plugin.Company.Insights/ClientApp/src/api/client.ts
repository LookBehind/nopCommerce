import type { Capabilities, ChatTurnResponse, ReportMeta, ReportResult, Schedule } from "../types";

// The SPA is hosted at /Admin/Insights, so the gated JSON API shares that base.
const API_BASE = "/Admin/Insights";

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { Accept: "application/json" },
    credentials: "same-origin",
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
async function postForm<T>(path: string, fields: Record<string, string>): Promise<T> {
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
  });
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
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
  reports: () => getJson<ReportMeta[]>("/Reports"),
  runReport: (id: string, params?: { from?: string; to?: string }) => {
    const qs = new URLSearchParams();
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    const suffix = qs.toString() ? `?${qs.toString()}` : "";
    return getJson<ReportResult>(`/Reports/${encodeURIComponent(id)}${suffix}`);
  },
  chat: (agentId: string, messages: { role: string; content: string }[]) =>
    postForm<ChatTurnResponse>("/Chat", { payload: JSON.stringify({ agentId, messages }) }),
  schedules: () => getJson<Schedule[]>("/Schedules"),
  saveSchedule: (s: Schedule) =>
    postForm<{ ok: boolean; schedule?: Schedule; error?: string }>("/SaveSchedule", {
      payload: JSON.stringify(s),
    }),
  deleteSchedule: (id: string) => postForm<{ ok: boolean }>("/DeleteSchedule", { id }),
};
