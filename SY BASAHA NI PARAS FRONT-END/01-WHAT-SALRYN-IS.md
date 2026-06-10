# 01 - What SALRYN Is

SALRYN is a native Windows desktop POS and inventory system.

Bisaya-English:

SALRYN kay local Windows POS app. Dili ni website. Dili ni cloud-first. Dili ni fancy dashboard una. Ang goal ani kay reliable sales and inventory system for small businesses.

Target businesses:

- Sari-sari stores
- Small groceries
- Boutiques
- Merch stores
- School supply stores
- Mini marts
- General merchandise stores

V1 goal:

- Stable offline-first Windows POS
- Product management
- Barcode product lookup
- Inventory tracking
- Stock in/out movements
- Cashier sessions
- Sales checkout
- Cash payments
- Receipt records
- Transaction history
- Basic reports later
- Local backup later
- User roles later

What SALRYN is not yet:

- Not cloud-first
- Not multi-branch yet
- Not BIR-accredited
- Not full accounting
- Not payroll
- Not web app
- Not mobile app
- Not Electron

Technical stack:

    C#
    .NET 10
    WinForms
    SQLite
    Microsoft.Data.Sqlite
    Local Windows desktop app

Architecture mindset:

    UI should be presentational.
    Core should own rules.
    Infrastructure should own database access.
