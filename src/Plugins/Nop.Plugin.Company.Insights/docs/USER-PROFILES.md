# Company Insights — User Profiles & Role Mapping

Status: **design / spec** (2026-09-15). Captures the intended user profiles ("views"),
the reports each needs, the global data rules, and how nopCommerce **CustomerRoles** drive
which profile a signed-in user gets. Grounded in dev DB schema facts (verified 2026-09-15,
`prod-mysnacks`).

---

## 1. Concept: the **profile is the primary context provider**

The **profile** is the main selector and the single source of interactive context. It replaces the
old cosmetic agent picker (Analyst / Operations / Finance) — that **standalone agent picker is
hidden** (see §9 for what "agents" become instead). Profiles are **tied to the signed-in user's
nopCommerce CustomerRoles**. A profile bundles everything the session runs under:

- **Identity** — name + description shown in the profile picker.
- **Role gate** — which `CustomerRole` SystemName(s) grant this profile (RBAC, not cosmetic).
- **Assistant persona** — the chat assistant's system prompt / focus **is derived from the active
  profile** (no separate agent choice). Backoffice vs Workplace Manager get different prompts.
- **Report set** — which named reports show in "+ Add" and which tools the assistant may call.
- **Data scope** — a mandatory, server-side data filter. ⚠️ Per-tenant isolation (separate DBs)
  is **not** enough: multiple **companies** live inside one tenant DB, so a Workplace Manager must
  be scoped to **their own company's** data. Enforced by restricting every tool/report query — §7.

The `AccessInsights` permission still gates the whole tool. The profile is a **second layer**:
everyone with `AccessInsights` gets in, but the **role determines which profile(s)** they may use
and therefore what persona, tools, reports, and data they get.

---

## 2. The two profiles

### Profile A — Workplace Manager

The company-side person who organises their organisation's food program *through MySnacks*
(formerly "tenant contact"). Wants to understand vendor performance, category health/competition,
reviews, and delivery reliability.

Reports / interests:

| # | Report | Shape | Notes |
|---|--------|-------|-------|
| A1 | **Vendor traction over time** | Area chart, X = time bucket, Y = # order items, series = vendor | "Gaining vs losing traction." Rank vendors by trend (slope) over the window. |
| A2 | **Products per category** | Bar / table, X = category, Y = # products | Catalog composition. |
| A3 | **Products per category per vendor** | Stacked bar / heat table, category × vendor = # products | Spot over-crowded categories / competition. |
| A4 | **Reviews** (existing `list_reviews`) | Table | Triage workflow. Apply the customer rule (§3). |
| A5 | **Vendor delivery reliability** | Table / bar, per vendor | "Which vendors struggle with deliveries." See §5 — needs a decision on how we define a "delay". |

### Profile B — MySnacks Backoffice Support

MySnacks-side staff handling tenant-employee requests, vendors, and deliveries.

Reports / interests:

| # | Report | Shape | Notes |
|---|--------|-------|-------|
| B1 | **Order items per product per vendor, by delivery date & time** | Table + any viz, grouped by vendor → product, value = qty ordered | THE key report. Driven by a **date / delivery-time picker** (§4). Must show a **grand total** under the table/chart. |
| B1r | Same, over a **range** | This week / last week / last month / custom range | Same report, range mode. |

Both profiles: **"reports should be renderable as any visualization"** — the widget already lets
you switch chart type (line/area/bar/pie) and table; ensure every new report exposes sensible
x/y/category defaults so all viz modes are meaningful, plus a table fallback.

---

## 3. GLOBAL RULE — customers are shown as **email + full name, never CustomerId**

For **every** tool/report that takes or returns a customer:

- **Params**: filter by **email** and/or **full name**, not `customerId`. (Reviews tool: drop
  `customerId`, keep `customerEmail`; add name match.)
- **Output columns**: show **Customer (full name)** + **Email**, never a raw `CustomerId`.

Data facts: full name lives in **GenericAttribute** (`FirstName` + `LastName`, entity `Customer`);
**`Customer.Email`** is a real column. The reviews tool already resolves both — extend the rule
everywhere and remove the `CustomerId` column/param.

---

## 4. New capability: date / delivery-time parameter (calendar component)

Profile B needs a **date picker / calendar** parameter, beyond today's `days`/`limit` ints:

- **Single day + time slot** — pick a delivery date and (optionally) a delivery time slot.
- **Range presets** — this week, last week, last month, **custom range** (two dates).
- Backend: a new report param type (`date` / `daterange`) alongside the existing `int` params;
  the SPA renders a calendar/range control from the param metadata.
- **Grand total** rendered under the table/chart is a first-class output (a report may return a
  `totals` row/summary the widget renders below the viz).

### Delivery date/time — verified data facts

