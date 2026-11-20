import { describe, expect, it } from "vitest";
import canonicalColors from "@salryn-brand/colors.json";
import {
  brandColors,
  getContrastRatio,
  passesWcagAA,
  semanticColors,
  surfaceColors,
  touchTargets,
} from "../tokens/brandTokens";

describe("Salryn Cafe brand tokens & WCAG 2.1 AA contrast", () => {
  it("matches exact canonical hex values from assets/brand/colors.json", () => {
    expect(brandColors).toEqual({
      espresso: canonicalColors.espresso,
      cream: canonicalColors.cream,
      copper: canonicalColors.copper,
      darkCopper: canonicalColors.darkCopper,
      latte: canonicalColors.latte,
      mocha: canonicalColors.mocha,
    });
    expect(brandColors.espresso.toUpperCase()).toBe("#3B1204");
    expect(brandColors.cream.toUpperCase()).toBe("#FFF4E1");
    expect(brandColors.copper.toUpperCase()).toBe("#CC7436");
    expect(brandColors.darkCopper.toUpperCase()).toBe("#934719");
    expect(brandColors.latte.toUpperCase()).toBe("#E9D5BB");
    expect(brandColors.mocha.toUpperCase()).toBe("#735447");
  });

  it("satisfies WCAG AA (>= 4.5:1) for all primary text and button combinations", () => {
    expect(passesWcagAA(brandColors.espresso, brandColors.cream)).toBe(true);
    expect(passesWcagAA(brandColors.mocha, brandColors.cream)).toBe(true);
    expect(passesWcagAA(brandColors.cream, brandColors.espresso)).toBe(true);
    expect(passesWcagAA(brandColors.cream, brandColors.darkCopper)).toBe(true);
    expect(passesWcagAA(brandColors.darkCopper, brandColors.cream)).toBe(true);
    expect(passesWcagAA(brandColors.espresso, surfaceColors.surface)).toBe(true);
    expect(passesWcagAA(brandColors.espresso, brandColors.latte)).toBe(true);

    // Copper on Cream is reserved for decorative/large display text per PALETTE.md (>= 3.0:1 large)
    expect(getContrastRatio(brandColors.copper, brandColors.cream)).toBeGreaterThanOrEqual(3.0);
  });

  it("satisfies WCAG AA (>= 4.5:1) for all semantic feedback status pairs", () => {
    for (const [tone, pair] of Object.entries(semanticColors)) {
      const ratio = getContrastRatio(pair.fg, pair.bg);
      expect(ratio, `Semantic tone "${tone}" (${pair.fg} on ${pair.bg})`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("enforces >= 44px minimum touch targets", () => {
    expect(touchTargets.minPx).toBeGreaterThanOrEqual(44);
    expect(touchTargets.posActionPx).toBeGreaterThanOrEqual(48);
  });
});
