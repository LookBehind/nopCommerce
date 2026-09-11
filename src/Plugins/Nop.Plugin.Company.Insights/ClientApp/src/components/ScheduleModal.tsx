import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { Capabilities, ReportMeta, Schedule } from "../types";

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

export function ScheduleModal({ onClose }: { onClose: () => void }) {
  const [reports, setReports] = useState<ReportMeta[]>([]);
  const [schedules, setSchedules] = useState<Schedule[]>([]);
  const [caps, setCaps] = useState<Capabilities | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState("");
  const [reportId, setReportId] = useState("");
  const [frequency, setFrequency] = useState("daily");
  const [time, setTime] = useState("09:00");
  const [weekday, setWeekday] = useState("1");
  const [customCron, setCustomCron] = useState("0 9 * * *");
  const [chatId, setChatId] = useState("");

  async function refresh() {
    try {
      const [rs, sc, pong] = await Promise.all([api.reports(), api.schedules(), api.ping()]);
      setReports(rs);
      setSchedules(sc);
      setCaps(pong.capabilities ?? null);
      setReportId((cur) => cur || (rs[0]?.id ?? ""));
    } catch (e) {
      setError(String(e));
    }
  }

  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function buildCron(): string {
    const [hh, mm] = time.split(":").map((x) => Number(x));
    if (frequency === "hourly") return "0 * * * *";
    if (frequency === "daily") return `${mm} ${hh} * * *`;
    if (frequency === "weekly") return `${mm} ${hh} * * ${weekday}`;
    return customCron.trim();
  }

  async function save() {
    setBusy(true);
    setError(null);
    try {
      const s: Schedule = {
        name: name || reports.find((r) => r.id === reportId)?.name || "Report",
        reportId,
        cron: buildCron(),
        telegramChatId: chatId,
        enabled: true,
      };
      const res = await api.saveSchedule(s);
      if (!res.ok) setError(res.error ?? "save failed");
      else {
        setName("");
        setChatId("");
        await refresh();
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }

  async function remove(id?: string) {
    if (!id) return;
    await api.deleteSchedule(id);
    await refresh();
  }

  async function toggle(s: Schedule) {
    await api.saveSchedule({ ...s, enabled: !s.enabled });
    await refresh();
  }

  return (
    <div className="ins-modal-backdrop" onClick={onClose}>
      <div className="ins-modal" onClick={(e) => e.stopPropagation()}>
        <div className="ins-modal-head">
          <h2>Scheduled reports → Telegram</h2>
          <button className="ins-icon-btn" onClick={onClose} title="Close">
            ✕
          </button>
        </div>

        {caps && !caps.scheduling && (
          <div className="ins-modal-warn">Scheduling needs the Postgres store (not configured here).</div>
        )}
        {caps && caps.scheduling && !caps.telegram && (
          <div className="ins-modal-warn">No Telegram bot token — schedules save but won't deliver until it's set.</div>
        )}
        {error && <div className="ins-modal-warn err">{error}</div>}

        <div className="ins-modal-body">
          <div className="ins-form">
            <label>
              Report
              <select value={reportId} onChange={(e) => setReportId(e.target.value)}>
                {reports.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Name (optional)
              <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Report name" />
            </label>
            <label>
              Frequency
              <select value={frequency} onChange={(e) => setFrequency(e.target.value)}>
                <option value="daily">Daily</option>
                <option value="weekly">Weekly</option>
                <option value="hourly">Hourly</option>
                <option value="custom">Custom cron</option>
              </select>
            </label>
            {(frequency === "daily" || frequency === "weekly") && (
              <label>
                Time (Yerevan)
                <input type="time" value={time} onChange={(e) => setTime(e.target.value)} />
              </label>
            )}
            {frequency === "weekly" && (
              <label>
                Day
                <select value={weekday} onChange={(e) => setWeekday(e.target.value)}>
                  {WEEKDAYS.map((d, i) => (
                    <option key={i} value={i}>
                      {d}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {frequency === "custom" && (
              <label>
                Cron (5-field)
                <input value={customCron} onChange={(e) => setCustomCron(e.target.value)} placeholder="0 9 * * *" />
              </label>
            )}
            <label>
              Telegram chat id
              <input value={chatId} onChange={(e) => setChatId(e.target.value)} placeholder="-1001234567890" />
            </label>
            <div className="ins-form-actions">
              <span className="ins-muted">
                cron: <code>{buildCron()}</code> · Asia/Yerevan
              </span>
              <button className="ins-btn primary" disabled={busy || !reportId || !chatId} onClick={() => void save()}>
                Add schedule
              </button>
            </div>
          </div>

          <div className="ins-sched-list">
            {schedules.length === 0 && <div className="ins-muted">No schedules yet.</div>}
            {schedules.map((s) => (
              <div key={s.id} className="ins-sched-row">
                <div className="ins-sched-main">
                  <div className="ins-sched-name">
                    {s.name} {!s.enabled && <span className="ins-muted">(paused)</span>}
                  </div>
                  <div className="ins-muted ins-sched-sub">
                    {s.reportId} · <code>{s.cron}</code> · → {s.telegramChatId}
                  </div>
                </div>
                <div className="ins-sched-actions">
                  <button className="ins-chip" onClick={() => void toggle(s)}>
                    {s.enabled ? "Pause" : "Resume"}
                  </button>
                  <button className="ins-chip" onClick={() => void remove(s.id)}>
                    Delete
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
