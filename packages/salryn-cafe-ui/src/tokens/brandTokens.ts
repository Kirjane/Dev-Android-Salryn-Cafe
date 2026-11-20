import brandColorsJson from "@salryn-brand/colors.json";

export const brandColors = {
  espresso: brandColorsJson.espresso,
  cream: brandColorsJson.cream,
  copper: brandColorsJson.copper,
  darkCopper: brandColorsJson.darkCopper,
  latte: brandColorsJson.latte,
  mocha: brandColorsJson.mocha,
} as const;

export const surfaceColors = {
  background: brandColors.cream,
  surface: "#FFFBF5",
  surfaceSubtle: "#F7EBDA",
  border: brandColors.latte,
  borderStrong: "#CBB090",
} as const;

export const semanticColors = {
  success: {
    fg: "#1B5E3A",
    bg: "#E6F4EC",
    border: "#9FD3B5",
  },
  warning: {
    fg: "#7A4100",
    bg: "#FEF3DC",
    border: "#E6C17A",
  },
  danger: {
    fg: "#9B1C1C",
    bg: "#FDECEC",
    border: "#E8A4A4",
  },
  info: {
    fg: "#1D4E7A",
    bg: "#E8F2FA",
    border: "#9EC2DF",
  },
  neutral: {
    fg: brandColors.espresso,
    bg: brandColors.latte,
    border: "#CBB090",
  },
  copper: {
    fg: brandColors.darkCopper,
    bg: "#FBEBDD",
    border: "#E0AE83",
  },
} as const;

export const touchTargets = {
  minPx: 44,
  posActionPx: 48,
} as const;

export const breakpoints = {
  mobileMax: 639,
  tabletMin: 640,
  posCompactMin: 1024,
  desktopMin: 1280,
} as const;

function parseHexChannel(hex: string, offset: number): number {
  return parseInt(hex.slice(offset, offset + 2), 16) / 255;
}

function toLinearChannel(channel: number): number {
  return channel <= 0.03928
    ? channel / 12.92
    : Math.pow((channel + 0.055) / 1.055, 2.4);
}

export function getRelativeLuminance(hex: string): number {
  const normalized = hex.replace(/^#/, "").trim();
  if (normalized.length !== 6) {
    throw new Error(`Expected 6-digit hex color, received "${hex}"`);
  }
  const r = toLinearChannel(parseHexChannel(normalized, 0));
  const g = toLinearChannel(parseHexChannel(normalized, 2));
  const b = toLinearChannel(parseHexChannel(normalized, 4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function getContrastRatio(foregroundHex: string, backgroundHex: string): number {
  const lumA = getRelativeLuminance(foregroundHex);
  const lumB = getRelativeLuminance(backgroundHex);
  const lighter = Math.max(lumA, lumB);
  const darker = Math.min(lumA, lumB);
  return Number(((lighter + 0.05) / (darker + 0.05)).toFixed(2));
}

export function passesWcagAA(
  foregroundHex: string,
  backgroundHex: string,
  isLargeText = false,
): boolean {
  const threshold = isLargeText ? 3.0 : 4.5;
  return getContrastRatio(foregroundHex, backgroundHex) >= threshold;
}
