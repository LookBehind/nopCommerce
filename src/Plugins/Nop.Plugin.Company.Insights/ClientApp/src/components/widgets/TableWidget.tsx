import { useEffect, useMemo, useState } from "react";
import type { Dataset, ReportParams } from "../../types";
import { useReportResult } from "../../hooks/useReport";

export function TableWidget({
  reportId,
  dataset,
  params,
  onData,
}: {
  reportId?: string;
  dataset?: Dataset;
  params?: ReportParams;
  onData?: (data: Dataset) => void;
}) {
  const remote = useReportResult(dataset ? undefined : reportId, params);
  const source = dataset ?? remote.result;

  const loading = !dataset && remote.loading;
  const error = !dataset ? remote.error : undefined;

  const [sort, setSort] = useState<{ col: string; dir: "asc" | "desc" } | null>(null);

  useEffect(() => {
    if (source) onData?.(source);
  }, [source, onData]);

  const rows = useMemo(() => {
    if (!source) return [];
    if (!sort) return source.rows;
    const { col, dir } = sort;
    const factor = dir === "asc" ? 1 : -1;
    return [...source.rows].sort((a, b) => cmp(a[col], b[col]) * factor);
  }, [source, sort]);

  if (loading) return <div className="ins-widget-msg">Loading…</div>;
  if (error) return <div className="ins-widget-msg err">Report error: {error}</div>;
  if (!source || source.rows.length === 0)
    return (
      <div className="ins-widget-msg">
        No data in this range{params?.days ? ` (last ${params.days} days)` : ""}.
      </div>
    );

  function toggleSort(col: string) {
    setSort((cur) =>
      cur?.col === col ? { col, dir: cur.dir === "asc" ? "desc" : "asc" } : { col, dir: "asc" }
    );
  }

  return (
    <div className="ins-table-wrap">
      <table className="ins-table">
        <thead>
          <tr>
            {source.columns.map((c) => (
              <th
                key={c.name}
                className={`${c.type === "number" ? "num" : ""} sortable`}
                onClick={() => toggleSort(c.name)}
                title="Click to sort"
              >
                {c.name}
                {sort?.col === c.name && <span className="ins-sort-arrow">{sort.dir === "asc" ? " ▲" : " ▼"}</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {source.columns.map((c) => (
                <td key={c.name} className={c.type === "number" ? "num" : ""}>
                  {formatCell(row[c.name], c.type)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function cmp(a: unknown, b: unknown): number {
  if (a === b) return 0;
  if (a === null || a === undefined) return -1;
  if (b === null || b === undefined) return 1;
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b), undefined, { numeric: true });
}

function formatCell(v: unknown, type: string): string {
  if (v === null || v === undefined) return "—";
  if (type === "number" && typeof v === "number") return v.toLocaleString();
  if (type === "date") {
    const d = new Date(String(v));
    return isNaN(d.getTime()) ? String(v) : d.toLocaleDateString();
  }
  return String(v);
}
