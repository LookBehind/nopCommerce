import type { ReportMeta, ReportParams } from "../types";

const SLOTS = ["", "09:00", "11:00", "14:00", "19:00"];

function isoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/** Default param values from a report's declared parameter metadata. */
export function defaultParams(report?: ReportMeta): ReportParams {
  const p: ReportParams = {};
  for (const param of report?.parameters ?? []) {
    if (param.name === "days") p.days = param.default;
    if (param.name === "limit") p.limit = param.default;
    if (param.type === "daterange") {
      const today = isoDate(new Date());
      p.from = today;
      p.to = today;
    }
  }
  return p;
}

function clamp(v: number, min: number, max: number) {
  return Math.max(min, Math.min(max, v));
}

/** Compute a preset date range [from, to] in ISO. */
function preset(kind: "today" | "this-week" | "last-week" | "last-month"): { from: string; to: string } {
  const now = new Date();
  const d = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const dow = (d.getDay() + 6) % 7; // Monday = 0
  if (kind === "today") return { from: isoDate(d), to: isoDate(d) };
  if (kind === "this-week") {
    const mon = new Date(d);
    mon.setDate(d.getDate() - dow);
    return { from: isoDate(mon), to: isoDate(d) };
  }
  if (kind === "last-week") {
    const mon = new Date(d);
    mon.setDate(d.getDate() - dow - 7);
    const sun = new Date(mon);
    sun.setDate(mon.getDate() + 6);
    return { from: isoDate(mon), to: isoDate(sun) };
  }
  // last-month = previous calendar month
  const first = new Date(d.getFullYear(), d.getMonth() - 1, 1);
  const last = new Date(d.getFullYear(), d.getMonth(), 0);
  return { from: isoDate(first), to: isoDate(last) };
}

function DateRangeControl({
  value,
  onChange,
}: {
  value: ReportParams;
  onChange: (next: ReportParams) => void;
}) {
  const presets: { key: "today" | "this-week" | "last-week" | "last-month"; label: string }[] = [
    { key: "today", label: "Today" },
    { key: "this-week", label: "This week" },
    { key: "last-week", label: "Last week" },
    { key: "last-month", label: "Last month" },
  ];
  return (
    <div className="ins-param">
      <label className="ins-param-label">Delivery date / range</label>
      <div className="ins-param-quick" style={{ marginBottom: 8 }}>
        {presets.map((p) => (
          <button
            key={p.key}
            type="button"
            className="ins-chip"
            onClick={() => onChange({ ...value, ...preset(p.key) })}
          >
            {p.label}
          </button>
        ))}
      </div>
      <div className="ins-param-row">
        <input type="date" value={value.from ?? ""} onChange={(e) => onChange({ ...value, from: e.target.value })} />
        <span className="ins-muted">→</span>
        <input type="date" value={value.to ?? ""} onChange={(e) => onChange({ ...value, to: e.target.value })} />
        <select value={value.slot ?? ""} onChange={(e) => onChange({ ...value, slot: e.target.value || undefined })} aria-label="Delivery time slot">
          {SLOTS.map((s) => (
            <option key={s} value={s}>
              {s === "" ? "Any time" : s}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
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
        if (param.type === "daterange") {
          return <DateRangeControl key={param.name} value={value} onChange={onChange} />;
        }
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
