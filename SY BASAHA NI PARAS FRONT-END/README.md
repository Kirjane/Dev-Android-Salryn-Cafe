# SY BASAHA NI PARAS FRONT-END

Para ni kang Paras / frontend developer.

This folder is the SALRYN frontend handoff.

SALRYN is now inside controlled UI territory. Naa nay backend engine, SQLite infrastructure, checkout draft builder, product picker boundary, checkout shell, presenter, and viewmodel.

Current sealed state:

- Phase 39: POS Checkout UI Shell
- Phase 40: POS Checkout UI Presenter/ViewModel Boundary

Current architecture:

    CheckoutShellForm
        -> CheckoutPresenter
            -> CheckoutViewModel
                -> PosCheckoutDraftTransactionBuilder

Main rule:

    UI displays state.
    Presenter controls UI state.
    Core owns business rules.
    Infrastructure owns database.

Do not put database logic inside the form.

Forbidden in UI:

    SqliteConnection
    SqliteCommand
    AddCompletedSaleAsync
    StockMovement
    CreateSaleWriteRepository

Frontend work now means:

- Make the POS UI clean.
- Improve layout.
- Improve labels.
- Improve spacing.
- Prepare cart grid.
- Prepare totals panel.
- Prepare product result section.
- Prepare payment section.
- Keep sale completion disabled.

Not allowed yet:

- No sale completion.
- No stock movement write.
- No receipt printing.
- No direct SQLite in UI.
- No schema migration.
- No SQL file.
- No database file.

Read order:

1. 00-BASAHA-FIRST.md
2. 01-WHAT-SALRYN-IS.md
3. 02-CURRENT-REPO-STATE.md
4. 03-FOLDER-STRUCTURE-GUIDE.md
5. 04-BACKEND-ARCHITECTURE-BISAYA-ENGLISH.md
6. 05-FRONTEND-ARCHITECTURE.md
7. 06-CHECKOUT-UI-FILES-EXPLAINED.md
8. 07-CODE-SYNTAX-EXPLAINED.md
9. 08-FRONTEND-RULES-NO-SPAGHETTI.md
10. 09-PHASE-41-README.md
11. 10-UI-TASKS-FOR-PARAS.md
12. 11-TERMS-GLOSSARY-BISAYA-ENGLISH.md
13. 12-DO-NOT-TOUCH-ZONES.md
14. 13-RUN-AND-VERIFY.md
15. 14-FRONTEND-HANDOFF-CHECKLIST.md
16. 15-CURRENT-CODE-MAP.md
17. 16-PARAS-QUICK-START.md
