# TC2: Balance Deduction and Payment Gating at Checkout

## Objective
Verify that the CheckMoneyOrder payment method is hidden when a customer's remaining allowance is insufficient, and that valid allowances allow payment to proceed. Also verify role-based bypass behavior.

## Preconditions
- A Company exists with a configured `AmountLimit` and `AmountLimitType`.
- A Customer is linked to that Company.
- The CheckMoneyOrder payment plugin is active.
- Shopping cart contains items with a known total.

## Steps & Expected Results

### Scenario 2A: Insufficient balance hides payment method
- **Setup:** Customer has $100 remaining allowance. Shopping cart total = $150.
- **Action:** Call `HidePaymentMethodAsync()` for this customer/cart.
- **Expected:** Method returns `true` (payment method is hidden). Customer cannot select CheckMoneyOrder at checkout.

### Scenario 2B: Sufficient balance shows payment method and processes payment
- **Setup:** Customer has $200 remaining allowance. Shopping cart total = $150.
- **Action:** Call `HidePaymentMethodAsync()`, then `ProcessPaymentAsync()`.
- **Expected:** `HidePaymentMethodAsync()` returns `false` (visible). `ProcessPaymentAsync()` succeeds with `PaymentStatus.Paid`. Remaining allowance is now $50 for the current period.

### Scenario 2C: Exact balance matches cart total
- **Setup:** Customer has $150 remaining allowance. Shopping cart total = $150.
- **Action:** Call `HidePaymentMethodAsync()`, then `ProcessPaymentAsync()`.
- **Expected:** Payment method is visible. Payment succeeds. Remaining allowance drops to $0.

### Scenario 2D: UnlimitedAccount role bypasses balance check
- **Setup:** Customer has $0 remaining allowance but is assigned the `UnlimitedAccount` role. Shopping cart total = $500.
- **Action:** Call `HidePaymentMethodAsync()`, then `ProcessPaymentAsync()`.
- **Expected:** Payment method is visible regardless of balance. Payment succeeds.

### Scenario 2E: Allowance Exempt role excludes customer from balance checks
- **Setup:** Customer is assigned the `Allowance Exempt` role.
- **Action:** Call `HidePaymentMethodAsync()`.
- **Expected:** CheckMoneyOrder payment method is hidden (customer is not eligible for company allowance). The customer is expected to use other payment methods.

### Scenario 2F: Balance deduction across multiple orders within same period
- **Setup:** Customer has $500 remaining allowance (monthly). Places an order for $200, then another for $200.
- **Action:** After first order, check remaining. After second order, check remaining.
- **Expected:** After first order: $300 remaining. After second order: $100 remaining. A third order of $150 should cause the payment method to be hidden.

## Status
- [ ] Not started
