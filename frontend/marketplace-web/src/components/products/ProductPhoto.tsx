import { Image as ImageIcon } from "lucide-react";
import type { CSSProperties } from "react";

import { cx } from "@/lib/format";

type PhotoFit = "cover" | "contain";

interface ProductPhotoProps {
  src: string | null;
  alt: string;
  width?: number;
  height?: number;
  aspectRatio?: string;
  fit?: PhotoFit;
  eager?: boolean;
  className?: string;
  style?: CSSProperties;
}

/**
 * One product image, used everywhere the same product appears.
 *
 * These URLs belong to sellers and outside hosts, so they stay plain `img` elements rather than
 * going through the Next optimizer. The component still centralizes aspect ratio, accessible
 * fallback, lazy loading, and image decoding.
 */
export function ProductPhoto({
  src,
  alt,
  width,
  height,
  aspectRatio = "1 / 1",
  fit = "cover",
  eager = false,
  className,
  style,
}: ProductPhotoProps) {
  if (!src) {
    return (
      <span
        role="img"
        aria-label={alt}
        className={cx("mp-product-photo", "mp-product-photo-empty", className)}
        style={{ aspectRatio, ...style }}
      >
        <ImageIcon aria-hidden size={28} />
      </span>
    );
  }

  return (
    // Seller-hosted URLs cannot be declared in Next's remote-image allowlist without trusting
    // arbitrary image hosts, so this stays a plain image with performance attributes.
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={src}
      alt={alt}
      width={width}
      height={height}
      loading={eager ? "eager" : "lazy"}
      decoding="async"
      className={cx("mp-product-photo", fit === "contain" && "mp-product-photo-contain", className)}
      style={{ aspectRatio, objectFit: fit, ...style }}
    />
  );
}
