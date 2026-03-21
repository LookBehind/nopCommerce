# nopCommerce UI Testing Memory

## Application Details
- **Base URL:** http://localhost:4000
- **Admin Panel:** http://localhost:4000/Admin
- **Test Credentials:** ed.isajanyan@gmail.com / 123456789

## Key UI Patterns

### Authentication
- Login page at `/login` with two sections:
  - "For Companies": Google authentication
  - "For Staff": Email/password form
- After login, header changes from "Log in" to "My account" and "Log out"

### Balance Widget (HeaderLinksBefore zone)
- **Location:** Top header navigation, first item in the list before "My Account"
- **Visibility:** Only appears when customer has company allowance (Model.HasBalance = true)
- **Visual Elements:**
  - Circular dual-ring SVG progress indicator
  - Gray outer ring (total allowance background)
  - Green ring segment (recommended spending)
  - Blue indicator (used balance) - currently 0
  - Center display: Abbreviated balance (e.g., "72.3k ֏")
  - Label below: "Remaining"
- **Tooltip on Hover:**
  - USED: 0,00 ֏ (blue dot)
  - RECOMMENDED: 25 804,00 ֏ (green dot)
  - REMAINING: 72 250,00 ֏ (gray dot)
- **Persistence:** Widget appears consistently across all pages when logged in

## Test Accounts
- **ed.isajanyan@gmail.com:** Has company allowance with balance widget

## Known Working Features
- Balance widget renders correctly in HeaderLinksBefore zone
- Tooltip displays detailed balance breakdown on hover
- Widget persists across page navigation
- Currency formatting displays correctly (֏ symbol)
