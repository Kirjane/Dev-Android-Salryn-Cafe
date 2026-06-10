# 01 - Unsa ang SALRYN

SALRYN is a local Windows POS and inventory system.

Dili ni website. Dili ni cloud-first. Dili ni mobile app. Native Windows desktop app ni siya para sa small businesses nga need og simple pero tarong nga sales and inventory system.

Target users:

- sari-sari store
- small grocery
- boutique
- school supplies
- general merchandise
- mini mart
- small retail shop

Main goal sa V1:

- stable offline-first POS
- product management
- barcode lookup
- stock tracking
- cashier session
- sales checkout
- cash payment
- receipt records
- transaction history
- basic inventory reports later
- local backup later

Current stack:

    C#
    .NET 10
    WinForms
    SQLite
    Microsoft.Data.Sqlite

Important mindset:

    Dili tanan ibutang sa UI.

Ang UI dapat limpyo. Ang rules naa sa Core. Ang database naa sa Infrastructure. Ang form mo-display ug mo-call ra sa allowed nga boundary.
