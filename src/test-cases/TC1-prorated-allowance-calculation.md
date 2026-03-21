# TC1: Prorated Allowance Calculation for Mid-Period Employee

## Objective
Verify that a customer created mid-period gets a correctly prorated allowance, counting only working days (Mon-Fri) from the customer's creation date to the end of the period.

## Preconditions
- A Company exists with a configured `AmountLimit` (e.g., $10,000) and a known `AmountLimitType` (Daily, Weekly, or Monthly).
- A Customer is linked to that Company via `CompanyCustomer`.
- The customer has no existing paid orders in the current period.

## Code Under Test
- `GetProratedLimit()` — `CheckMoneyOrderPaymentProcessor.cs:555-587`
- `CountWorkingDays()` — `CheckMoneyOrderPaymentProcessor.cs:543-552`
- `GetCustomerRemainingAllowance()` — `CheckMoneyOrderPaymentProcessor.cs:606-642`

## Steps & Expected Results

### Scenario 1A: Monthly cadence, customer created mid-month on a weekday
- **Setup:** Company allowance = $10,000/month. Customer created Thu Jan 15, 2026. Schedule date = Jan 20, 2026.
- **Trace:** periodStart=Jan 1, periodEnd=Jan 31. Jan 15 > Jan 1 → proration. totalWorkingDays(Jan 1–31)=22. customerWorkingDays(Jan 15–31)=12 (Jan 31 is Sat). Result = 10000 * 12/22 = $5,454.54.
- **Result:** PASS

### Scenario 1B: Monthly cadence, customer created on a weekend
- **Setup:** Company allowance = $10,000/month. Customer created Sat Jan 17, 2026. Schedule date = Jan 20, 2026.
- **Trace:** customerWorkingDays(Jan 17–31): loop starts at Sat 17, skips Sat+Sun, counts Mon 19–Fri 30 = 10 days. A customer created Mon Jan 19 also gets CountWorkingDays(Jan 19, Jan 31) = 10. Weekend creation is naturally equivalent to next-Monday creation.
- **Result:** PASS — Weekend days are correctly skipped by `CountWorkingDays`.

### Scenario 1C: Customer created on the first day of the period
- **Setup:** Company allowance = $10,000/month. Customer created Jan 1, 2026. Schedule date = Jan 15, 2026.
- **Trace:** periodStart=Jan 1. `customerCreatedOnUtc.Date (Jan 1) <= periodStart (Jan 1)` → true → returns full `limitAmount` = $10,000. No proration.
- **Result:** PASS

### Scenario 1D: Customer created on the last working day of the period
- **Setup:** Company allowance = $10,000/month. Customer created Fri Jan 30, 2026. Schedule date = Jan 30, 2026.
- **Trace:** periodStart=Jan 1, periodEnd=Jan 31. Jan 30 > Jan 1 → proration. totalWorkingDays=22. customerWorkingDays(Jan 30–31): Fri 30 counts, Sat 31 skipped = 1. Result = 10000 * 1/22 = $454.54.
- **Result:** PASS

### Scenario 1E: Weekly cadence
- **Setup:** Company allowance = $5,000/week. Customer created Wed Jan 21, 2026. Schedule date = Jan 21, 2026.
- **Trace:** `periodStart = Jan 21 - (int)Wednesday(3) = Jan 18 (Sunday)`. periodEnd = Jan 18 + 6 = Jan 24 (Saturday). Week runs Sun–Sat. totalWorkingDays(Jan 18–24) = 5 (Mon–Fri). customerWorkingDays(Jan 21–24) = 3 (Wed, Thu, Fri). Result = 5000 * 3/5 = $3,000.
- **Note:** Week boundary is Sunday–Saturday per C# `DayOfWeek` convention (Sunday=0). This is internally consistent between `GetProratedLimit` (line 566) and `GetUsedAllowanceForPeriod` (line 510). If the business expects ISO Monday–Sunday weeks, this would be a discrepancy.
- **Result:** PASS (with observation)

### Scenario 1F: Daily cadence
- **Setup:** Company allowance = $500/day. Customer created today.
- **Trace:** Line 558: `limitType == AmountLimitType.Daily` → immediately returns `limitAmount` ($500). No proration logic runs.
- **Result:** PASS

## Overall Status
- [x] PASS — All 6 scenarios verified

## Observations
1. **Weekly period uses Sunday–Saturday boundaries** (C# `DayOfWeek` where Sunday=0). If the business expects Monday-based weeks (ISO 8601), the calculation at line 566 (`scheduleDateUtc.Date.AddDays(-(int)scheduleDateUtc.DayOfWeek)`) would need adjustment to anchor on Monday instead. Both `GetProratedLimit` and `GetUsedAllowanceForPeriod` use the same convention, so the system is internally consistent.
2. **No rounding** is applied to the prorated limit — it uses raw decimal division. This is fine for internal calculations but the displayed balance may show many decimal places depending on the UI formatting.
