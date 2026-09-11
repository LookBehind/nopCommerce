import { useMemo } from "react";
import { VegaLite } from "react-vega";
import type { ChartConfig, Dataset } from "../../types";
import { useReportResult } from "../../hooks/useReport";
import { useElementSize } from "../../hooks/useElementSize";
import { buildSpec } from "../../charts/vegaSpec";

export function ChartWidget({ config, dataset }: { config: ChartConfig; dataset?: Dataset }) {
  // Live report widgets fetch by id; agent-pinned widgets carry an inline dataset.
  const remote = useReportResult(dataset ? undefined : config.reportId);
  const { ref, width, height } = useElementSize<HTMLDivElement>();

  const source = dataset ?? remote.result;

  const spec = useMemo(() => {
    if (!source || width < 20 || height < 20) return null;
    return buildSpec(source.rows, source.columns, config, width - 8, height - 8);
  }, [source, config, width, height]);

  const loading = !dataset && remote.loading;
  const error = !dataset ? remote.error : undefined;

  return (
    <div ref={ref} className="ins-chart">
      {loading && <div className="ins-widget-msg">Loading…</div>}
      {error && <div className="ins-widget-msg err">Report error: {error}</div>}
      {!loading && !error && source && source.rows.length === 0 && (
        <div className="ins-widget-msg">No data</div>
      )}
      {spec && <VegaLite spec={spec as never} actions={false} renderer="canvas" />}
    </div>
  );
}
