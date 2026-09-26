# UI Design System

> Bootstrap 5 as the layout substrate, a strict token layer on top so the result reads as a designed product rather than default Bootstrap. Three dashboards, one storefront, one visual language.

## 1. Design principles

| Principle | Meaning in practice |
| --- | --- |
| **Calm, not loud** | One accent colour carries action. Chrome is neutral. No gradient soup. |
| **Data is the hero** | Dashboards optimise for scanability: strong numerals, quiet grid lines, one highlighted metric. |
| **Depth through layering, not shadow** | Surfaces separated by 1 px borders and a 2–3 step elevation ramp, never heavy drop shadows. |
| **Consistent geometry** | 12 px base radius scale, 4/8/12/16/24 spacing steps, 1.5 px borders. |
| **Colour never carries meaning alone** | Every status badge has a dot **and** a label. |
| **Dense but breathable** | Tables at 52 px row height with 16 px gutters; card padding 20–24 px. |

## 2. Colour tokens

```css
:root {
  /* brand */
  --brand-50:#f2f0ff;  --brand-100:#e6e1ff; --brand-200:#cec6ff;
  --brand-300:#aea1ff; --brand-400:#8b78ff; --brand-500:#7c5cff;
  --brand-600:#6a44f5; --brand-700:#5734d1; --brand-800:#452aa5;

  /* neutrals (light) */
  --bg-body:#f6f7fb;      --bg-surface:#ffffff;   --bg-subtle:#f1f3f9;
  --bg-elevated:#ffffff;  --border:#e4e7f0;       --border-strong:#cfd4e4;
  --text:#151a2e;         --text-muted:#5b6480;  --text-subtle:#8b93ab;

  /* sidebar */
  --sidebar-bg:#0f1222;   --sidebar-bg-2:#171b31; --sidebar-text:#a7aecb;
  --sidebar-text-active:#ffffff; --sidebar-border:#252b45;

  /* semantic */
  --success:#16a34a; --success-bg:#e8f7ee;
  --warning:#d97706; --warning-bg:#fdf2e0;
  --danger:#dc2626;  --danger-bg:#fdeaea;
  --info:#2563eb;    --info-bg:#e8efff;
  --violet:#7c5cff;  --violet-bg:#f2f0ff;
  --cyan:#0891b2;    --teal:#0d9488;  --orange:#ea580c;

  /* chart series */
  --chart-1:#7c5cff; --chart-2:#22c55e; --chart-3:#f97316;
  --chart-4:#0ea5e9; --chart-5:#ec4899; --chart-6:#eab308;

  /* elevation */
  --shadow-1:0 1px 2px rgba(21,26,46,.06), 0 1px 3px rgba(21,26,46,.04);
  --shadow-2:0 2px 6px rgba(21,26,46,.07), 0 6px 16px rgba(21,26,46,.05);
  --shadow-pop:0 10px 30px rgba(21,26,46,.14);
}
```

Dark theme re-maps the same variable names — components never branch on theme:

```css
[data-theme='dark'] {
  --bg-body:#0b0e1a;  --bg-surface:#141829; --bg-subtle:#1b2036;
  --border:#262c44;  --border-strong:#343c5c;
  --text:#eef1fa;     --text-muted:#a3abc6; --text-subtle:#7b84a1;
  --sidebar-bg:#0a0c16; --sidebar-bg-2:#12162a;
  --success-bg:#0f2a1c; --warning-bg:#33240c; --danger-bg:#3a1616;
  --info-bg:#12234a;   --violet-bg:#1c1740;
}
```

## 3. Typography

```css
--font-sans: 'Plus Jakarta Sans', system-ui, -apple-system, 'Segoe UI', sans-serif;
--font-display: 'Sora', var(--font-sans);

--fs-display: clamp(1.75rem, 1.2rem + 2vw, 2.75rem);
--fs-h1: 1.5rem;  --fs-h2: 1.25rem; --fs-h3: 1.0625rem;
--fs-body: .9375rem;  --fs-sm: .8125rem;  --fs-xs: .75rem;
--fs-metric: 2rem;  --fs-metric-lg: 2.5rem;

--lh-tight: 1.15;  --lh-normal: 1.55;
--tracking-tight: -.02em;
```

- Page titles use `--font-display` at `--fs-h1` with `--tracking-tight`.
- **Metric numbers** are the visual signature of the dashboards: `--fs-metric-lg`, weight 700, `font-variant-numeric: tabular-nums` so columns align.
- Labels above metrics are `--fs-xs`, uppercase, `letter-spacing:.06em`, `--text-subtle`.
- Body copy is 15 px — compact for a dashboard, still comfortable for storefront paragraphs.

