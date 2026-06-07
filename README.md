# Salryn

Salryn is a native offline-first Windows POS project for small retail and general merchandise stores.

## Current Status

Planning Pack: Complete  
Phase 1: C# Solution Skeleton Complete  
Phase 2: Core Value Objects Complete  
Phase 3: Core Models Complete  
Phase 4: Result/Error Pattern Polish Complete  

## Stack

- C#
- .NET 10
- WinForms
- SQLite later
- Microsoft.Data.Sqlite later
- ESC/POS printer later
- Keyboard-wedge barcode scanner
- Layered desktop architecture

## Project Rules

- Engine first, UI second.
- No business logic in WinForms.
- Money uses integer cents.
- Quantity uses x1000 scaled integer.
- Sales are saved before printing.
- Receipt snapshots are preserved.
- Inventory changes use stock movements.
- V1 is void-only; refunds are reserved for V2.
- No BIR-compliance claim unless proper compliance work is completed.

## Projects

- src/Salryn.App.WinForms
- src/Salryn.Core
- src/Salryn.Infrastructure
- tests/Salryn.Tests

## Current Next Phase

Phase 5: SQLite Infrastructure.
