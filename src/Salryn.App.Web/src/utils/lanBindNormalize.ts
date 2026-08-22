/**
 * HOST-LAN-CONTROL-M4: normalize host LAN bind input to bare IPv4.
 * Mirrors backend LocalApiLanBindNormalizer intent for client-side form validation.
 */

export type LanBindNormalizeResult = {
  ok: boolean;
  ip: string | null;
  wasRepaired: boolean;
  error: string | null;
  raw: string;
};

const FORBIDDEN = new Set(["0.0.0.0", "*", "::", "[::]", "+"]);

function isLoopback(ip: string): boolean {
  return ip === "127.0.0.1" || ip.startsWith("127.");
}

function isIpv4(value: string): boolean {
  const parts = value.split(".");
  if (parts.length !== 4) return false;
  return parts.every((part) => {
    if (!/^\d{1,3}$/.test(part)) return false;
    const n = Number(part);
    return n >= 0 && n <= 255;
  });
}

function tryExtractIpv4(input: string): string | null {
  const trimmed = input.trim().replace(/\/+$/, "");
  try {
    const absolute = trimmed.includes("://") ? new URL(trimmed) : new URL(`http://${trimmed}`);
    if (isIpv4(absolute.hostname)) {
      return absolute.hostname;
    }
  } catch {
    // fall through
  }

  const token = trimmed.split(/[/?#\s]/).find(Boolean) ?? "";
  const hostPart = token.includes(":") ? token.split(":")[0] : token;
  return isIpv4(hostPart) ? hostPart : null;
}

export function normalizeConfiguredLanBindInput(input: string | null | undefined): LanBindNormalizeResult {
  const raw = (input ?? "").trim();
  if (!raw) {
    return { ok: false, ip: null, wasRepaired: false, error: "An explicit LAN IPv4 address is required.", raw };
  }

  if (FORBIDDEN.has(raw) || FORBIDDEN.has(raw.toLowerCase())) {
    return {
      ok: false,
      ip: null,
      wasRepaired: false,
      error: "Wildcard bind addresses are forbidden. Use an explicit LAN IPv4 address.",
      raw,
    };
  }

  let candidate = raw;
  let wasRepaired = false;

  if (raw.includes("://") || raw.includes("/") || (raw.includes(":") && raw.includes("."))) {
    const extracted = tryExtractIpv4(raw);
    if (extracted) {
      wasRepaired = extracted !== raw;
      candidate = extracted;
    }
  }

  if (!isIpv4(candidate)) {
    return {
      ok: false,
      ip: null,
      wasRepaired: false,
      error: "LAN bind must be a bare IPv4 address (example: 192.168.1.161).",
      raw,
    };
  }

  if (isLoopback(candidate)) {
    return {
      ok: false,
      ip: null,
      wasRepaired: false,
      error: "LAN bind must be a non-loopback IPv4 address on this host.",
      raw,
    };
  }

  if (candidate !== raw) {
    wasRepaired = true;
  }

  return { ok: true, ip: candidate, wasRepaired, error: null, raw };
}

export function looksLikeBareIpv4(value: string | null | undefined): boolean {
  const trimmed = (value ?? "").trim();
  return isIpv4(trimmed) && !trimmed.includes(":");
}
