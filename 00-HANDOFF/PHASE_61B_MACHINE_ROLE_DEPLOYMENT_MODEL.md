# Phase 61B - Machine Role and Deployment Mode Model

Status: Implementation checkpoint.

Purpose:
Add lightweight Core concepts for SALRYN Adaptive Store Host deployment without creating Host projects or changing frontend code.

Implemented:
- Added SalrynMachineRole
- Added SalrynDeploymentMode
- Added StoreIdentity
- Added StoreHostIdentity
- Added TerminalIdentity
- Added StoreHostEndpoint
- Added StoreDeploymentProfile
- Added StoreDeploymentRules
- Added compile check for adaptive Store Host deployment model

Architecture rule preserved:
- Core owns models and rules
- Infrastructure still owns SQLite
- WinForms UI still does not access SQLite directly
- No Host process was created
- No LAN networking was implemented
- No Product Edit UI work was resumed

Important:
This phase only defines machine role and deployment mode concepts. It does not yet change runtime behavior.

Next:
Phase 61C - Host and Client Contract Boundary Plan
