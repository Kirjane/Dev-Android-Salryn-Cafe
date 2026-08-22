# J20-5A - Checkout 768p Action Visibility Repair

Status: manual launch UI repair.

## Finding

During first manual dev launch on a 1366x768 screen, the Checkout Actions card was pushed below the visible area after Totals and Payment.

Manual retest confirmed the full guarded checkout flow works through the UI:

- product search
- add item
- cash payment
- completion dry run
- submit guard
- complete sale confirmation
- sale completed result
- start new sale reset

Remaining polish found during manual retest:

- Add Cash Payment button was visually clipped under the cash field.
- Complete Sale confirmation/result used small default MessageBox dialogs.

## Change

- Reduced checkout minimum/window size for short work areas.
- Reduced fixed root vertical height.
- Reduced workspace vertical padding.
- Made the right checkout column scrollable.
- Compacted the Totals card.
- Increased Payment card height so Add Cash Payment is fully visible.
- Moved Validate Completion, Prepare Submit Guard, and Complete Sale near the top of the Actions card.
- Replaced Complete Sale confirmation/result MessageBox calls with a larger custom modal window.
- Kept Complete Sale controlled by CheckoutViewModel.CanCompleteSale.
- Added source guard checks for 768p action visibility and custom completion dialogs.

## Boundary

- No business logic change.
- No database schema change.
- No service/repository change.
- No production database touched.
- Dev database may be reseeded before retrying manual launch.