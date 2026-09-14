import { useRef, useState } from "react";
import type { View } from "vega";
import type { ChartKind, Dataset, ReportParams, Widget } from "../types";
import { useWorkspace } from "../store/workspace";
import { useReportResult } from "../hooks/useReport";
import { ChartWidget } from "./widgets/ChartWidget";
import { TableWidget } from "./widgets/TableWidget";
import { NoteWidget } from "./widgets/NoteWidget";
import { DividerWidget } from "./widgets/DividerWidget";
import { WidgetConfigModal } from "./WidgetConfigModal";
import { downloadCsv, downloadPng } from "../utils/download";
import { promptDialog, toast } from "../ui/feedback";

const CHART_KINDS: ChartKind[] = ["line", "area", "bar", "pie"];

function ago(ts?: number): string {
  if (!ts) return "";
  const s = Math.round((Date.now() - ts) / 1000);
  if (s < 60) return "just now";
  const m = Math.round(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.round(m / 60);
  return `${h}h ago`;
}

export function WidgetFrame({ tabId, widget }: { tabId: string; widget: Widget }) {
  const removeWidget = useWorkspace((s) => s.removeWidget);
  const updateWidget = useWorkspace((s) => s.updateWidget);
  const duplicateWidget = useWorkspace((s) => s.duplicateWidget);
  const setChartKind = useWorkspace((s) => s.setChartKind);

  const [menuOpen, setMenuOpen] = useState(false);
  const [config, setConfig] = useState(false);
  const [maximized, setMaximized] = useState(false);
  const viewRef = useRef<View | null>(null);
  const dataRef = useRef<Dataset | null>(null);

  const isDivider = widget.type === "divider";
  const reportId = widget.chart?.reportId ?? widget.tableReportId;
  const isReportBacked = !!reportId && !widget.dataset;

  // Shared cache entry — cheap; used for the last-updated stamp + refresh in the header.
  const report = useReportResult(isReportBacked ? reportId : undefined, widget.reportParams);

  function rename() {
    setMenuOpen(false);
    void promptDialog({ title: "Rename widget", defaultValue: widget.title, confirmLabel: "Rename" }).then((name) => {
      if (name && name.trim()) updateWidget(tabId, widget.id, { title: name.trim() });
    });
  }

  function exportData() {
    setMenuOpen(false);
    if (widget.type === "chart") {
      if (!viewRef.current) {
        toast.error("Chart isn't ready yet");
        return;
      }
      void viewRef.current
        .toImageURL("png", 2)
        .then((u: string) => downloadPng(widget.title, u))
        .catch(() => toast.error("PNG export failed"));
    } else if (widget.type === "table") {
      if (!dataRef.current) {
        toast.error("No data to export");
        return;
      }
      downloadCsv(widget.title, dataRef.current);
    }
  }

  const body = (
    <>
      {widget.type === "chart" && widget.chart && (
        <ChartWidget
          config={widget.chart}
          dataset={widget.dataset}
          params={widget.reportParams}
          onView={(v) => (viewRef.current = v)}
        />
      )}
      {widget.type === "table" && (
        <TableWidget
          reportId={widget.tableReportId}
          dataset={widget.dataset}
          params={widget.reportParams}
          onData={(d) => (dataRef.current = d)}
        />
      )}
      {widget.type === "note" && (
        <NoteWidget
          text={widget.noteText ?? ""}
          onChange={(t) => updateWidget(tabId, widget.id, { noteText: t })}
        />
      )}
      {widget.type === "divider" && <DividerWidget label={widget.dividerLabel} />}
    </>
  );

  return (
    <div className={`ins-widget ${isDivider ? "is-divider" : ""}`}>
      <div className="ins-widget-drag">
        <span className="ins-widget-title" title={widget.title}>
          {widget.title}
        </span>
        <div className="ins-widget-actions">
          {isReportBacked && report.fetchedAt && (
            <span className="ins-widget-stamp" title="Data age">
              {ago(report.fetchedAt)}
            </span>
          )}
          {isReportBacked && (
            <button
              className="ins-icon-btn"
              title="Refresh data"
              aria-label="Refresh data"
              onMouseDown={(e) => e.stopPropagation()}
              onClick={() => {
                report.refresh();
                toast.info("Refreshing…");
              }}
            >
              ⟳
            </button>
          )}
          {!isDivider && (
            <div className="ins-wmenu">
              <button
                className="ins-icon-btn"
                title="Widget options"
                aria-label="Widget options"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={() => setMenuOpen((o) => !o)}
              >
                ⋯
              </button>
              {menuOpen && (
                <>
                  <div className="ins-addmenu-backdrop" onClick={() => setMenuOpen(false)} />
                  <div className="ins-wmenu-panel" role="menu">
                    <button className="ins-addmenu-item" onClick={rename}>
                      Rename
                    </button>
                    <button
                      className="ins-addmenu-item"
                      onClick={() => {
                        duplicateWidget(tabId, widget.id);
                        setMenuOpen(false);
                      }}
                    >
                      Duplicate
                    </button>
                    <button
                      className="ins-addmenu-item"
                      onClick={() => {
                        setMaximized(true);
                        setMenuOpen(false);
                      }}
                    >
                      Maximize
                    </button>
                    {widget.type === "chart" && (
                      <>
                        <div className="ins-addmenu-section">Chart type</div>
                        <div className="ins-wmenu-kinds">
                          {CHART_KINDS.map((k) => (
                            <button
                              key={k}
                              className={`ins-chip ${widget.chart?.chartKind === k ? "on" : ""}`}
                              onClick={() => {
                                setChartKind(tabId, widget.id, k);
                                setMenuOpen(false);
                              }}
                            >
                              {k}
                            </button>
                          ))}
                        </div>
                      </>
                    )}
                    {isReportBacked && (
                      <button
                        className="ins-addmenu-item"
                        onClick={() => {
                          setConfig(true);
                          setMenuOpen(false);
                        }}
                      >
                        Configure parameters…
                      </button>
                    )}
                    {(widget.type === "chart" || widget.type === "table") && (
                      <button className="ins-addmenu-item" onClick={exportData}>
                        {widget.type === "chart" ? "Download PNG" : "Download CSV"}
                      </button>
                    )}
                    <button className="ins-addmenu-item danger" onClick={() => removeWidget(tabId, widget.id)}>
                      Remove
                    </button>
                  </div>
                </>
              )}
            </div>
          )}
          {isDivider && (
            <button
              className="ins-icon-btn"
              title="Remove"
              aria-label="Remove widget"
              onMouseDown={(e) => e.stopPropagation()}
              onClick={() => removeWidget(tabId, widget.id)}
            >
              ✕
            </button>
          )}
        </div>
      </div>
      <div className="ins-widget-body">{body}</div>

      {config && (
        <WidgetConfigModal
          widget={widget}
          onClose={() => setConfig(false)}
          onApply={(params: ReportParams) => updateWidget(tabId, widget.id, { reportParams: params })}
        />
      )}

      {maximized && (
        <div className="ins-modal-backdrop" onClick={() => setMaximized(false)} onKeyDown={(e) => e.key === "Escape" && setMaximized(false)}>
          <div className="ins-widget ins-widget-max" onClick={(e) => e.stopPropagation()}>
            <div className="ins-widget-drag" style={{ cursor: "default" }}>
              <span className="ins-widget-title">{widget.title}</span>
              <button className="ins-icon-btn" title="Close" aria-label="Close" onClick={() => setMaximized(false)}>
                ✕
              </button>
            </div>
            <div className="ins-widget-body">{body}</div>
          </div>
        </div>
      )}
    </div>
  );
}
