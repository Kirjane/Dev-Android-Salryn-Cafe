# RMO 24-2023 Electronic Journal Append Contract Plan - Salryn

Status: contract planning only.

## Purpose

J21-5F defines the future electronic journal append contract and atomic transaction boundary before any append service, checkout integration, renderer, printer, export behavior, X/Z reading, or official output work.

This document is not service behavior.

## RMO Controls

- C08 planning: electronic journal append contract shape
- C07 planning: official transaction integrity boundary
- C12 planning: preservation of future journal evidence boundary

## Contract Ownership

The future append contract must carry:

- machine profile id
- sale id
- official document sequence id
- official document number
- document kind
- transaction timestamp
- cashier/user id
- terminal id
- optional snapshot payload/hash evidence
- ORIGINAL event identity
- deferred compliance notes

## Atomic Boundary

A later implementation must keep these steps in single explicit transaction:

1. official sequence allocation
2. snapshot payload/hash persistence after renderer approval
3. journal anchor insert
4. ORIGINAL event insert
5. sale official snapshot link only if separately approved

If these steps are split across unrelated transactions, official numbers, snapshots, and journal evidence can drift.

## Snapshot Boundary

Snapshot evidence remains optional in the J21-5F contract because renderer and official snapshot persistence do not exist yet.

The future append service must require snapshot evidence only after renderer and persistence rules are approved.

## Not Added

- No append service.
- No checkout wiring.
- No database migration.
- No repository.
- No receipt_snapshot writes.
- No official document persistence from checkout.
- No renderer.
- No printer.
- No sequence allocation behavior change.
- No tax calculator engine.
- No X/Z reading.
- No UI.
- No official receipt/invoice output claim.

## Guardrail

J21-5F may define contracts and the atomic boundary only.

Do not add SQLite access, migration changes, service behavior, checkout integration, renderer integration, print integration, export behavior, X/Z reports, UI, or official output until explicitly confirmed.