- **`Order.ScheduleDate`** (nvarchar) is the real **promised delivery date *and* time-of-day**,
  stored **UTC+4 (Asia/Yerevan)**. `Order.ScheduleDateTime` is just a copy of `CreatedOnUtc` —
  **do not use it**. (See memory `reference_order_schedule_columns`.)
- Format is ISO with **7 fractional digits** (`2026-09-07 09:00:00.0000000`) → parse with
  **`datetime2`**, NOT `datetime` (`datetime` rejects the 7-digit fraction; ~0% parse).
- Delivery **time slots are real and concentrated**: newest 5000 orders → 14:00 (1937),
  09:00 (1090), 11:00 (652), 19:00 (106), 16:00 (33)… so "by delivery time" is a meaningful axis.
- `OrderItem` has `ProductId` + `Quantity` (no vendor). **Vendor is on `Product.VendorId`** — join
  OrderItem → Product → Vendor. (Vendor ids differ per tenant DB → resolve by name;
  memory `feedback_vendor_ids_per_db`.)
- Category mapping table is **`Product_Category_Mapping`** (NOT `ProductCategory`); `Category`
  has 22 active rows on dev.

---

## 5. Delivery struggle / shipping delay (Profile A5) — DECIDED 2026-09-15

**Definition: actual delivery vs promised.** Per vendor, compare each order's **actual** delivery
(`Shipment.DeliveryDateUtc`, falling back to `Shipment.ShippedDateUtc`) against the **promised**
`Order.ScheduleDate` (parsed as `datetime2`, UTC+4 → normalise to UTC for comparison). Metrics per
vendor: # late, avg/median lateness, on-time %, worst offenders.

- ⚠️ **Dev caveat**: `Shipment` has only 65 rows on dev — this is a **dev data gap, not a schema
  gap**. Prod has real shipment data; on dev we **seed test orders/shipments** to validate A5.
  (User confirmed 2026-09-15.) Verify prod `Shipment` coverage before shipping A5.
- **Fallback signal** for orders with **no** Shipment row: treat as "past-due, not completed" when
  `ScheduleDate` has passed and `OrderStatusId`/`ShippingStatusId` isn't a completed/shipped state
  — surfaced separately so it isn't conflated with measured lateness.

`Order` fields available: `ShippingStatusId`, `OrderStatusId`, `ShippingMethod`, `PickupInStore`
(exclude `PickupInStore` orders from delivery-delay — no delivery leg).

---

## 6. Role → Profile mapping (the core of this doc)

nopCommerce `CustomerRole.SystemName`s present on dev (active):

```
Administrators, Registered, Guests, Vendors,
CompanyDashboardViewer  ("Company Dashboard Viewer"),
CompanyAllowanceVoider, CompanyGiftCardManager, ExternalOrdersVendor,
UnlimitedAccount, AllowanceExcempt, ForumModerators
```

Mapping — DECIDED 2026-09-15:

| Profile | Granted by role(s) | Rationale |
|---------|--------------------|-----------|
| **Backoffice Support** (B) | `Administrators` | MySnacks internal staff. Admins **also** get Profile A (dual view, see below). |
| **Workplace Manager** (A) | `CompanyDashboardViewer` | "Company Dashboard Viewer" = the tenant's reporting contact. Binding is config-tunable per tenant (role SystemNames can differ per tenant DB). |

**Admins get both** (decided): an `Administrators` user may switch between Backoffice Support and
Workplace Manager, defaulting to Backoffice — so support can see what a Workplace Manager sees.

Resolution logic (server-side, authoritative):

1. Load the current user's roles via `IWorkContext` / `ICustomerService.GetCustomerRolesAsync`.
2. Compute the **set of allowed profiles** from a role→profile map (a user may qualify for more
   than one; e.g. an Admin who is also a company viewer).
