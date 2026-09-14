import type { ReportMeta, ReportParams } from "../types";

/** Default param values from a report's declared parameter metadata. */
export function defaultParams(report?: ReportMeta): ReportParams {
  const p: ReportParams = {};
  for (const param of report?.parameters ?? []) {
    if (param.name === "days") p.days = param.default;
    if (param.name === "limit") p.limit = param.default;
  }
  return p;
}

function clamp(v: number, min: number, max: number) {
  return Math.max(min, Math.min(max, v));
}

/** Renders one bounded control per declared parameter (days/limit today). */
export function ReportParamControls({
  report,
  value,
  onChange,
  compact,
}: {
  report: ReportMeta;
  value: ReportParams;
  onChange: (next: ReportParams) => void;
  compact?: boolean;
}) {
  const params = report.parameters ?? [];
  if (params.length === 0) return null;

  const set = (name: string, raw: number, min: number, max: number) => {
    const v = clamp(Math.round(raw) || min, min, max);
    onChange({ ...value, [name]: v });
  };

  return (
    <div className={`ins-params ${compact ? "compact" : ""}`}>
      {params.map((param) => {
        const cur =
          param.name === "days" ? value.days ?? param.default : value.limit ?? param.default;
        const quick = param.name === "days" ? [7, 30, 90] : [25, 50, 100, 200];
        return (
          <div key={param.name} className="ins-param">
            <label className="ins-param-label">
              {param.label || param.name}
              <span className="ins-muted"> (max {param.max})</span>
            </label>
            <div className="ins-param-row">
              <input
                type="number"
                min={param.min}
                max={param.max}
                value={cur}
                onChange={(e) => set(param.name, Number(e.target.value), param.min, param.max)}
              />
              <div className="ins-param-quick">
                {quick
                  .filter((q) => q >= param.min && q <= param.max)
                  .map((q) => (
                    <button
                      key={q}
                      type="button"
                      className={`ins-chip ${cur === q ? "on" : ""}`}
                      onClick={() => set(param.name, q, param.min, param.max)}
                    >
                      {q}
                    </button>
                  ))}
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
