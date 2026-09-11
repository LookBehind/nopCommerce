import { useState } from "react";
import { useWorkspace } from "../store/workspace";
import { useReportCatalog } from "../hooks/useReport";
import type { ReportMeta } from "../types";

export function AddWidgetMenu({ tabId }: { tabId: string }) {
  const [open, setOpen] = useState(false);
  const addWidget = useWorkspace((s) => s.addWidget);
  const { loading, reports, error } = useReportCatalog();

  function addChart(r: ReportMeta) {
    addWidget(tabId, {
      type: "chart",
      title: r.name,
      chart: {
        reportId: r.id,
        chartKind: r.defaultChart,
        xField: r.xField,
        yField: r.yField,
        categoryField: r.categoryField,
      },
    });
    setOpen(false);
  }

  function addTable(r: ReportMeta) {
    addWidget(tabId, { type: "table", title: r.name, tableReportId: r.id });
    setOpen(false);
  }

  return (
    <div className="ins-addmenu">
      <button className="ins-btn primary" onClick={() => setOpen((o) => !o)}>
        + Add
      </button>
      {open && (
        <>
          <div className="ins-addmenu-backdrop" onClick={() => setOpen(false)} />
          <div className="ins-addmenu-panel" role="menu">
            <div className="ins-addmenu-section">Layout</div>
            <button
              className="ins-addmenu-item"
              onClick={() => {
                addWidget(tabId, { type: "note", title: "Note", noteText: "" });
                setOpen(false);
              }}
            >
              📝 Note
            </button>
            <button
              className="ins-addmenu-item"
              onClick={() => {
                const label = window.prompt("Divider label (optional)") ?? "";
                addWidget(tabId, { type: "divider", title: "Divider", dividerLabel: label });
                setOpen(false);
              }}
            >
              ➖ Divider
            </button>

            <div className="ins-addmenu-section">From a report</div>
            {loading && <div className="ins-addmenu-msg">Loading reports…</div>}
            {error && <div className="ins-addmenu-msg err">{error}</div>}
            {!loading && !error && reports.length === 0 && (
              <div className="ins-addmenu-msg">No reports available</div>
            )}
            {reports.map((r) => (
              <div key={r.id} className="ins-addmenu-report">
                <div className="ins-addmenu-report-name" title={r.description}>
                  {r.name}
                </div>
                <div className="ins-addmenu-report-actions">
                  <button className="ins-chip" onClick={() => addChart(r)}>
                    Chart
                  </button>
                  <button className="ins-chip" onClick={() => addTable(r)}>
                    Table
                  </button>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
