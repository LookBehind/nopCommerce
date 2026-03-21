# TC3: Void Allowance API and Its Effect on Orders

## Objective
Verify the `/voidallowance` API endpoint correctly prevents further orders on voided dates, persists voided dates in customer attributes, and returns proper error responses.

## Preconditions
- A Customer exists and is linked to a Company with an active allowance.
- The API caller has `CompanyAllowanceVoiding` and `ExternalOrdersCreation` permissions.
- The customer has remaining allowance for the target date.

## Steps & Expected Results

### Scenario 3A: Successfully void an allowance for a specific date
- **Action:** `POST /api/integration/servicetitan/voidallowance?customerEmail=test@example.com&dateUtc=2026-02-10T00:00:00Z`
- **Expected:** HTTP 200 with `{"Voided": true}`.
- **Verify:** Customer's `VoidedAllowancesSettings` generic attribute contains the voided date in its JSON list.

### Scenario 3B: Get remaining allowance after voiding
- **Setup:** Customer has had their allowance voided for 2026-02-10.
- **Action:** `GET /api/integration/servicetitan/getremainingallowance?customerEmail=test@example.com` (for the voided date).
- **Expected:** The endpoint returns a response indicating ineligibility (402 Payment Required) for the voided date, since the allowance was voided.

### Scenario 3C: Voiding does not affect other dates
- **Setup:** Customer has allowance voided for 2026-02-10 but NOT for 2026-02-11.
- **Action:** `GET /getremainingallowance` for 2026-02-11.
- **Expected:** Normal remaining allowance is returned for the non-voided date.

### Scenario 3D: Void allowance for unknown customer (404)
- **Action:** `POST /voidallowance?customerEmail=nonexistent@example.com&dateUtc=2026-02-10T00:00:00Z`
- **Expected:** HTTP 404 Not Found.

### Scenario 3E: Void allowance without proper permissions (401)
- **Setup:** API caller does NOT have `CompanyAllowanceVoiding` permission.
- **Action:** `POST /voidallowance?customerEmail=test@example.com&dateUtc=2026-02-10T00:00:00Z`
- **Expected:** HTTP 401 Unauthorized.

### Scenario 3F: Void allowance with missing/invalid parameters (400)
- **Action:** `POST /voidallowance?customerEmail=&dateUtc=` (empty parameters).
- **Expected:** HTTP 400 Bad Request with appropriate error message.

### Scenario 3G: Voided dates persist correctly in generic attributes
- **Action:** Void multiple dates for the same customer (e.g., Feb 10, Feb 12, Feb 15).
- **Expected:** The `VoidedAllowancesSettings` JSON attribute contains all three dates. No duplicates if the same date is voided twice.

## Status
- [ ] Not started
