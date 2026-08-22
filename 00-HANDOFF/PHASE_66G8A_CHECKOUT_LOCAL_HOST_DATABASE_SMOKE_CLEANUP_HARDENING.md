# Phase 66G8A - Checkout Local Host Database Smoke Cleanup Hardening

Status: Database smoke cleanup hardening only.

## Scope

This phase hardens the 66G8 database-backed checkout Local Host smoke so the isolated temporary database folder is cleaned deterministically.

The smoke still verifies the same database-backed checkout lookup path from 66G8. This phase only improves release and cleanup behavior around the smoke harness.

## Added / changed

- Added Infrastructure-owned SQLite pool cleanup through SqliteConnectionFactory.ClearAllPools.
- The checkout Local Host database smoke now releases passive app session identity before cleanup.
- The checkout Local Host database smoke now stops the Local Host runtime before cleanup.
- The checkout Local Host database smoke now clears SQLite pools through Infrastructure before deleting the temporary database root.
- The cleanup path now throws if the temporary smoke folder remains after retries.
- Added a 66G8A source guard.
- CoreSmoke now checks the 66G8A cleanup hardening shape.

## Preserved boundaries

- No Shell wiring was changed.
- No CheckoutPresenter behavior was changed.
- No ProductLookup behavior was changed.
- No AppServices checkout wiring was changed.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No production database path is touched by the smoke.
- The smoke tool does not directly reference Microsoft.Data.Sqlite.
- SQLite pool cleanup remains owned by Infrastructure.

## Current state after this phase

The 66G8 database-backed checkout Local Host smoke is expected to pass without cleanup warnings and without leaving its isolated temporary database folder behind.
