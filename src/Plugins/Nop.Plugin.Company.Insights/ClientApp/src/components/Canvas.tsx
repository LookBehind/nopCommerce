import { useMemo } from "react";
import GridLayout, { WidthProvider, type Layout } from "react-grid-layout";
import type { Tab } from "../types";
import { useWorkspace } from "../store/workspace";
import { WidgetFrame } from "./WidgetFrame";

const Grid = WidthProvider(GridLayout);

export function Canvas({ tab }: { tab: Tab }) {
  const setLayout = useWorkspace((s) => s.setLayout);

  const layout = useMemo<Layout[]>(
    () => tab.layout.map((l) => ({ ...l })),
    [tab.layout]
  );

  if (tab.widgets.length === 0) {
    return (
      <div className="ins-canvas-empty">
        <p>This tab is empty.</p>
        <p className="ins-muted">
          Use <strong>+ Add</strong> to place a chart, table, note or divider — or ask the
          agent to build one for you.
        </p>
      </div>
    );
  }

  return (
    <Grid
      className="ins-grid"
      layout={layout}
      cols={12}
      rowHeight={40}
      margin={[12, 12]}
      containerPadding={[16, 16]}
      draggableHandle=".ins-widget-drag"
      isBounded={false}
      onLayoutChange={(next: Layout[]) =>
        setLayout(
          tab.id,
          next.map((n) => ({ i: n.i, x: n.x, y: n.y, w: n.w, h: n.h }))
        )
      }
    >
      {tab.widgets.map((w) => (
        <div key={w.id} className="ins-grid-item">
          <WidgetFrame tabId={tab.id} widget={w} />
        </div>
      ))}
    </Grid>
  );
}