3. The SPA's profile picker shows **only allowed profiles**; the selected profile is **re-checked
   on the server** on every tool/report call (never trust the client's chosen profile) — a report
   or tool not in the active profile's set is refused.
4. Default profile = the highest-privilege allowed (Admin → Backoffice), else the first allowed.
5. `AccessInsights` still required to enter at all. Consider a config/env map so the role→profile
   binding is tunable per tenant without a code change (role SystemNames can differ per tenant DB).

Security note: profiles are **capability sets enforced on the backend**, mirroring how
`AccessInsights` is enforced — the client picker is only a convenience. This keeps a
workplace-manager from invoking backoffice-only tools by tampering with the request.

(Resolved: admins get both profiles and can switch — see the mapping table above.)

---

## 7. Company data scoping — a Workplace Manager sees ONLY their company (security boundary)

Verified schema (dev, `prod-mysnacks`): companies are **sub-entities inside a tenant DB**, so
per-tenant isolation does NOT scope them.

- **`Company`** table. Dev has **1** company (1069 customer mappings, 21 vendor mappings) — but the
  schema supports many; other tenants / prod may have more. Don't hard-code single-company.
- **`Company_Customer_Mapping(CompanyId, CustomerId)`** — each customer → exactly one company
  (max 1 observed). This is how we resolve "the signed-in user's company".
- **`Company_Vendor_Mapping(CompanyId, VendorId)`** — the vendors a company uses (its vendor set).
- **`Order.CompanyId`** — populated on 100% of orders → direct order scoping.

**Enforcement (server-side, authoritative — never trust the client):**

1. Resolve `companyId` from the current customer via `Company_Customer_Mapping` on **every**
   Workplace Manager call. Cache per request.
2. Inject a **mandatory** filter into every query:
   - Order / order-item / delivery reports → `WHERE Order.CompanyId = @companyId`.
   - Catalog reports (products per category **per vendor**, vendor traction) → restrict vendors to
     the company's set via `Company_Vendor_Mapping` (join or `VendorId IN (…)`).
   - Reviews → the company's vendors' products (and/or reviews authored by the company's employees —
     small sub-decision; default: the company's vendors' products).
3. **Fail closed**: no company mapping (or a customer in 0 companies) ⇒ **empty result**, never a
   fall-through to all-company data. Log it.
4. A Workplace Manager **cannot** pass a `companyId` param; any client-supplied company is ignored.
5. **Backoffice (Administrators)** is **unscoped** (all companies) and may *optionally* pass a
   `companyId` / company-name filter. When an admin switches into the Workplace Manager view, they
   must pick which company to view (they have no single company of their own).

Testing note: dev has only 1 company, so cross-company leakage can't be fully proven there —
**seed a second company + a manager mapped to it** to prove isolation before prod.

---

## 8. "Agents" = background workers (future; hidden from the UI for now)

The word **agent** is repurposed: not an interactive persona the user picks, but a **background
worker** that autonomously performs a specific task, triggered by an **event** or a **schedule**,
and produces an artifact (a flag, a recommendation, a report, a Telegram message). They run
server-side, independent of who is looking at the UI. **Not built yet — the interactive agent
picker is hidden until these exist.**

Examples (illustrative):

- **Product-quality reviewer** — trigger: *a new product is added*. Reads the product and checks it
  against MySnacks catalog guidelines; flags non-compliant products for review.
- **Vendor analyzer** — trigger: *scheduled*. Scans reviews + sales; recommends **decommissioning
  products not purchased for a long time** and doubling down on best-sellers.

Shape (when we build them): a registry of workers, each with a trigger (nopCommerce **event
consumer** for "entity added/updated", or a **Hangfire** recurring job — reuse the existing
scheduling infra), an allowed read-only toolset, an LLM step, and an output sink (DB row surfaced
in the workspace, and/or Telegram via the existing sender). They are **profile-independent** and
have their **own** data scope (typically whole-tenant or per-company as the task dictates).

Relationship to profiles: a profile is *who is looking and what they may see/ask now*; a background
agent is *automation that runs on its own*. A profile's workspace may **surface** a background
agent's outputs (e.g. a "products to decommission" widget), gated by that profile's report set.

Note: today's chat still runs an LLM tool-loop internally, but its persona/tools now come from the
**profile**, not a user-chosen agent. The current `AgentPicker`/`selectedAgentId`/`AGENTS` and the
per-agent `remember`/`recall` memory keying get re-pointed at the profile (memory becomes
per-user+profile) when Phase 2 lands.

---

## 9. Implementation sketch (phased, not yet built)

1. **Customer rule sweep** — reviews tool: remove `customerId` param + `CustomerId` column; ensure
   email + full name in/out. Audit any other customer-touching tool. (Small, do first.)
2. **Profiles + role gate + company scope** — server: profile registry (id, roles[], persona
   prompt, allowed reports/tools); `/Profiles` returns the user's allowed profiles; chat + report
   endpoints derive the assistant persona from the active profile, enforce its report/tool set,
   **and** inject the mandatory company filter for Workplace Manager (§7), fail-closed. SPA:
   **profile picker** becomes the primary selector and the **agent picker is hidden** (+ company
   selector for admins in the Workplace Manager view). This is the security-critical phase.
3. **New reports** — A1 vendor traction (area, OrderItem→Product→Vendor, time-bucketed),
   A2 products/category, A3 products/category/vendor, B1 order-items/product/vendor by
   delivery date+time with **grand total**.
4. **Date/daterange param + calendar UI** — new param type + SPA calendar/range control + presets;
   totals rendering under widgets.
5. **A5 delivery reliability** — after the §5 decision.

Cross-refs: [[project_ai_chat_bi_native_plugin]], `reference_order_schedule_columns`,
`feedback_vendor_ids_per_db`, `reference_nopcommerce_spa_plugin_gotchas`.
