import { useState } from "react";
import type { ReportParams, Widget } from "../types";
import { useReportCatalog } from "../hooks/useReport";
import { ReportParamControls, defaultParams } from "./ReportParamControls";

/** Edit a report-backed widget's parameters (days/limit). */
export function WidgetConfigModal({
  widget,
  onClose,
  onApply,
}: {
  widget: Widget;
  onClose: () => void;
  onApply: (params: ReportParams) => void;
}) {
  const { reports } = useReportCatalog();
  const reportId = widget.chart?.reportId ?? widget.tableReportId;
  const report = reports.find((r) => r.id === reportId);

  const [params, setParams] = useState<ReportParams>(
    widget.reportParams ?? (report ? defaultParams(report) : {})
  );

  return (
    <div
      className="ins-modal-backdrop"
      onClick={onClose}
      onKeyDown={(e) => e.key === "Escape" && onClose()}
    >
      <div className="ins-dialog" role="dialog" aria-modal="true" aria-label="Configure widget" onClick={(e) => e.stopPropagation()}>
        <h3 className="ins-dialog-title">Configure “{widget.title}”</h3>
        {!report ? (
          <p className="ins-dialog-msg ins-muted">
            This widget isn't backed by a parameterized report (it holds a fixed data snapshot).
          </p>
        ) : report.parameters && report.parameters.length > 0 ? (
          <ReportParamControls report={report} value={params} onChange={setParams} />
        ) : (
          <p className="ins-dialog-msg ins-muted">This report has no adjustable parameters.</p>
        )}
        <div className="ins-dialog-actions">
          <button className="ins-btn subtle" onClick={onClose}>
            Cancel
          </button>
          <button
            className="ins-btn primary"
            disabled={!report}
            onClick={() => {
              onApply(params);
              onClose();
            }}
          >
            Apply
          </button>
        </div>
      </div>
    </div>
  );
}
