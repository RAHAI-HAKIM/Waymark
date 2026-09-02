# waymark-admin

**One codebase, two surfaces.** Local Admin runs on the shop's LAN against
`Waymark.StoreServer`. Cloud Admin is the same application, deployed as a PWA
against the Cloud API, with every screen showing a customer name, phone or
email hidden — not disabled, absent.

That difference is the DPIA's most visible promise, so it is a build-time
surface flag, not a runtime role check.

## Stack (decided, not yet scaffolded)

React · TypeScript · Tailwind · Vite · Radix primitives · TanStack Table ·
TanStack Query · react-hook-form + zod · Recharts.

## Before the first component

- **CSS logical properties from the first component.** `margin-inline-start`,
  never `margin-left`. RTL is never retrofitted.
- **Violet `#5A3AA8` is the operator**; every button, action, confirmation and
  active state. **Cyan `#0E8C86` is Almanac** — edges, fills, interval caps,
  3px rules. Never a button, never a link, never text; cyan text is `#0A5F5B`.
- **Cyan is never a card fill.** Engine cards are white with a cyan top edge.
- **Two semantic colours only** — critical and warning. There is no positive
  state; a shelf that is fine gets no card.
- Archivo for readable text, IBM Plex Mono for labels, SKUs, quantities and
  figures.

Full palette and the seven locked lines: `Waymark Brand Identity Design`.

## Status

Phase 0: directory reserved. Scaffolding lands in Phase 1 alongside the
catalogue and stock screens.
