"use client";

/**
 * The product gallery: one large image plus selectable thumbnails.
 *
 * A small client island on an otherwise server-rendered page. Thumbnails are real buttons with
 * pressed state, arrow keys move between images, and a horizontal swipe on touch devices does
 * the same. No carousel library and no lightbox: image sets here are small (usually two), and a
 * dialog would add focus-management cost for little benefit.
 */

import { useRef, useState } from "react";

import { ProductPhoto } from "@/components/products/ProductPhoto";
import { cx } from "@/lib/format";
import type { ProductImage } from "@/types/product";

export function ProductGallery({ images, productName }: { images: ProductImage[]; productName: string }) {
  const ordered = [...images].sort((a, b) => a.sortOrder - b.sortOrder);
  const [selectedId, setSelectedId] = useState<string | null>(ordered.find((image) => image.isPrimary)?.id ?? null);
  const thumbRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const touchStartX = useRef<number | null>(null);

  const selected = ordered.find((image) => image.id === selectedId) ?? ordered[0] ?? null;

  if (!selected) {
    return <ProductPhoto src={null} alt={productName} width={960} height={960} eager style={{ borderRadius: "var(--radius)" }} />;
  }

  const select = (id: string, focus = false) => {
    setSelectedId(id);

    if (focus) {
      const index = ordered.findIndex((image) => image.id === id);
      thumbRefs.current[index]?.focus();
    }
  };

  const step = (direction: 1 | -1, focus: boolean) => {
    const index = ordered.findIndex((image) => image.id === selected.id);
    const next = ordered[(index + direction + ordered.length) % ordered.length];
    select(next.id, focus);
  };

  return (
    <div className="mp-stack-sm">
      <div
        onTouchStart={(event) => {
          touchStartX.current = event.touches[0]?.clientX ?? null;
        }}
        onTouchEnd={(event) => {
          if (touchStartX.current === null) {
            return;
          }

          const deltaX = (event.changedTouches[0]?.clientX ?? touchStartX.current) - touchStartX.current;
          touchStartX.current = null;

          // A deliberate horizontal swipe changes the image; anything smaller, or mostly
          // vertical scrolling, is left alone.
          if (Math.abs(deltaX) > 40) {
            step(deltaX < 0 ? 1 : -1, false);
          }
        }}
      >
        <ProductPhoto
          src={selected.url}
          alt={selected.altText ?? productName}
          width={960}
          height={960}
          eager
          fetchPriority="high"
          style={{ borderRadius: "var(--radius)", backgroundColor: "var(--bg-subtle)" }}
        />
      </div>

      {ordered.length > 1 ? (
        <div
          className="d-flex"
          role="group"
          aria-label="Product images"
          style={{ gap: "var(--space-2)" }}
          onKeyDown={(event) => {
            if (event.key === "ArrowRight") {
              event.preventDefault();
              step(1, true);
            } else if (event.key === "ArrowLeft") {
              event.preventDefault();
              step(-1, true);
            }
          }}
        >
          {ordered.map((image, index) => {
            const active = image.id === selected.id;

            return (
              <button
                key={image.id}
                ref={(element) => {
                  thumbRefs.current[index] = element;
                }}
                type="button"
                onClick={() => select(image.id)}
                aria-pressed={active}
                aria-label={`View image ${index + 1} of ${ordered.length}: ${image.altText ?? productName}`}
                className={cx("mp-gallery-thumb", active && "is-active")}
              >
                <ProductPhoto
                  src={image.url}
                  alt=""
                  width={192}
                  height={192}
                  style={{ width: "4.5rem", height: "4.5rem", borderRadius: "var(--radius-sm)" }}
                />
              </button>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}
