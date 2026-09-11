import type { ChartConfig, ReportColumn } from "../types";

type Row = Record<string, unknown>;

function vegaType(col: ReportColumn | undefined): "temporal" | "quantitative" | "nominal" {
  if (!col) return "nominal";
  if (col.type === "date") return "temporal";
  if (col.type === "number") return "quantitative";
  return "nominal";
}

/**
 * Build a Vega-Lite spec (as a plain object) from tabular report data + the widget's
 * chart config. Data is embedded inline so each widget is self-contained.
 */
export function buildSpec(
  rows: Row[],
  columns: ReportColumn[],
  config: ChartConfig,
  width: number,
  height: number
): Record<string, unknown> {
  const colByName = (n?: string) => columns.find((c) => c.name === n);
  const x = config.xField;
  const y = config.yField;
  const cat = config.categoryField;

  const base = {
    $schema: "https://vega.github.io/schema/vega-lite/v5.json",
    width: Math.max(80, width),
    height: Math.max(60, height),
    background: "transparent",
    autosize: { type: "fit", contains: "padding" },
    data: { values: rows },
    config: {
      axis: { labelColor: "#9aa3b2", titleColor: "#c7cddb", gridColor: "rgba(255,255,255,.06)", domainColor: "rgba(255,255,255,.12)" },
      legend: { labelColor: "#9aa3b2", titleColor: "#c7cddb" },
      view: { stroke: "transparent" },
    },
  };

  if (config.chartKind === "pie") {
    return {
      ...base,
      mark: { type: "arc", innerRadius: Math.min(width, height) > 260 ? 50 : 0, tooltip: true },
      encoding: {
        theta: { field: y, type: "quantitative", stack: true },
        color: { field: cat ?? x, type: "nominal", legend: { orient: "right" } },
      },
    };
  }

  const mark =
    config.chartKind === "area"
      ? { type: "area", line: true, opacity: 0.5, tooltip: true, point: false }
      : config.chartKind === "bar"
      ? { type: "bar", tooltip: true }
      : { type: "line", point: true, tooltip: true };

  const encoding: Record<string, unknown> = {
    x: { field: x, type: vegaType(colByName(x)), title: null },
    y: { field: y, type: "quantitative", title: null },
  };
  if (cat) {
    encoding.color = { field: cat, type: "nominal", legend: { orient: "top" } };
  }

  return { ...base, mark, encoding };
}
