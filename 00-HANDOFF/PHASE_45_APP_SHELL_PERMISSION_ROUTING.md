# Phase 45 - App Shell and Permission-Based Mode Routing

## Goal

Implement the first SALRYN application shell so the app no longer treats the cashier checkout screen as the whole system.

This phase introduces permission-based module routing while keeping the existing backend core untouched unless required.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Current Status

Phase 45 is being implemented on top of the existing Phase 44 checkout UI work-in-progress.

A safety branch pointer exists:

PHASE44-WIP-SAFETY-COPY

## Backend Rule

The existing backend is treated as stable legacy core.

This phase does not rewrite:

- checkout domain rules
- sale completion logic
- stock movement writes
- cashier session enforcement
- SQLite migration schema
- product repositories
- inventory write repository

## New Core Additions

### Permission Constants

File:

src/Salryn.Core/Security/SalrynPermissions.cs

Purpose:

Defines stable permission keys such as Sales.Create, Products.View, Inventory.Adjust, Reports.View, Settings.Manage, and Users.Manage.

These constants prevent routing logic from using random hardcoded permission strings.

## New Read Model

File:

src/Salryn.Core/ReadModels/SecurityReadModels.cs

Adds:

UserPermissionReadModel

Purpose:

Represents a user profile together with role name, status, and permission list.

## New Repository Contract

File:

src/Salryn.Core/Repositories/ReadRepositoryInterfaces.cs

Adds:

IUserPermissionReadRepository

Purpose:

Reads role permissions for a user without authentication logic and without writes.

## New Infrastructure Repository

File:

src/Salryn.Infrastructure/Data/Repositories/System/SqliteUserPermissionReadRepository.cs

Purpose:

Reads the user, role, and role permissions from SQLite.

This repository is read-only.

## Repository Factory Update

File:

src/Salryn.Infrastructure/Data/Repositories/SqliteRepositoryFactory.cs

Adds:

CreateUserPermissionReadRepository()

Purpose:

Allows app composition to create the new permission reader without leaking SQLite details into WinForms.

## App Composition Update

File:

src/Salryn.App.WinForms/Composition/SalrynAppServices.cs

Adds exposed services:

- UserReadRepository
- UserPermissionReadRepository

Purpose:

Allows the WinForms app shell to load a current app session and route modules by permissions.

## New App Session Layer

Folder:

src/Salryn.App.WinForms/Features/Shell/

Files:

- CurrentAppSession.cs
- AppSessionBootstrapper.cs

Purpose:

Creates the first app-level user session concept.

Important:

This is not the cashier session.

The app session answers:

- who is using the app
- what role they have
- what permissions they have
- what modules they can access

Temporary limitation:

Until a real login screen exists, AppSessionBootstrapper loads the first active user from the database.

## New Shell Routing Layer

File:

src/Salryn.App.WinForms/Features/Shell/ShellPresenter.cs

Purpose:

Builds the list of available modules from permissions.

Current modules:

- POS Checkout
- Products
- Inventory
- Sales History
- Reports
- Settings
- Users
- Backup
- Audit

Routing rule:

Modules are enabled based on permissions, not just role names.

## New Shell Form

File:

src/Salryn.App.WinForms/Features/Shell/SalrynShellForm.cs

Purpose:

Provides the main SALRYN shell UI with a left-side module list and workspace area.

Current behavior:

- POS Checkout opens the existing checkout form.
- Other modules show permission-routed placeholders.
- Locked modules show the reason they are locked.

## Important Limitations

This phase does not yet implement:

- real login screen
- password verification
- user switching
- admin dashboard workflows
- product management screen
- inventory adjustment screen
- reports screen
- settings screen
- backup workflow
- audit viewer
- sale completion unlock

## Comment Style

All new explanatory implementation comments should use:

// Kirch: ...

Do not use:

// Dev note: ...

## Current Architecture Direction

The correct future flow is:

Start App
  -> Initialize Database
  -> Load or login current user
  -> Read user permissions
  -> Create CurrentAppSession
  -> Open SalrynShellForm
  -> Enable modules by permission
  -> Open selected module

## Next Step

Switch Program.cs from direct checkout startup to shell startup.

Current behavior before final switch:

App starts directly into CheckoutShellForm

Target behavior after final switch:

App starts into SalrynShellForm
POS Checkout opens from inside the shell
