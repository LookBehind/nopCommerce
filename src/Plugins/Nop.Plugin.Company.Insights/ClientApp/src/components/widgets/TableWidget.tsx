import type { Dataset } from "../../types";
import { useReportResult } from "../../hooks/useReport";

export function TableWidget({ reportId, dataset }: { reportId?: string; dataset?: Dataset }) {
  const remote = useReportResult(dataset ? undefined : reportId);
  const source = dataset ?? remote.result;

  const loading = !dataset && remote.loading;
  const error = !dataset ? remote.error : undefined;

  if (loading) return <div className="ins-widget-msg">Loading…</div>;
  if (error) return <div className="ins-widget-msg err">Report error: {error}</div>;
  if (!source || source.rows.length === 0) return <div className="ins-widget-msg">No data</div>;

  return (
    <div className="ins-table-wrap">
      <table className="ins-table">
        <thead>
          <tr>
            {source.columns.map((c) => (
              <th key={c.name} className={c.type === "number" ? "num" : ""}>
                {c.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {source.rows.map((row, i) => (
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

function formatCell(v: unknown, type: string): string {
  if (v === null || v === undefined) return "—";
  if (type === "number" && typeof v === "number") return v.toLocaleString();
  if (type === "date") {
    const d = new Date(String(v));
    return isNaN(d.getTime()) ? String(v) : d.toLocaleDateString();
  }
  return String(v);
}
