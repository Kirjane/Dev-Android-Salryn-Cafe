export type TerminalLifecycleStatus = "PendingPairing" | "Active" | "Disabled" | "Revoked" | string;

export function formatTerminalRoleLabel(role: string): string {
  switch (role) {
    case "CashierClient":
      return "Remote POS terminal";
    case "FutureMobileDashboardReadOnly":
      return "Remote Read-Only terminal";
    case "HostConsole":
      return "Host console";
    case "AllInOneLocalTerminal":
      return "Local POS terminal";
    case "AdminClient":
      return "Admin client";
    default:
      return role;
  }
}

export function formatTerminalStatusLabel(status: TerminalLifecycleStatus): string {
  switch (status) {
    case "Active":
      return "Active";
    case "Disabled":
      return "Disabled";
    case "Revoked":
      return "Revoked";
    case "PendingPairing":
      return "Pending pairing";
    default:
      return status;
  }
}

export function terminalStatusTone(status: TerminalLifecycleStatus): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Active":
      return "success";
    case "Disabled":
      return "warning";
    case "Revoked":
      return "danger";
    case "PendingPairing":
      return "info";
    default:
      return "info";
  }
}

export function terminalLifecycleExplanation(status: TerminalLifecycleStatus): string | null {
  switch (status) {
    case "Revoked":
      return "This device is no longer trusted. The client must request access again to open a new pending request.";
    case "Disabled":
      return "This terminal is temporarily blocked. Re-enable is not supported in this build.";
    default:
      return null;
  }
}

export function isManagedRemoteLanTerminal(role: string): boolean {
  return role === "CashierClient" || role === "FutureMobileDashboardReadOnly";
}

export function canDisableTerminal(status: TerminalLifecycleStatus, role: string): boolean {
  return status === "Active" && isManagedRemoteLanTerminal(role);
}

export function canRevokeTerminal(status: TerminalLifecycleStatus, role: string): boolean {
  return (status === "Active" || status === "Disabled") && isManagedRemoteLanTerminal(role);
}

export function isRevokedTerminalReadOnly(status: TerminalLifecycleStatus): boolean {
  return status === "Revoked";
}

export function isHostLocalTerminalRole(role: string): boolean {
  return role === "HostConsole" || role === "AllInOneLocalTerminal" || role === "AdminClient";
}

export function canRestoreDisabledTerminal(_status: TerminalLifecycleStatus, _role: string): boolean {
  // Backend has no EnableTerminalAsync in this build — never offer a fake restore.
  return false;
}

export function canClearTerminalOperatorSessions(status: TerminalLifecycleStatus, role: string): boolean {
  return isManagedRemoteLanTerminal(role) && (status === "Active" || status === "Disabled");
}

export function canHideTerminalFromList(status: TerminalLifecycleStatus, role: string): boolean {
  // Soft hide only — no hard delete/archive schema. Active terminals stay visible.
  if (isHostLocalTerminalRole(role)) {
    return status === "Revoked" || status === "Disabled";
  }
  return isManagedRemoteLanTerminal(role) && (status === "Revoked" || status === "Disabled");
}

export type TerminalConnectionGroup = "active" | "blocked" | "retired" | "host-local";

export function terminalConnectionGroup(
  status: TerminalLifecycleStatus,
  role: string,
): TerminalConnectionGroup {
  if (isHostLocalTerminalRole(role)) {
    return "host-local";
  }
  if (status === "Active" || status === "PendingPairing") {
    return "active";
  }
  if (status === "Disabled") {
    return "blocked";
  }
  return "retired";
}

export function terminalGroupTitle(group: TerminalConnectionGroup): string {
  switch (group) {
    case "active":
      return "Active terminals";
    case "blocked":
      return "Blocked terminals";
    case "retired":
      return "Retired / revoked terminals";
    case "host-local":
      return "Local host / admin clients";
    default:
      return "Terminals";
  }
}