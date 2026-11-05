# Design

## Palette: "Lavender"

From [Color Hunt](https://colorhunt.co/palette/f1eaffe5d4ffdcbfffd0a2f7):

| Swatch | Hex | Contrast with white | Role |
|---|---|---|---|
| Lilac 50 | `#F1EAFF` | 1.17 : 1 | Selected rows, soft badges, the sidebar pipeline card |
| Lilac 100 | `#E5D4FF` | 1.38 : 1 | Avatars, borders, text selection |
| Lilac 200 | `#DCBFFF` | 1.62 : 1 | Chart bars, scrollbars, the Prospecting accent |
| Lilac 300 | `#D0A2F7` | 2.06 : 1 | Accents, the funnel top, hover borders |
| Brand violet | `#803FC6` | 6.09 : 1 | Primary buttons, active navigation, the main chart series |

Why the extra violet: none of the four swatches can hold white text, so a button made from them would fail WCAG AA. The brand violet sits on the same hue (OKLCH 302°), so it reads as part of the palette rather than an add-on.

Rules that keep it working:

- **Lavender is a tint, never the page.** The canvas is a near-white with a faint violet lean (`oklch(0.982 0.006 300)`).
- **Brand violet is not danger.** Destructive actions use a deep raspberry (`oklch(0.5 0.17 12)`) plus a confirm dialog.
- **Neutrals carry 0.006–0.03 chroma at hue 300**, so greys belong to the palette.
- **Gradients stay in one hue.** Six ramps: brand (violet), mint, amber, danger, teal and orchid. Nothing fades from violet into blue.
- **Charts** lead with the brand violet and the lavender swatches, then add mint, amber, raspberry and slate for multi-series charts. On the funnel, the two pale bars carry dark ink and the deep bars carry white.

All tokens live in [`client/src/styles.css`](client/src/styles.css) as OKLCH values.

## Type and density

- Geist (variable) for everything, Geist Mono for usernames and shortcuts. Weight carries the hierarchy.
- Root size 14 px for a compact SaaS density; table rows are 40 px tall with 13 px text.
- Tabular numerals on every money, count and score column.

## Motion

160–260 ms ease-out-quart / expo on state changes only: drawers, menus, toasts, list entry, the board drop flash. `prefers-reduced-motion` makes them instant.

## Components

- **One data table everywhere**: global search, per-column search row, advanced filter builder (AND / OR rules), column chooser, sort, page size, totals across all filtered rows, skeleton and empty states, and Excel / CSV download of the filtered rows with the visible columns. Row actions stay pinned to the right edge.
- **Forms open in a right-hand drawer** so the list or board stays in view.
- **Toasts**: SweetAlert2, top right, 10 s, progress bar, pause on hover, tinted by type, plain text only.
- **KPI tiles**: a single-hue gradient with soft concentric rings; every tile is a link to its records.
