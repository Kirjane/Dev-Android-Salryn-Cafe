import appIconUrl from "@salryn-brand/salryn-app-icon.png";
import bannerUrl from "@salryn-brand/salryn-banner.png";
import transparentLogoUrl from "@salryn-brand/salryn-transparent-logo.png";

export type BrandAssetVariant = "logo" | "icon" | "banner";

export type BrandMarkProps = {
  variant?: BrandAssetVariant;
  alt?: string;
  decorative?: boolean;
  height?: number;
  className?: string;
};

const ASSET_MAP: Record<BrandAssetVariant, { src: string; defaultAlt: string; defaultHeight: number }> = {
  logo: {
    src: transparentLogoUrl,
    defaultAlt: "Salryn Cafe logo",
    defaultHeight: 40,
  },
  icon: {
    src: appIconUrl,
    defaultAlt: "Salryn Cafe app icon",
    defaultHeight: 40,
  },
  banner: {
    src: bannerUrl,
    defaultAlt: "Salryn Cafe banner",
    defaultHeight: 120,
  },
};

/**
 * Renders canonical Salryn Cafe brand artwork from assets/brand with preserved aspect ratio.
 * Transparent logo is framed on a light Cream surface per assets/brand/PALETTE.md.
 */
export function BrandMark({
  variant = "logo",
  alt,
  decorative = false,
  height,
  className = "",
}: BrandMarkProps) {
  const asset = ASSET_MAP[variant];
  const resolvedHeight = height ?? asset.defaultHeight;
  const isLightFramed = variant === "logo";

  return (
    <span
      className={`salryn-brand-frame ${isLightFramed ? "salryn-brand-frame--light" : ""} ${className}`.trim()}
      style={{ height: `${resolvedHeight}px` }}
      data-brand-variant={variant}
    >
      <img
        src={asset.src}
        alt={decorative ? "" : (alt ?? asset.defaultAlt)}
        aria-hidden={decorative ? true : undefined}
        className="salryn-brand-img"
        style={{ height: "100%", width: "auto", objectFit: "contain" }}
      />
    </span>
  );
}
