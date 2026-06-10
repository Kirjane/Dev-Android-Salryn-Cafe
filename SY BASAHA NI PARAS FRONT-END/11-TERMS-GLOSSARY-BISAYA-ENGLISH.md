# 11 - Terms Glossary Bisaya-English

UI:
User Interface. Ang makita sa user: buttons, forms, labels, textboxes, grid.

Frontend:
Part sa app nga gi-interact sa user. Sa SALRYN, frontend means WinForms app.

Backend:
Rules, database, repositories, services.

Core:
Pure rules and models. Dili ni dapat kahibaw sa UI or SQLite.

Infrastructure:
Database implementation. Diri naa ang SQLite repositories.

Repository:
Class nga responsible mo-read/write sa database.

Presenter:
Bridge between UI and logic. It receives UI actions and returns UI state.

ViewModel:
Data nga ready ipakita sa UI.

Example:

    TotalText = PHP 50.00
    StatusMessage = Cash payment added

Smoke test:
Fast verification script. Dili full unit test framework, pero checks if major flows still work.

Sale completion:
Final action nga mo-save sale, payments, and stock movement. Not wired yet sa UI.

Stock movement:
Ledger entry for inventory.

Examples:

    Sale = stock out
    VoidSale = stock reversal
    StockIn = stock added
    Adjustment = manual adjustment

Ledger-based inventory:
Every stock change has a record. Dili lang basta edit stock number.

Cashier session:
Open/close session for cashier. Sale completion must be tied to open cashier session later.

Barcode scanner:
Usually acts like keyboard input. It types barcode into textbox and presses Enter.

x1000 quantity:

    1.000 = 1000
    2.000 = 2000
    0.500 = 500

Cents money:

    PHP 1.00 = 100
    PHP 15.00 = 1500

Read-only:
Can read data, cannot change database.

Write:
Changing database. Sale completion and stock movements are writes.