## 4. Spacing, radius, motion

```css
--space-1:.25rem; --space-2:.5rem;  --space-3:.75rem; --space-4:1rem;
--space-5:1.5rem; --space-6:2rem;   --space-7:3rem;

--radius-sm:6px; --radius-md:10px; --radius-lg:14px; --radius-xl:20px; --radius-pill:999px;

--transition-fast:120ms cubic-bezier(.4,0,.2,1);
--transition-base:200ms cubic-bezier(.4,0,.2,1);
--transition-slow:320ms cubic-bezier(.4,0,.2,1);
```

`prefers-reduced-motion: reduce` sets every transition/animation duration to `1ms` and disables chart animation.

## 5. Shell anatomy

```text
┌──────────────────────────────────────────────────────────────────┐
│ ▓ SIDEBAR (264px)          │  TOPBAR (64px, sticky)               │
│  logo / store switcher      │  search · range picker · theme ·     │
│  ────────────────           │  notifications bell · user menu       │
│  nav sections (label +      ├─────────────────────────────────────│
│   icon + label + badge)     │  PAGE HEADER  title · actions        │
│  ────────────────           │  ─────────────────────────────────── │
│  recent orders / support    │  KPI ROW     4 × StatCard           │
│  ────────────────           │  CHART ROW   2:1 + 1:1               │
│  upgrade / status footer    │  DATA ROW    table or product grid    │
└─────────────────────────────┴──────────────────────────────────────┘
```

- Sidebar: 264 px expanded, 76 px collapsed (icon only, tooltips on hover). Dark surface in **all three dashboards** so the product areas feel unified.
- Active item: brand-tinted background, left 3 px brand bar, white text, subtle inner glow.
- Topbar: translucent with `backdrop-filter: blur(12px)`, 1 px bottom border, 64 px tall.
- Below 992 px the sidebar becomes an off-canvas drawer with a scrim; the topbar collapses to search + menu + bell.

## 6. Component specifications

### StatCard (KPI)

```text
┌──────────────────────────────────────┐
│ ◷  TOTAL ORDERS            [ ⋯ ]     │  label 12px uppercase, icon chip 36px
│                                      │
│ 3,484                    ╱╲╱╲        │  metric 40px 700 tabular
│ +1.1% vs last week                   │  delta 13px, green/red + arrow glyph
└──────────────────────────────────────┘
```

- 1 px `--border`, `--radius-lg`, `--bg-surface`, `--shadow-1`, 20 px padding.
- Optional **featured** variant: brand gradient surface, white text, `SparklineChart` in the right 40 %, used for at most **one** card per row (mirrors the admin reference where "Total Sales" is the hero).
- Delta colour is paired with an arrow icon and the words "vs last …", never colour alone.
- Icon chip: 36×36, `--radius-md`, 12 % tint background of the series colour.

### Charts

```text
Revenue trend   AreaChart  smooth monotone, 1.5 px stroke, vertical gradient
                                        fill 24% → 0%, 4 px dot on hover
Orders trend    BarChart   rounded 4 px bars, 1.5 px gap, active bar brand
Top products    RankedList thumbnail + name + horizontal bar + value
Channels        DonutChart 8 px ring gap, 4 px corner radius, center % label
Location/Cat.   HorizontalBars label column, bar, right-aligned value
```

- Shared `<ChartCard title subtitle actions>{children}</ChartCard>` wrapper: title 16 px 600, subtitle 13 px muted, optional range `Select` and chart-type `Toggle`.
- Axes: no axis lines, 12 px `--text-subtle` ticks, 4 dashed `--border` gridlines, currency ticks abbreviated (`$10K`).
- Tooltip: white/dark surface, 1 px border, `--shadow-pop`, 12 px radius, series dot + label + value.
- Recharts is dynamically imported with `ssr: false` and only inside dashboard routes.

### DataTable

```text
┌────────────────────────────────────────────────────────────────┐
│ ☑ Order ID    Product        Date        Total   Status  ⋯   │  header 13px uppercase muted
├────────────────────────────────────────────────────────────────┤
│ ☐ #SHA54321   Man U Away     8 Feb 25    $103    ●Pending  ✎  │  row 56px, hover tint
│ ☐ #FAH12454   iPad 10 Gen    9 Feb 25    $653    ●Done    ✎  │
└────────────────────────────────────────────────────────────────┘
```

