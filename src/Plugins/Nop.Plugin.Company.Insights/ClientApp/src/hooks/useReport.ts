import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { ReportMeta, ReportParams, ReportResult } from "../types";

// Cache is keyed by report id + resolved params so widgets with different windows don't collide.
interface Entry {
  promise: Promise<ReportResult>;
  fetchedAt: number;
}
const resultCache = new Map<string, Entry>();
const listeners = new Map<string, Set<() => void>>();

function keyOf(id: string, params?: ReportParams): string {
  return `${id}|${params?.days ?? ""}|${params?.limit ?? ""}`;
}

function notify(key: string) {
  listeners.get(key)?.forEach((fn) => fn());
}

export function fetchReport(id: string, params?: ReportParams): Entry {
  const key = keyOf(id, params);
  let entry = resultCache.get(key);
  if (!entry) {
    entry = { promise: api.runReport(id, params), fetchedAt: Date.now() };
    resultCache.set(key, entry);
  }
  return entry;
}

/** Drop a cached result and tell every widget bound to it to refetch. */
export function refreshReport(id: string, params?: ReportParams) {
  resultCache.delete(keyOf(id, params));
  notify(keyOf(id, params));
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
  const key = reportId ? keyOf(reportId, params) : "";

  useEffect(() => {
    if (!reportId) {
      setState({ loading: false });
      return;
    }
    let cancelled = false;

    const load = () => {
      setState({ loading: true });
      const entry = fetchReport(reportId, params);
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

  return { ...state, refresh: () => reportId && refreshReport(reportId, params) };
}

export function useReportCatalog(): { loading: boolean; reports: ReportMeta[]; error?: string } {
  const [state, setState] = useState<{ loading: boolean; reports: ReportMeta[]; error?: string }>({
    loading: true,
    reports: [],
  });
  useEffect(() => {
    let cancelled = false;
    api
      .reports()
      .then((reports) => !cancelled && setState({ loading: false, reports }))
      .catch((e) => !cancelled && setState({ loading: false, reports: [], error: String(e) }));
    return () => {
      cancelled = true;
    };
  }, []);
  return state;
}
