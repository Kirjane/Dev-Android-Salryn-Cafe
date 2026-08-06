# Salryn POS

Salryn is an offline-ready point-of-sale and retail operations platform developed for micro, small, and medium enterprises. It centralizes sales, inventory, stock movement, receipts, reporting, user activity, and local data protection in one Windows-based system.

## Problem Addressed

Many small retail businesses still maintain sales and inventory records through notebooks, spreadsheets, or disconnected applications. This creates repetitive encoding, delayed stock information, inconsistent records, and limited visibility for day-to-day business decisions.

Salryn connects completed sales directly to inventory movement and reporting so that transaction records, stock levels, receipts, and operational activity remain organized in one system.

## Core Capabilities

- Point-of-sale checkout and sales recording
- Automatic inventory deduction for completed sales
- Product, barcode, SKU, unit, category, and brand management
- Stock-in and controlled stock-adjustment workflows
- Low-stock and out-of-stock monitoring
- Sales history and receipt preservation
- Receipt reprinting without modifying the original sale
- Daily, weekly, monthly, and product-level reporting
- Cashier, shift, and user activity tracking
- Role-based access controls
- Audit records and CSV export
- Local backup and restore
- Offline operation without continuous internet access

## Design Principles

- Transaction records are saved before receipt printing.
- Receipt snapshots remain immutable after a completed sale.
- Inventory changes are represented through traceable stock movements.
- Voids and reversals follow controlled workflows.
- Business logic is separated from the user interface.
- Monetary and quantity values use deterministic representations.
- Compliance claims are not made without the corresponding review and implementation work.

## Technology

- C# and .NET
- Local ASP.NET Core application services
- Windows desktop application shell
- Web-based user interface hosted locally
- SQLite-based local persistence
- Keyboard-wedge barcode scanner support
- Receipt printer integration
- Layered application architecture

## Current Stage

Salryn has a functional, internally validated build covering the principal retail workflows, including checkout, inventory updates, stock management, receipt handling, reporting, user controls, auditing, and backup operations.

The solution is positioned for controlled pilot implementation with selected MSMEs to validate real operating conditions, staff usability, hardware compatibility, deployment procedures, and business-specific requirements.

## Intended Users

- Small retail and general merchandise stores
- Sari-sari and neighborhood stores
- Specialty retailers
- MSMEs transitioning from manual sales and inventory records
- Businesses operating in locations with limited or unstable connectivity

## Project Structure

- `src/` — application and supporting services
- `tests/` — automated and validation-focused tests
- `docs/` — technical, operational, and release documentation
- `installer/` — packaging and deployment resources where applicable

## Product and Publisher

**Product:** Salryn POS  
**Publisher:** Kirjane Information Technology Services

Salryn is proprietary software. Access, deployment, licensing, and use are subject to the applicable written agreement with Kirjane Information Technology Services.

## Release-candidate boundaries

- The Windows Host is the supported production shape for this candidate.
- LAN Terminals are a future feature and are not included in the current production release.
- Accounts Receivable / Utang is an optional paid Host entitlement named `ReceivablesUtangLedger`; it is an operational ledger, not an accounting or tax-compliance system.
- Historical PASS reports are not fresh execution evidence. Current results and blockers are recorded in `00-HANDOFF/SALRYN_STABILIZATION_EXECUTION_LEDGER.md`.
- No merge, public release, production installer distribution, or client deployment is authorized without Kirch's approval of the exact candidate SHA.
