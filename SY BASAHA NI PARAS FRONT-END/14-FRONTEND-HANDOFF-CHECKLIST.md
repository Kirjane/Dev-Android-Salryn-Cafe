# 14 - Frontend Handoff Checklist

Must understand:

- SALRYN is native Windows WinForms POS.
- UI must stay thin.
- Presenter owns UI state.
- Core owns business rules.
- Infrastructure owns database.
- Smoke tests protect the project.

Must open first:

    CheckoutShellForm.cs
    CheckoutPresenter.cs
    CheckoutViewModels.cs

Must not do:

- No SQLite in UI.
- No repository inside form event handlers.
- No sale completion.
- No stock movement write.
- No receipt print.
- No schema change.
- No SQL file.
- No DB file.

Safe tasks:

- UI layout.
- Styling.
- Better labels.
- Better grid formatting.
- Better section design.
- Render viewmodel later.
- Button state based on viewmodel flags later.

Ask before:

- Product search wiring.
- Cashier session UI.
- Complete sale button.
- Receipt printing.
- Inventory movement.
- Database migration.
