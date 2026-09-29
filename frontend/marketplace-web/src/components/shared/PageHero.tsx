import Link from "next/link";
import { ArrowRight } from "lucide-react";
import type { ReactNode } from "react";

export interface HeroAction {
  href: string;
  label: string;
  variant?: "primary" | "secondary";
}

interface PageHeroProps {
  id?: string;
  eyebrow?: string;
  title: string;
  description?: string;
  primaryAction: HeroAction;
  secondaryAction?: HeroAction;
  media?: ReactNode;
}

/**
 * The reusable storefront hero.
 *
 * Copy comes first and imagery second because a shopper is here for the marketplace before any
 * product. On narrow screens the media stacks below the calls to action, so text never overlaps
 * an image and buttons remain reachable without horizontal scrolling.
 */
export function PageHero({ id, eyebrow, title, description, primaryAction, secondaryAction, media }: PageHeroProps) {
  return (
    <section className="mp-hero" aria-labelledby={id}>
      <div className="mp-hero-copy">
        {eyebrow ? <p className="mp-hero-eyebrow">{eyebrow}</p> : null}
        <h1 className="mp-hero-title" id={id}>
          {title}
        </h1>
        {description ? <p className="mp-hero-description">{description}</p> : null}

        <div className="mp-hero-actions">
          <HeroButton action={primaryAction} />
          {secondaryAction ? <HeroButton action={secondaryAction} /> : null}
        </div>
      </div>

      {media ? <div className="mp-hero-media">{media}</div> : null}
    </section>
  );
}

function HeroButton({ action }: { action: HeroAction }) {
  const className = action.variant === "secondary" ? "btn btn-outline-secondary" : "btn btn-primary";

  return (
    <Link href={action.href} className={className}>
      {action.label}
      {action.variant === "secondary" ? null : <ArrowRight size={16} aria-hidden />}
    </Link>
  );
}
