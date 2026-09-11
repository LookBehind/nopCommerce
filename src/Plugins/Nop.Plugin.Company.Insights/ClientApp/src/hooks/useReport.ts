import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { ReportMeta, ReportResult } from "../types";

// Simple in-memory cache so multiple widgets bound to the same report share one fetch.
const resultCache = new Map<string, Promise<ReportResult>>();

export function fetchReport(id: string): Promise<ReportResult> {
  let p = resultCache.get(id);
  if (!p) {
    p = api.runReport(id);
    resultCache.set(id, p);
  }
  return p;
}

export function invalidateReports() {
  resultCache.clear();
}

interface State {
  loading: boolean;
  error?: string;
  result?: ReportResult;
}

export function useReportResult(reportId: string | undefined): State {
  const [state, setState] = useState<State>({ loading: !!reportId });

  useEffect(() => {
    if (!reportId) {
      setState({ loading: false });
      return;
    }
    let cancelled = false;
    setState({ loading: true });
    fetchReport(reportId)
      .then((result) => !cancelled && setState({ loading: false, result }))
      .catch((e) => !cancelled && setState({ loading: false, error: String(e) }));
    return () => {
      cancelled = true;
    };
  }, [reportId]);

  return state;
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
