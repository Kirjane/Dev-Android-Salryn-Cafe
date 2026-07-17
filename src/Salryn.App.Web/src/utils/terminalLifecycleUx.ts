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
      // Operator-facing label; internal status enum remains Revoked.
      return "Removed";
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
      return "This terminal was removed. The client must request access again to open a new pending request.";
    case "Disabled":
      return "This terminal is temporarily blocked. Enable terminal restores access without re-pairing, or Remove terminal ends trust permanently.";
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

/** Permanent trust removal (API: revoke). Visible only after the terminal is Disabled. */
export function canRemoveTerminal(status: TerminalLifecycleStatus, role: string): boolean {
  return status === "Disabled" && isManagedRemoteLanTerminal(role);
}

/** @deprecated Prefer canRemoveTerminal — kept for existing source-guard names. */
export function canRevokeTerminal(status: TerminalLifecycleStatus, role: string): boolean {
  return canRemoveTerminal(status, role);
}

export function isRevokedTerminalReadOnly(status: TerminalLifecycleStatus): boolean {
  return status === "Revoked";
}

export function isHostLocalTerminalRole(role: string): boolean {
  return role === "HostConsole" || role === "AllInOneLocalTerminal" || role === "AdminClient";
}

/** Enable a Disabled terminal (not Revoked) without re-pairing. */
export function canEnableDisabledTerminal(status: TerminalLifecycleStatus, role: string): boolean {
  return status === "Disabled" && isManagedRemoteLanTerminal(role);
}

/** Alias used by older guards; same as canEnableDisabledTerminal. */
export function canRestoreDisabledTerminal(status: TerminalLifecycleStatus, role: string): boolean {
  return canEnableDisabledTerminal(status, role);
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
      return "Retired / removed terminals";
    case "host-local":
      return "Local host / admin clients";
    default:
      return "Terminals";
  }
}
