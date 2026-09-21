# waymark-admin

**One codebase, two surfaces.** Local Admin runs on the shop's LAN against
`Waymark.StoreServer`. Cloud Admin is the same application, deployed as a PWA
against the Cloud API, with every screen showing a customer name, phone or
email hidden — not disabled, absent.

That difference is the DPIA's most visible promise, so it is a build-time
surface flag, not a runtime role check. `src/main.tsx` holds it (`SURFACE`);
nothing reads it yet, because nothing in Phase 0.5 shows a customer at all.

## Stack

React · TypeScript · Tailwind · Vite. Radix primitives, TanStack Table,
TanStack Query, react-hook-form + zod and Recharts are the decided stack and
land in Phase 1, when there are screens that need them.

## Running it

**It has never been run.** There is no Node on the machine it was written on,
so every file here is reviewed-but-unexecuted. Expect to fix a version or two
in `package.json`.

```bash
cd waymark-admin
npm install
npm run dev
```

StoreServer must be running on `http://localhost:5290` (see `docs/status.md`
§6.2). The dev server proxies `/api` to it — `vite.config.ts` — rather than
StoreServer enabling CORS: the store's server has no business accepting
cross-origin calls so that a dev server can be convenient.

Then type a staff id into the box. Seed-42's owner is
`01JCWEQNC0W9W3YV7F0CPNDDCB`'s colleague — read the ids out of the store, or
use the ones in `docs/status.md` §6.2. A cashier sees the count of cards held
back; the rung above sees the cards.

## What is on the page (Phase 0.5, hop 8)

One board: the near-expiry cards this staff member may act on, each with its
Because block, its computed-at age, and its options. Accepting records the
decision and closes the card. **It does not apply the markdown** (D-069) — the
option's payload says what would be done, and Phase 1 does it.

## Before the next component

- **CSS logical properties from the first component.** `margin-inline-start`,
  never `margin-left`. RTL is never retrofitted. `src/index.css` is written
  this way throughout; switching `<html dir>` is the whole change.
- **Violet `#5A3AA8` is the operator**; every button, action, confirmation and
  active state. **Cyan `#0E8C86` is Almanac** — edges, fills, interval caps,
  3px rules. Never a button, never a link, never text; cyan text is `#0A5F5B`.
- **Cyan is never a card fill.** Engine cards are white with a cyan top edge.
- **Two semantic colours only** — critical and warning. There is no positive
  state; a shelf that is fine gets no card.
- **Label before colour.** The urgency chip carries a word, always.
- Archivo for readable text, IBM Plex Mono for labels, SKUs, quantities and
  figures. **Neither is bundled yet** — that is a download, and it is on the
  Phase 1 list waiting for Hakim. Until then the page falls back to the
  system's sans and mono.

The tokens are at the top of `src/index.css`, so the seven locked lines can be
checked without reading a component.

Full palette and the seven locked lines: `Waymark Brand Identity Design`.
