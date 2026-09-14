import { useMemo } from "react";
import { Responsive, WidthProvider, type Layout, type Layouts } from "react-grid-layout";
import type { Tab } from "../types";
import { useWorkspace } from "../store/workspace";
import { WidgetFrame } from "./WidgetFrame";

const Grid = WidthProvider(Responsive);

const COLS = { lg: 12, md: 10, sm: 6, xs: 4, xxs: 2 };
const BREAKPOINTS = { lg: 1200, md: 996, sm: 768, xs: 480, xxs: 0 };

export function Canvas({ tab }: { tab: Tab }) {
  const setLayout = useWorkspace((s) => s.setLayout);

  const layouts = useMemo<Layouts>(
    () => ({ lg: tab.layout.map((l) => ({ ...l })) }),
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
      layouts={layouts}
      cols={COLS}
      breakpoints={BREAKPOINTS}
      rowHeight={40}
      margin={[12, 12]}
      containerPadding={[16, 16]}
      draggableHandle=".ins-widget-drag"
      isBounded={false}
      onLayoutChange={(current: Layout[], all: Layouts) => {
        const source = all.lg ?? current;
        setLayout(
          tab.id,
          source.map((n) => ({ i: n.i, x: n.x, y: n.y, w: n.w, h: n.h }))
        );
      }}
    >
      {tab.widgets.map((w) => (
        <div key={w.id} className="ins-grid-item">
          <WidgetFrame tabId={tab.id} widget={w} />
        </div>
      ))}
    </Grid>
  );
}
