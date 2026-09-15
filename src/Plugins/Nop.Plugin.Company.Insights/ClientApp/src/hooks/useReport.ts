import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { ProfileContext } from "../api/client";
import { useWorkspace } from "../store/workspace";
import type { ReportMeta, ReportParams, ReportResult } from "../types";

// Cache is keyed by report id + resolved params + profile/company so widgets don't collide across
// windows OR profiles (switching profile/company must refetch — different data scope).
interface Entry {
  promise: Promise<ReportResult>;
  fetchedAt: number;
}
const resultCache = new Map<string, Entry>();
const listeners = new Map<string, Set<() => void>>();

function keyOf(id: string, params?: ReportParams, ctx?: ProfileContext): string {
  return `${id}|${params?.days ?? ""}|${params?.limit ?? ""}|${ctx?.profileId ?? ""}|${ctx?.companyId ?? ""}`;
}

function notify(key: string) {
  listeners.get(key)?.forEach((fn) => fn());
}

/** Read the active profile context from the store (for report scoping). */
function currentCtx(): ProfileContext {
  const s = useWorkspace.getState();
  return { profileId: s.selectedProfileId || undefined, companyId: s.selectedCompanyId };
}

export function fetchReport(id: string, params?: ReportParams, ctx?: ProfileContext): Entry {
  const c = ctx ?? currentCtx();
  const key = keyOf(id, params, c);
  let entry = resultCache.get(key);
  if (!entry) {
    entry = { promise: api.runReport(id, params, c), fetchedAt: Date.now() };
    resultCache.set(key, entry);
  }
  return entry;
}

/** Drop a cached result and tell every widget bound to it to refetch. */
export function refreshReport(id: string, params?: ReportParams, ctx?: ProfileContext) {
  const key = keyOf(id, params, ctx ?? currentCtx());
  resultCache.delete(key);
  notify(key);
}

export function invalidateReports() {
  resultCache.clear();
  listeners.forEach((set) => set.forEach((fn) => fn()));
}

interface State {
  loading: boolean;
  error?: string;
  result?: ReportResult;
  fetchedAt?: number;
}

export function useReportResult(reportId: string | undefined, params?: ReportParams): State & { refresh: () => void } {
  const [state, setState] = useState<State>({ loading: !!reportId });
  // Re-key on the active profile/company so a scope change refetches.
  const profileId = useWorkspace((s) => s.selectedProfileId);
  const companyId = useWorkspace((s) => s.selectedCompanyId);
  const ctx: ProfileContext = { profileId: profileId || undefined, companyId };
  const key = reportId ? keyOf(reportId, params, ctx) : "";

  useEffect(() => {
    if (!reportId) {
      setState({ loading: false });
      return;
    }
    let cancelled = false;

    const load = () => {
      setState({ loading: true });
      const entry = fetchReport(reportId, params, ctx);
      entry.promise
        .then((result) => !cancelled && setState({ loading: false, result, fetchedAt: entry.fetchedAt }))
        .catch((e) => !cancelled && setState({ loading: false, error: String(e) }));
    };
    load();

    // Re-run when this key is invalidated (refresh) by any widget.
    let set = listeners.get(key);
    if (!set) {
      set = new Set();
      listeners.set(key, set);
    }
    set.add(load);

    return () => {
      cancelled = true;
      set?.delete(load);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  return { ...state, refresh: () => reportId && refreshReport(reportId, params, ctx) };
}

export function useReportCatalog(): { loading: boolean; reports: ReportMeta[]; error?: string } {
  const [state, setState] = useState<{ loading: boolean; reports: ReportMeta[]; error?: string }>({
    loading: true,
    reports: [],
  });
  // The catalog is profile-filtered, so refetch when the profile changes.
  const profileId = useWorkspace((s) => s.selectedProfileId);
  useEffect(() => {
    let cancelled = false;
    api
      .reports({ profileId: profileId || undefined })
      .then((reports) => !cancelled && setState({ loading: false, reports }))
      .catch((e) => !cancelled && setState({ loading: false, reports: [], error: String(e) }));
    return () => {
      cancelled = true;
    };
  }, [profileId]);
  return state;
}
