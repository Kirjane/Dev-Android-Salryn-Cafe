# 11 - Glossary

UI:
Ang makita sa user. Form, buttons, labels, textbox, grid.

Front-end:
Ang side sa app nga gi-interact sa user. Sa SALRYN, WinForms ni.

Backend:
Rules, services, repositories, database logic.

Core:
Pure rules and contracts. Dili dapat kahibaw sa WinForms or SQLite.

Infrastructure:
Actual database implementation. Diri ang SQLite repositories.

Repository:
Class nga mo-read or mo-write sa database.

Presenter:
Bridge between UI and rules. Mo-receive siya og UI action, dayon mo-return og state.

ViewModel:
Data nga ready ipakita sa screen.

Example:

    TotalText = PHP 50.00
    StatusMessage = Cash payment added

Smoke check:
Quick verification para makita kung naa bay naguba.

Sale completion:
Final action nga mo-save sale, payments, and stock movement. Dili pa wired sa UI.

Stock movement:
Inventory ledger entry. Every stock change dapat naay record.

Cashier session:
Open/close session sa cashier. Sale completion later must require open session.

Barcode scanner:
Usually keyboard-style input. Mo-type siya sa textbox then Enter.

Money cents:

    100 = PHP 1.00
    1500 = PHP 15.00

Quantity x1000:

    1000 = 1
    500 = 0.5
    1250 = 1.25

Read-only:
Pwede mobasa, dili pwede mo-usab sa database.

Write:
Naay change sa database. Examples: sale save, stock movement, void sale.
