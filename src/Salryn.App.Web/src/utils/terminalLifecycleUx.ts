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