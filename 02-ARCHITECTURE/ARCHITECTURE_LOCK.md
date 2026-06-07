# Salryn POS Architecture Lock

Status:
LOCKED FOR PLANNING.
NO APP CODE YET.

Project:
Salryn POS

Product Type:
Native offline-first Windows POS for general merchandise and small retail stores.

Final Stack:
- Language: C#
- Runtime: .NET 10 LTS
- UI: WinForms
- Database: SQLite
- Database Access: Microsoft.Data.Sqlite
- Printer: ESC/POS, LAN-first
- Barcode Scanner: Keyboard-wedge mode
- Reports: PDF export and CSV export
- Architecture: Layered desktop application

---

# 1. Final Architecture Principle

Salryn must be coded as a local transaction system.

It is not:
- A simple database editor
- A web dashboard
- A receipt-printing toy
- A random WinForms spaghetti app

It is:
- A native Windows POS
- Offline-first
- Transaction-safe
- Inventory-ledger based
- Hardware-aware
- Client-customizable
- Report-ready

---

# 2. Layered Architecture

Final project layout when coding starts:

Salryn/
├─ Salryn.App.WinForms
│  ├─ Forms
│  ├─ Controls
│  ├─ ViewModels
│  └─ Program.cs
│
├─ Salryn.Core
│  ├─ Models
│  ├─ Enums
│  ├─ ValueObjects
│  ├─ Services
│  └─ BusinessRules
│
├─ Salryn.Infrastructure
│  ├─ Database
│  ├─ Repositories
│  ├─ Migrations
│  ├─ Printing
│  ├─ Reports
│  ├─ Backup
│  └─ Devices
│
├─ Salryn.Tests
│
└─ Salryn.Tools
   ├─ ImportCsv
   ├─ ExportCsv
   └─ DbRepair

---

# 3. Layer Responsibilities

## 3.1 WinForms App Layer

Allowed:
- Display screens
- Capture keyboard input
- Handle button clicks
- Call service methods
- Show results/errors
- Refresh UI grids
- Open modals

Not allowed:
- Direct SQL
- Direct stock computation
- Direct sale saving logic
- Direct receipt-number generation
- Direct audit handling
- Direct backup restore logic

Rule:
UI does not own truth.

---

## 3.2 Core Layer

Owns:
- Business rules
- Models
- Value objects
- Service interfaces
- Validation
- Transaction flow rules

Examples:
- Product
- Cart
- CartLine
- Sale
- SaleItem
- Payment
- StockMovement
- Money
- Quantity

---

## 3.3 Infrastructure Layer

Owns:
- SQLite connection
- SQL repositories
- Database migrations
- ESC/POS printer implementation
- PDF report generation
- Backup and restore
- File system paths
- Device adapters

---

# 4. Main Services

## AuthService

Purpose:
Controls login and role permission checking.

Methods:
- LoginAsync(username, password)
- Logout()
- HasPermission(userId, permissionKey)
- ChangePasswordAsync(userId, oldPassword, newPassword)

---

## ProductService

Purpose:
Controls product creation, edit, search, and barcode lookup.

Methods:
- FindByBarcodeAsync(barcode)
- SearchAsync(keyword)
- QuickAddProductAsync(input, userId)
- UpdateProductAsync(input, userId)
- DeactivateProductAsync(productId, userId)
- GetProductDetailsAsync(productId)

---

## CartService

Purpose:
Controls in-memory cashier cart behavior.

Methods:
- AddProduct(cart, product, quantity)
- ChangeQuantity(cart, productId, quantity)
- RemoveProduct(cart, productId)
- ApplyDiscount(cart, productId, discount)
- ClearCart(cart)
- CalculateTotals(cart)

---

## SaleService

Purpose:
Controls checkout, sale saving, voids, and sale history.

Methods:
- CompleteSaleAsync(cart, payment, cashierUserId)
- VoidSaleAsync(saleId, reason, userId)
- GetSaleDetailsAsync(saleId)
- SearchSalesAsync(filter)

---

## InventoryService

Purpose:
Controls all stock movement.

Methods:
- StockInAsync(productId, quantity, costPrice, reason, userId)
- AdjustStockAsync(productId, newQuantity, reason, userId)
- GetStockMovementsAsync(productId)
- GetLowStockAsync()
- GetInventoryValueAsync()

---

## ReceiptService

Purpose:
Controls receipt text generation and printing.

Methods:
- BuildReceiptTextAsync(saleId)
- PrintReceiptAsync(saleId)
- ReprintReceiptAsync(saleId, userId)
- TestPrintAsync()

---

## ReportService

Purpose:
Controls daily, monthly, PDF, and CSV reports.

Methods:
- BuildDailyReportAsync(date)
- BuildMonthlyReportAsync(year, month)
- ExportMonthlyPdfAsync(year, month, outputPath)
- ExportSalesCsvAsync(start, end, outputPath)

---

## BackupService

Purpose:
Controls database backup and restore.

Methods:
- CreateBackupAsync()
- VerifyBackupAsync(backupPath)
- RestoreBackupAsync(backupPath, adminUserId)

---

## AuditService

Purpose:
Controls trace logs for important actions.

Methods:
- LogAsync(input)
- SearchAuditLogsAsync(filter)

---

# 5. Core Engineering Rules

1. UI calls services only.
2. Services contain business logic.
3. Repositories contain SQL only.
4. Every critical operation uses a database transaction.
5. Sales are saved before printing.
6. Stock changes always create stock movement records.
7. Completed sales are never edited directly.
8. Sales are voided, not deleted.
9. Products used in sales are deactivated, not deleted.
10. Receipts are saved as snapshots.
11. Money is stored as integer cents.
12. Quantity is stored as scaled integer.
13. Reports read from completed sales unless void/refund report is requested.
14. Backup and restore require admin permission.
15. Printer failure must not erase a sale.

---

# 6. Runtime Data Paths

Recommended production path:

C:\ProgramData\Salryn\
├─ data\
│  └─ salryn.db
├─ backups\
├─ exports\
├─ logs\
├─ receipts\
└─ config\

Development path can stay inside the project workspace until installer stage.

---

# 7. Deployment Model

V1:
- Single Windows PC
- Local SQLite database
- One cashier terminal
- One receipt printer
- One barcode scanner
- Local backups

Avoid in V1:
- Multi-terminal sync
- Cloud-first database
- Branch sync
- Online accounts

---

# 8. Future Upgrade Path

V2:
- Multiple payment methods
- Suppliers
- Purchase orders
- Expiry tracking
- Stock count mode
- Barcode label printing

V3:
- Multi-terminal mode
- Local server database
- Cloud backup
- Client licensing
- Branch support

V4:
- BIR-oriented compliance workflow
- Official invoice/receipt mode
- Accreditation/registration support
