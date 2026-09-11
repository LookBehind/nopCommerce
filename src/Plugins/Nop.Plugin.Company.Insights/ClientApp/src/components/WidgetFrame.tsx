import type { Widget } from "../types";
import { useWorkspace } from "../store/workspace";
import { ChartWidget } from "./widgets/ChartWidget";
import { TableWidget } from "./widgets/TableWidget";
import { NoteWidget } from "./widgets/NoteWidget";
import { DividerWidget } from "./widgets/DividerWidget";

export function WidgetFrame({ tabId, widget }: { tabId: string; widget: Widget }) {
  const removeWidget = useWorkspace((s) => s.removeWidget);
  const updateWidget = useWorkspace((s) => s.updateWidget);

  const isDivider = widget.type === "divider";

  return (
    <div className={`ins-widget ${isDivider ? "is-divider" : ""}`}>
      <div className="ins-widget-drag">
        <span className="ins-widget-title" title={widget.title}>
          {widget.title}
        </span>
        <div className="ins-widget-actions">
          <button
            className="ins-icon-btn"
            title="Remove"
            onMouseDown={(e) => e.stopPropagation()}
            onClick={() => removeWidget(tabId, widget.id)}
          >
            ✕
          </button>
        </div>
      </div>
      <div className="ins-widget-body">
        {widget.type === "chart" && widget.chart && (
          <ChartWidget config={widget.chart} dataset={widget.dataset} />
        )}
        {widget.type === "table" && (
          <TableWidget reportId={widget.tableReportId} dataset={widget.dataset} />
        )}
        {widget.type === "note" && (
          <NoteWidget
            text={widget.noteText ?? ""}
            onChange={(t) => updateWidget(tabId, widget.id, { noteText: t })}
          />
        )}
        {widget.type === "divider" && <DividerWidget label={widget.dividerLabel} />}
      </div>
    </div>
  );
}
