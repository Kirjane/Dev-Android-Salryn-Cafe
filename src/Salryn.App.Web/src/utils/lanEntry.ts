export function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.trim().toLowerCase();
  return normalized === "localhost"
    || normalized === "::1"
    || normalized === "[::1]"
    || normalized === "127.0.0.1"
    || normalized.startsWith("127.");
}

export function isRemoteClientBrowserLocation(): boolean {
  if (typeof window === "undefined") {
    return false;
  }

  return !isLoopbackHostname(window.location.hostname);
}

export function currentBrowserOrigin(): string {
  if (typeof window === "undefined") {
    return "";
  }

  return window.location.origin;
}
