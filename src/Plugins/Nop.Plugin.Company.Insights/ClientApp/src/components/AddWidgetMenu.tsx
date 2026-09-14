import { useState } from "react";
import { useWorkspace } from "../store/workspace";
import { useReportCatalog } from "../hooks/useReport";
import { ReportParamControls, defaultParams } from "./ReportParamControls";
import { promptDialog } from "../ui/feedback";
import type { ReportMeta, ReportParams } from "../types";

export function AddWidgetMenu({ tabId }: { tabId: string }) {
  const [open, setOpen] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [paramsById, setParamsById] = useState<Record<string, ReportParams>>({});
  const addWidget = useWorkspace((s) => s.addWidget);
  const { loading, reports, error } = useReportCatalog();

  function paramsFor(r: ReportMeta): ReportParams {
    return paramsById[r.id] ?? defaultParams(r);
  }

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
      reportParams: paramsFor(r),
    });
    close();
  }

  function addTable(r: ReportMeta) {
    addWidget(tabId, { type: "table", title: r.name, tableReportId: r.id, reportParams: paramsFor(r) });
    close();
  }

  function close() {
    setOpen(false);
    setExpanded(null);
  }

  return (
    <div className="ins-addmenu">
      <button className="ins-btn primary" onClick={() => setOpen((o) => !o)} aria-haspopup="menu" aria-expanded={open}>
        + Add
      </button>
      {open && (
        <>
          <div className="ins-addmenu-backdrop" onClick={close} />
          <div className="ins-addmenu-panel" role="menu">
            <div className="ins-addmenu-section">Layout</div>
            <button
              className="ins-addmenu-item"
              onClick={() => {
                addWidget(tabId, { type: "note", title: "Note", noteText: "" });
                close();
              }}
            >
              📝 Note
            </button>
            <button
              className="ins-addmenu-item"
              onClick={() => {
                void promptDialog({ title: "Divider label", placeholder: "Optional label", confirmLabel: "Add" }).then((label) => {
                  addWidget(tabId, { type: "divider", title: "Divider", dividerLabel: label ?? "" });
                  close();
                });
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
            {reports.map((r) => {
              const hasParams = (r.parameters ?? []).length > 0;
              const isOpen = expanded === r.id;
              return (
                <div key={r.id} className="ins-addmenu-reportblock">
                  <div className="ins-addmenu-report">
                    <button
                      className="ins-addmenu-report-name as-button"
                      title={r.description}
                      onClick={() => hasParams && setExpanded(isOpen ? null : r.id)}
                    >
                      {hasParams ? (isOpen ? "▾ " : "▸ ") : ""}
                      {r.name}
                    </button>
                    <div className="ins-addmenu-report-actions">
                      <button className="ins-chip" onClick={() => addChart(r)}>
                        Chart
                      </button>
                      <button className="ins-chip" onClick={() => addTable(r)}>
                        Table
                      </button>
                    </div>
                  </div>
                  {hasParams && isOpen && (
                    <ReportParamControls
                      report={r}
                      value={paramsFor(r)}
                      compact
                      onChange={(next) => setParamsById((m) => ({ ...m, [r.id]: next }))}
                    />
                  )}
                </div>
              );
            })}
          </div>
        </>
      )}
    </div>
  );
}
