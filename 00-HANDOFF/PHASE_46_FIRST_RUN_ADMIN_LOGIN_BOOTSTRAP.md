# Phase 46 - First-Run Admin and Login Bootstrap

## Goal

Implement the first real authentication entry point for SALRYN.

Phase 45 created the permission-routed shell, but the shell had no active user.

Phase 46 adds first-run Admin creation and login so the shell can open with a real authenticated CurrentAppSession.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

The application now starts with authentication before opening the shell.

Startup flow:

Start App
  -> Initialize Database
  -> Check active users
  -> If no active user exists, show First-Run Admin Setup
  -> If active user exists, show Login
  -> Authenticate user
  -> Create CurrentAppSession
  -> Open SalrynShellForm
  -> Enable modules by permission

## New Core Files

src/Salryn.Core/Security/AuthenticationModels.cs

Adds authentication request and read models.

src/Salryn.Core/Security/SalrynPasswordHasher.cs

Adds salted PBKDF2-SHA256 password hashing and verification.

src/Salryn.Core/Services/AuthenticationService.cs

Adds first-run admin creation and login validation logic.

## Updated Core Contract

src/Salryn.Core/Repositories/ReadRepositoryInterfaces.cs

Adds IAuthenticationRepository.

The repository contract supports:

- checking if any active user exists
- reading login credentials by username
- creating the first Admin account

## New Infrastructure File

src/Salryn.Infrastructure/Data/Repositories/System/SqliteAuthenticationRepository.cs

Adds SQLite-backed authentication persistence.

This repository writes only during first-run Admin setup.

It does not verify passwords; password verification stays in Core authentication logic.

## Updated Infrastructure Factory

src/Salryn.Infrastructure/Data/Repositories/SqliteRepositoryFactory.cs

Adds CreateAuthenticationRepository().

## Updated App Composition

src/Salryn.App.WinForms/Composition/SalrynAppServices.cs

Adds Authentication service exposure.

## New WinForms Auth Screen

src/Salryn.App.WinForms/Features/Auth/AuthStartupForm.cs

Behavior:

- if no active user exists, shows First-Run Admin Setup
- if active user exists, shows Login
- returns CurrentAppSession after successful authentication

## Updated Startup

src/Salryn.App.WinForms/Program.cs

Program.cs now opens AuthStartupForm before SalrynShellForm.

## Security Notes

Passwords are not stored as plain text.

Passwords are stored using SALRYN PBKDF2-SHA256 V1 format with random salt.

First-run setup is blocked once an active user exists.

## Current Limitations

This phase does not yet implement:

- user management screen
- change password
- reset password
- logout
- user switching
- lockout after failed attempts
- cashier shift opening UI

## Verified Behavior

First-run Admin setup successfully opens the app shell with Admin permissions unlocked.

Build passes after implementation.

## Comment Style

Implementation comments use:

// Kirch: ...