- Sticky header inside a `max-height` scroll container for long lists.
- Row selection with a master checkbox and a bulk action bar that appears above the table.
- Status cell = coloured dot + `StatusBadge` with a tinted background and 1 px border in the same hue.
- Row actions collapse to a `⋯` menu below 768 px; below that the row becomes a stacked card.
- Pagination: page-size select, "Showing 1–20 of 250", numbered pages with ellipsis, prev/next.

### ProductCard (storefront)

```text
┌───────────────────────────┐
│  ▢ 4:3 image    ♡  ⤢     │  hover: 2 px brand border, image scales 1.03
│  ── −35% ──               │  discount pill top-left, wishlist top-right
├───────────────────────────┤
│ TechWorld · ★ 4.7 (212)   │  store + rating, 12px muted
│ AeroLux Wireless          │  name 15px 600, 2-line clamp
│ Headphones                │
│ $84.99  $129.99           │  price 17px 700, compare-at struck-through muted
│ ● In stock    [Add]       │  stock dot + label, brand button
└───────────────────────────┘
```

Out-of-stock swaps the button for a disabled "Notify me" and desaturates the image 12 %.

### Order tracking timeline

Vertical rail with a 2 px connector and 20 px nodes; completed nodes filled brand with a check, the current node ringed, future nodes hollow. Each node shows the step name, the timestamp, and a secondary detail line (estimated delivery, carrier, location).

### Sidebar navigation

```text
OVERVIEW
  ▦ Dashboard          [active]
  ◫ Analytics
FEATURES
  ▣ Products        ⌄
      My Products        128
      Create Product      +
  ▤ Orders          ⌄
      All Orders
      Returns              7
SETTINGS
  ⚙ Store
  ⚙ Payouts
```

Sections use a 11 px uppercase `--text-subtle` label. Badges use a small pill in `--violet-bg` (pending) or `--danger-bg` (attention).

## 7. Storefront

```text
Topbar   announcement strip (brand gradient, dismissible) + locale/currency
Header   logo · category mega-menu · search with ⌘K · wishlist · cart
Hero     2-column: headline + search + trust chips | illustration/gradient panel
Rails    category tiles · flash deal with countdown · featured brands
Grids    "Trending now" 4-up · "Best sellers" 4-up · "From our stores" store cards
Footer   4 link columns · newsletter · payment badges · legal
```

- Mega-menu on hover/focus: 2-level, image-led, keyboard navigable, closes on `Escape`.
- Product grids: `repeat(auto-fill, minmax(230px, 1fr))`.
- Search is prominent (full-width on mobile, inline with a category `Select` on desktop), instant results in a dropdown after 250 ms debounce.
- Cart drawer on add-to-cart with quantity stepper and "go to cart" / "checkout".

## 8. Accessibility contract

| Concern | Rule |
| --- | --- |
| Focus | `:focus-visible` → 2 px brand ring + 2 px offset, never removed |
| Landmarks | `header`, `nav`, `main`, `aside`, `footer`; one `h1` per page |
| Contrast | ≥ 4.5:1 body, ≥ 3:1 for large text and UI borders, verified in both themes |
| Status | colour + icon + text, always |
| Charts | an adjacent visually-hidden table or `aria-label` summary conveying the same data |
| Motion | disabled under `prefers-reduced-motion` |
| Touch | ≥ 44×44 px targets on mobile; 8 px minimum gap |
| Zoom | usable to 200 % without horizontal scroll |

## 9. Breakpoint behaviour

| Breakpoint | Grid | Sidebar | Filters | Tables |
| --- | --- | --- | --- | --- |
| ≥ 1600 | 5 columns | expanded, persistent | rail visible | full |
| 1400–1599 | 4 columns | expanded | rail visible | full |
| 1200–1399 | 4 columns | expanded | rail visible | full |
| 992–1199 | 3 columns | collapsed icons | drawer | full |
| 768–991 | 2–3 columns | off-canvas | drawer | card mode |
| 576–767 | 2 columns | off-canvas | drawer | card mode |
| < 576 | 2 columns compact | off-canvas | drawer | card mode |

## 10. Implementation rules

1. Bootstrap 5 supplies grid, utilities and behaviour; the token layer overrides colour, radius, shadow, typography and every component variant.
2. No Tailwind, no second component library, no inline `<style>` blocks in JSX.
3. Icons are always `lucide-react` at 18/20/24 px with `stroke-width={1.75}`; never emoji as UI iconography.
4. Charts are Recharts, wrapped in a shared `ChartCard` and dynamically imported.
5. Every new component ships its four states (loading, empty, error, data) and passes the viewport matrix: 1920, 1440, 1366, 1024, 768, 430, 390, 360.
