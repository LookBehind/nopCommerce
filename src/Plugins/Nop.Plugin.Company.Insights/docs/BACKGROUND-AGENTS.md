# Background Agents — Triggers, Event Stream & Fine-tuning

Status: **design / spec** (2026-09-16). Companion to [USER-PROFILES.md](USER-PROFILES.md) §8
("agents = background workers"). Grounded in this fork's existing infra.

Background agents are autonomous workers: something happens → an agent runs a read-only LLM task →
it emits an artifact (a flag, a recommendation, a Telegram message, a dashboard note, a memory).
They are **not** the interactive chat. This doc answers: (1) what fires them, (2) how the event
stream reaches an agent, (3) how a Workplace Manager / admin **creates and fine-tunes** them.

**Decided (2026-09-16):** durable outbox event table + Hangfire; outputs = in-app agent feed +
Telegram + memory (the creator chooses per agent); built-in **and** user-defined agents; agents are
**authored conversationally through the main chat** (the interactive agent writes the background
agent's system prompt and shapes its output); the UI shows a **visual flow graph**; and there is a
**run-history / observability page**.

---

## 1. Trigger taxonomy — two kinds

**(A) Schedule** — a cron the user sets (e.g. "every weekday 18:00"). Owned by Hangfire recurring
jobs (we already do this for scheduled reports via `IRecurringJobManager`).

**(B) Event** — something happened. Each maps to a concrete source in this codebase:

| Event | Source (verified available) |
|---|---|
| review added | `IConsumer<EntityInsertedEvent<ProductReview>>` |
| review triaged | emit from the admin triage action (`ProductReviewController.Edit` — where we already stamp `TriagedOnUtc` on first approval). ⚠️ *Not* a generic `EntityUpdatedEvent` — that carries only the new state, so the null→set transition isn't detectable there. Hook the action we control. |
| order placed | `IConsumer<OrderPlacedEvent>` (`Nop.Core.Domain.Orders`) |
| order cancelled | `IConsumer<OrderCancelledEvent>` |
| delivery approaching (40 min before) | **time-derived** — per-slot Hangfire recurring job, computed from the delivery slots, firing 40 min before each slot (reuse the `PreDeliveryNudgeReconciler` pattern; UTC+4). Not a DB event, not a poll. |
| day closing (last slot passed) | **time-derived** — per-store recurring job firing just after the last enabled slot of the day. |
| product created / updated | `IConsumer<EntityInsertedEvent<Product>>` / `IConsumer<EntityUpdatedEvent<Product>>` (updated = high-volume → debounce, see §7) |

So there are **two producers**: nopCommerce **event consumers** (DB events) and **Hangfire
recurring jobs** (time-derived + user schedules).

---

## 2. Architecture — producers → stream → dispatcher → agent → sink

```
 DB events (IConsumer<…>)  ─┐
 triage action hook       ─┤   write a normalized trigger row (fast, in-txn)
 time jobs (slot / day)   ─┘             │
 user schedule jobs ───────── enqueue ───┤
                                         ▼
                        insights_agent_event   (durable outbox, separate Postgres)
                          {id, tenant, event_type, entity_type, entity_id,
                           company_id, payload jsonb, occurred_at, status}
                                         │
                     Dispatcher (Hangfire): drains new rows, matches enabled
                     agent configs (event_type + scope + filters), enqueues one
                     agent-run job per match, marks the row processed
                                         │
                                         ▼
                  Agent run (Hangfire job): load trigger context via READ-ONLY
                  scoped tools → LLM (reuse the agent loop) → write to the sink;
                  every run logged to insights_agent_run (§9 → observability page)
                                         │
                        ┌────────────────┼───────────────────────┐
                    Telegram          dashboard "agent           agent memory
                    (bot)             feed" widget               (pgvector)

 Authoring: the main chat's meta-agent drafts an insights_agent config (trigger,
 filter, output, and the background agent's system prompt) → user confirms/edits
 it in the flow-graph editor → saved. Built-ins ship as insights_agent rows.
```

**Why this shape — the key decision.** The consumer runs **on the request hot path** (placing an
order, saving a review): it must return in milliseconds. LLM runs take seconds–minutes (the model
scales to zero → cold start). So the consumer **never runs the LLM inline** — it only **enqueues a
trigger** (one INSERT). A background **dispatcher** then matches + runs agents. This gives:

- **Decoupling** — the storefront/admin request isn't blocked by a slow agent.
- **Durability & replay** — `insights_agent_event` is the event stream itself: survives restarts,
  auditable, replayable, back-fillable.
- **Idempotency** — a `status` column + a fired-ledger stop double-runs (§7).
- **Fan-out** — one event can match several agent configs.

**Event table vs. pure Hangfire?** We could skip the table and have consumers
`IBackgroundJobClient.Enqueue<Dispatcher>(…)` directly (Hangfire persists jobs in the tenant DB).
Simpler, but loses the queryable/auditable **stream**. Recommended hybrid: **insert the event row
(the durable stream) AND enqueue a dispatch job** for low latency; a slow recurring drain is the
safety net for anything missed. The table is the source of truth; Hangfire is the execution engine.

---

## 3. Time-derived triggers — reuse the slot-cron pattern (no polling)

`PreDeliveryNudgeReconciler` already does exactly this for pre-delivery nudges: it keeps **one
Hangfire recurring job per delivery slot per store**, each firing at a computed cron (there: 1h
before; here: **40 min** before), and **reconciles** (AddOrUpdate / remove) whenever the slot config
changes + once at boot. We mirror it:

- **delivery-approaching** → per-slot job at `slot − 40min` (UTC+4 → cron in UTC). On fire, it scans
  orders due at that slot (optionally per company) and emits `delivery-approaching` trigger rows.
- **day-closing** → per-store job just after the last enabled slot; emits a `day-closing` trigger
  (payload = the day's summary scope).
- **schedule** (user cron) → one recurring job per enabled schedule-kind agent config; on fire it
  emits a `schedule` trigger (or runs the agent directly).

Reconciliation runs at boot and whenever slots or agent configs change — never a busy poll.

---

## 4. Agent config — the thing WM/admin fine-tunes

Stored in the separate Postgres (`insights_agent`, sibling of `insights_schedule`):

```
insights_agent {
  id, tenant, name, enabled, owner_user_id, built_in,
  scope:   { company_id | null(global) },        -- WM: their company; admin: any/global
  trigger: { kind: "event" | "schedule",
             event_type,                          -- for event kind (from the registry, §6)
             cron },                              -- for schedule kind
  filter:  { vendor_ids?, min_rating?, product_ids?, … },  -- optional predicates that narrow fires
  task:    { system_prompt, instruction, allowed_tools/reports }, -- read-only LLM task;
                                                                  -- system_prompt written by the main agent
  output:  { sinks: ["telegram"|"dashboard"|"memory"|"log"], target, shape }, -- creator-defined
  created_at, updated_at
}
```

**Authoring is conversational.** A user creates an agent by asking the **main chat** ("make an agent
that flags 1-star reviews and pings the vendor"). The interactive agent acts as a **meta-agent**: it
proposes the `trigger`, `filter`, `output.sinks`/`shape`, and — crucially — **writes the background
agent's `system_prompt`** and `instruction`, then produces a draft `insights_agent` config the user
confirms/edits. The Automations UI (§8) renders that config as an editable **flow graph**; the user
tweaks nodes and saves. So the model authors the prompt; the human curates. Built-in agents are just
`built_in=true` rows shipped enabled-or-ready.

**Matching (dispatcher):** for an event `(event_type, company_id, entity)`, run every **enabled**
config where `trigger.kind="event"` ∧ `event_type` matches ∧ (`scope.company_id` null ∨ ==
event's company) ∧ `filter` predicates pass. Schedule-kind configs are driven by their own recurring
job, not the event stream.

**Scope reuse:** the agent run resolves a `ReportScope` (from [USER-PROFILES.md] §7) so a
company-scoped agent only ever reads its company's data — same guarantee as interactive reports.

---

## 5. Agent run

1. Load the trigger's context (the entity + related data) through the **read-only, scoped** report/
   query tools — never free SQL, never writes.
2. Build the prompt from `task.instruction` + the event payload + a compact data snapshot.
3. Run the LLM (reuse `InsightsAgentService`'s JSON-action loop, or a leaner single-shot runner).
4. Write the result to `output.sink`: Telegram (existing bot), a **dashboard "agent feed"** the
   workspace surfaces as a widget, agent **memory** (pgvector), or just a log row.

Example configs:
- *Product-quality reviewer* — trigger `product-created`; task "check against catalog guidelines,
  flag if non-compliant"; output → dashboard feed + Telegram.
- *Vendor analyzer* — trigger `schedule` (weekly); task "from reviews + sales, recommend products
  to decommission (stale) vs. promote (best-sellers)"; output → dashboard feed.
- *Bad-review responder* — trigger `review-added`, filter `min_rating<=2`; task "summarise the
  complaint + suggest a resolution"; output → Telegram to the vendor's chat.

---

## 6. Event-type registry

A fixed catalog the UI offers (name, label, description, payload fields, applicable scopes):
`review-added, review-triaged, order-placed, order-cancelled, delivery-approaching, day-closing,
product-created, product-updated`. New types are added in code (each needs a producer); the UI reads
the registry so users pick from supported types only.

---

## 7. Idempotency, safety, cost

- **Fire-once** for time triggers: a fired-ledger keyed by (agent, entity/slot/day) — mirrors what
  the nudge reconciler already needs.
- **Debounce / batch** high-volume events (`product-updated`, bursts of `order-placed`): coalesce
  into one run per N minutes per agent, or per-entity dedupe within a window.
- **Read-only** always (agents never write business data); output sinks are constrained; company
  scope enforced on the run.
- **Cost / cold-start**: cap concurrent agent runs (Hangfire queue + concurrency limit); batch where
  possible; the model scales to zero, so prefer batched scheduled runs over many tiny event runs.
- **Failure**: dispatcher/agent jobs are Hangfire-retried; the event row stays until processed.
- **Retention / cleanup**: a Hangfire **daily recurring job** purges `insights_agent_event` and
  `insights_agent_run` rows older than **30 days** (per tenant, in its Postgres) — bounded, auditable
  history without unbounded growth. Batched deletes; only terminal (processed / finished) rows are
  eligible so nothing in-flight is dropped.

---

## 8. Automations UI — conversational authoring + flow graph + run history

Three surfaces, all role-gated (WM → own company; Admin → global/any company):

**8a. Create via chat.** The primary path: the user asks the main chat to build an agent; the
meta-agent (§4) drafts the config and opens it in the graph editor for confirmation. No blank form
to fill — the model does the heavy lifting (trigger choice, filters, and the background agent's
system prompt), the human curates.

**8b. Flow graph editor.** Each automation renders as a **left-to-right node graph** so the flow is
legible at a glance and editable:

```
[ Trigger ]──▶[ Filter? ]──▶[ Agent (task/system prompt) ]──▶[ Output(s) ]
  event/cron     conditions      the LLM step                  feed / telegram / memory
```

Nodes are clickable to edit (change the trigger, tweak a filter, edit the prompt the model wrote,
add/remove output sinks). Enable/disable, duplicate, delete from here. Likely lib: a lightweight
flow renderer (e.g. React Flow / `@xyflow/react`) inside the SPA — plain enough for non-technical
users. A list view complements the graph for many automations.

**8c. Run history / observability page.** A table of **agent runs** (newest first) answering "which
agent ran when, on what, and did it fail": columns = agent, trigger (event type + the entity/input),
started/finished, duration, status (ok / error / skipped), output summary, and the error/trace on
failure. Backed by `insights_agent_run` (§9). Filter by agent / status / date; open a run to see its
full input payload, the LLM's steps, and the produced output. This is the trust surface — users can
see exactly what their automations did.

The existing **scheduled reports** (`insights_schedule`) are a special case of a *schedule*-kind
automation (output = a report to Telegram). Recommend keeping them separate for now; later unify
into `insights_agent` (`trigger.kind="schedule"`, a "report" task).

---

## 9. Run ledger (observability) + remaining decisions

**`insights_agent_run`** (separate Postgres) — one row per agent execution, powering §8c:

```
insights_agent_run {
  id, tenant, agent_id, event_id?,        -- event_id links to insights_agent_event (null for schedule)
  trigger_type, input jsonb,              -- the entity/context the run received
  started_at, finished_at, duration_ms,
  status: "ok" | "error" | "skipped",
  output jsonb,                           -- what it produced / where it was sent
  error text                              -- message + trace on failure
}
```

**Decided (2026-09-16):** hybrid outbox table + Hangfire; sinks = feed + Telegram + memory (creator
chooses per agent, shaped conversationally); built-in **and** user-defined; conversational authoring
with the main agent writing the system prompt; flow-graph editor; run-history page. Schedules stay
separate for now. Agents always run **inside one tenant instance** → per-tenant; no cross-tenant fan-out.

**Still open:** (1) flow-graph library (React Flow vs. hand-rolled SVG); (2) whether a user-edited
system prompt can be "re-generated" by the main agent on request; (3) debounce windows for
high-volume events; (4) which built-ins ship enabled vs. ready-to-enable.

---

## 10. Phasing (when we build)

1. **Event stream** — `insights_agent_event` table + the DB-event consumers (review/order/product) +
   the triage-action hook + the dispatcher skeleton (log-only sink). Prove events land + dispatch.
2. **Time triggers** — delivery-approaching + day-closing per-slot jobs (reuse the nudge reconciler).
3. **Agent config + runner + run ledger** — `insights_agent` store, matching, the scoped read-only
   LLM run, sinks (feed + Telegram + memory), `insights_agent_run` logging every execution, and the
   daily 30-day retention cleanup for both the event stream and run history (§7).
4. **Conversational authoring** — main-chat meta-agent that drafts a config + writes the background
   agent's system prompt, returning it for confirmation.
5. **Automations UI** — flow-graph editor (view/edit nodes) + the run-history/observability page,
   role-gated.
6. **Built-in agents** — product-quality reviewer, vendor analyzer, bad-review responder.

Cross-refs: [[USER-PROFILES.md]] (profiles, ReportScope), `PreDeliveryNudgeReconciler`
(slot-cron pattern), `InsightsScheduleService` (Hangfire recurring + Postgres store precedent),
`InsightsAgentService` (the LLM tool loop to reuse).
