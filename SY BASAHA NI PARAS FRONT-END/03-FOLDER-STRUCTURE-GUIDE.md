# 03 - Folder Structure Guide

## src/Salryn.Core

Core means pure business logic.

Diri ibutang ang rules.

Examples:

    Money
    Quantity
    Barcode
    Sale
    Product
    CashierSession
    Checkout draft builder
    Product picker boundary

Core should not know WinForms.
Core should not know SQLite.
Core should not know UI buttons.

## src/Salryn.Infrastructure

Infrastructure means database and real implementation.

Diri makita:

    SQLite connection
    Database migrations
    Seed data
    Repository implementation
    DB initialization
    Local paths

Infrastructure talks to SQLite.

UI should not directly talk to Infrastructure unless through controlled services.

## src/Salryn.App.WinForms

This is the Windows frontend app.

Diri ang:

    Program.cs
    Forms
    Panels
    Buttons
    Labels
    TextBoxes
    DataGridViews
    Checkout UI shell
    Presenter
    ViewModels

Frontend developer mostly works here.

## tools/Salryn.Tools.InfrastructureSmoke

This is the smoke-test console tool.

Purpose:

- Check database initialization.
- Check repositories.
- Check services.
- Check checkout engine.
- Check presenter/viewmodel.
- Catch mistakes before commit.

## 00-HANDOFF

This stores phase audit documents.

Do not delete these.

## SY BASAHA NI PARAS FRONT-END

This current folder.

Purpose:

- Human-readable frontend handoff.
- Explain what exists.
- Explain how code works.
- Explain rules.
- Explain frontend task boundaries.
