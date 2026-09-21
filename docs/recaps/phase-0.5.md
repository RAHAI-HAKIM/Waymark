# Phase 0.5 recap: the walking skeleton

**Built 18–20/09/2026; phase closed 21/09/2026.** Next is Phase 1, planned in
`../phase-1-plan.md`.

A snapshot, not a live document (see `README.md` in this folder). Decisions are cited by
number; `../decisions.md` keeps each one's title, and this file holds the reasoning and the
rejected alternatives.

---

## 1. What Phase 0.5 was for

One thin cut through every layer, to find out whether the architecture Phase 0 laid down
holds when something real passes through it. The Build Plan's words: *one product, one store,
one department, ugly UI*. No breadth and no polish — only the question **does the path exist
end to end**, and the defects that answer it.

The cut: **scan → cart → complete sale → transaction and stock movement → outbox → a stub
cloud reads it → expiry evaluator flags a batch → envelope → Integration Layer role check →
card in Local Admin → accept → decision written and the card closed**.

Everything in it is real except the cloud. The database is the encrypted store database, the
money is `Money`, the store scoping is the global filter, the commands go through the real
executor, and the POS talks HTTP to StoreServer even though both run on one machine.

## 2. Exit criteria, as closed

| Criterion | State |
| :---- | :---- |
| The whole cut runs end to end on generated data | ✅ seed-42: scan `2000000000015`, pay, invoice `GDZ-001-2026-000004`, one outbox row; the evaluator flags ~99 batches of ~315; a card decided in Local Admin |
| Nothing merged that cannot be explained | ✅ twelve decisions (D-063–D-074), each with its rejected alternatives, and a reading guide per session in `../status.md` |
| Every risky rule proven by breaking it (D-012) | ✅ hop 1: 8 breaks · session A: 11 · B: 7 · C: 9 · D: 8 |
| The synthetic store still loads; the previous demo still runs | ✅ every session, and on the real StoreServer process |
| Solution green | ✅ **995 tests**, 0 warnings, Debug and Release |

## 3. What was built

Four sessions, eight hops, one commit each (D-069).

| Session | Hops | What landed |
| :---- | :---- | :---- |
| — | 1 Scan, cart | `KeyboardWedgeScanner` rewritten to end a scan on silence (D-063); the five-step lookup (D-066); the store's date (D-067); the preview cart and the till (D-068) |
| A | 2 Complete sale | `CompleteSaleHandler`: the transaction, its items with TVA from TTC, a movement and a level per line, the cash payment and its tender rounding (D-070); `POST /api/sales`; the Pay button; and the write-side store guard that closed F-21 (D-071) |
| B | 3–5 Outbox, tier 2, stub cloud | The anonymous basket staged as an outbox row **in the sale's own transaction**, gapless (D-072); `ITier2Writer` with a writer that keeps nothing (D-065); a stub cloud that parses and acknowledges, in the tests |
| C | 6 Expiry evaluator | `NearExpiry` in Domain, pure; the cold-start window in `parameter_registry`; a card carrying its Because block, its parameter version and its computed-at time; `POST /api/engine/expiry` (D-073) |
| D | 7–8 Integration Layer, Local Admin | One role check on `roles.rank` for both the board and the decision; the decision row and the card's close committed together; `waymark-admin` scaffolded — Vite, React, TS, Tailwind, brand tokens, RTL (D-074) |

**New in the solution:** `Waymark.Pos.Tests`, and `waymark-admin` (Node 24.19.0 LTS, 82
packages, `tsc --noEmit` and `vite build` both clean).

## 4. Decisions

### D-063 — A scan ends on silence, and every keystroke is held until classified (F-6)
Hakim, 18/09/2026. `KeyboardWedgeScanner` groups characters arriving within 50 ms of each
other into a burst. The burst ends when nothing arrives for 50 ms, or at once on any control
character, and only then is it classified: 8 characters or more (EAN-8, the shortest retail
code) is a scan; anything shorter was typed and goes back through `Typed`, in order, before
any key that followed it. **Why silence:** a configured terminator broke whenever the
scanner's suffix setting differed, and the old custom-terminator path never ended a scan;
silence works whatever the suffix. A control character is kept as a shortcut so a scanner
sending Enter doesn't pay the 50 ms, and a CR LF's second half is swallowed. **Why hold:** the
first character of a scan is indistinguishable from a keystroke, so passing characters
through meant half a barcode had already reached the focused box; holding costs typing at
most 50 ms. **Why a minimum length:** once Enter is no longer required, two keys rolled over
by a fast typist fit inside the window, and a PLU typed by hand is 4–5 digits. The silence
timer comes from the injected `TimeProvider` and is posted back to the constructing thread's
`SynchronizationContext`, so the scanner stays single-threaded. **Rejected:** a list of
known terminators (Hakim's reason above); letting characters into the box and clearing them
afterwards, hiding the box meanwhile (the box's change events still see the partial code,
and restoring the caret and selection is fragile); a window wider than 50 ms (it meets fast
typing at 60–100 ms and delays every typed key further).

**Open, for the POS cart (Hakim's idea, 18/09):** what the cashier sees when input does not
become a product line. The idea is to say so in the UI and move to search by name. Three
cases, since `Accept` returning false does not itself mean a failed scan:
- `Accept` returns **false**: a control key that belongs to the cashier (an Enter that ends
  no scan). Nothing to show; the cart handles the key.
- **`Typed` hands back text**: the cashier typed, so the box is a search. This is where
  search by name (and by code typed by hand) starts.
- **`Scanned` gives a code that matches no product**: the case the UI must flag, with the
  code shown and a move to search by name.

Decide with the cart window.

### D-064 — Phase 0.5 emits only the anonymous basket; the customer period record is Phase 2
Hakim, 18/09/2026. A sale's outbox row in the skeleton is the `AnonymousBasketRecord`, whose
fields D-043 and the contract already fix. **The customer period record and its spend bands
are deferred to Phase 2**, "Statistics for real", which builds the tier 1→2 boundary and
the outbox streams (Build Plan). **Why:** it is monthly, not per sale, so no hop of the
skeleton produces one; and its bands are the kind of choice D-043 wants made with the data
in view, not guessed. **Consequence:** the sale's outbox row carries no pseudonym, so in 0.5
the pseudonymisation boundary is exercised through the tier-2 hop (D-065), not the outbox.

### D-065 — Tier 2 is a stub in Phase 0.5, and it will be encrypted
Hakim, 18/09/2026. The skeleton declares the tier-2 writer as a port and supplies an
implementation that stores nothing. **Why:** no later hop reads tier 2 (the expiry evaluator
reads batches from tier 1), and 0.5 proves the path, not the statistics. The real DuckDB
writer arrives in Phase 2 with D-064. **Tier 2 will be encrypted at rest** (O-23, first half):
it holds transaction grain keyed by pseudonym, and the tenant key sits on the same machine,
so a stolen disk could re-identify it. *How* (DuckDB's own encryption, which key, where it
lives) stays open as O-23 until the Phase 2 writer.

### D-066 — What the till may sell for a barcode (hop 1)
Hakim, 18/09/2026. `IProductLookup` (a Domain port, implemented in Persistence) answers
*this barcode, in this store, today*, with one of three results, never an exception:
found, unknown barcode, or not sellable with a reason. On the wire (`Contracts/Pos`)
every result is a 200, so an HTTP error only ever means the server failed. The rules, each
a test in `ProductLookupTests`:
- **Variant:** discontinued still sells its remaining stock; archived does not. Weighted
  does not until Phase 1 (scales, weight-embedded codes).
- **TVA rate:** from the product's categories. No rate, or two different rates, is not
  sellable (a null rate is not 0%, D-037). The real rule is O-24.
- **Price:** the store's `retail` row with `valid_from ≤ today < valid_to` (null `valid_to`
  is open-ended; `valid_to` is exclusive, the next price's first day). The latest
  `valid_from` wins; `promotional` rows are ignored in the skeleton. No price is not
  sellable, never zero. An HT price in force is refused, because converting it would be a
  new rounding site.
- **Today** is the store's date from `IStoreCalendar`, not UTC's: prices change at the
  store's midnight (F-22 for how the date is known).
- **Stock on hand** is the sum of `inventories.quantity` over the store's batches. Zero or
  negative still sells, and the till shows a warning (CLAUDE.md §3.8).
- **Store scoping** is the global filter alone; the query never filters by store itself.
- **The route is `GET /api/products/lookup?barcode=…`**, a query parameter rather than a path
  segment. ASP.NET Core leaves `%2F` encoded in a route value, so a typed `12/34` was looked
  up as `12%2F34` (found in the end-to-end run, 18/09), and decoding it again would
  double-decode every other code. A query string is decoded exactly once. A blank code is a
  400, the only non-200 answer that isn't a server failure.

**Rejected:** a 404 for an unknown barcode (the till would have to tell "no such product"
from a routing failure); converting HT to TTC in the lookup; picking the primary category's
rate on a conflict (that is O-24's decision, not the skeleton's).

### D-067 — The store's time zone is resolved through a hand-kept IANA→Windows map (F-22)
Hakim, 18/09/2026. `stores.timezone` stays an IANA name. `StoreTimeZones` (Application) maps
each name Waymark serves to its Windows zone id, which resolves under the build's invariant
globalisation where the IANA name does not. There is one row today: `Africa/Algiers` → `W.
Central Africa Standard Time`, UTC+1 all year. StoreServer resolves the store's zone at
start and **refuses to start on an unmapped zone**, rather than fall back to UTC and move
the day prices change on. `StoreCalendar` asks the clock each time and never caches the
day, so a server left running over midnight changes prices at midnight. A store with no
row yet has no zone, and asking its date is an error. **Rejected:** turning ICU on for the
hosts (culture-sensitive behaviour would then differ between the hosts and the tests,
which run invariant); a fixed configured offset (it makes the column decoration and breaks
the first market with daylight saving time).

### D-068 — The till in hop 1: a preview cart, a session that keeps scan order, neutral notices
Hakim, 18/09/2026. The POS side of hop 1, in `Waymark.Pos`, with its logic tested in
`Waymark.Pos.Tests` (a new project, Hakim's choice) and the window kept thin:
- **`StoreServerClient`** separates *the server answered* (found, unknown, not sellable: all
  shown as they are) from *the server could not answer* (refused, 3 s timeout, error status,
  an unreadable reply). There is no Level-2 cache in the skeleton, so an outage is shown
  plainly. The caller's own cancellation propagates, and is never reported as an outage.
- **The cart is a preview.** A scan adds one whole unit and a repeat scan adds to its line;
  line total = `price × count` with `Money * int`, which is exact and creates no rounding
  site. The receipt's figures (TVA split, cash rounding) come from the server in hop 2, and
  fractional quantities come with weighing in Phase 1. Wire figures are read exactly, in the
  invariant culture; one that would need rounding is refused. An empty cart has no total,
  not a zero without a currency.
- **Stock:** a line says so when its count exceeds the stock on hand (Hakim, 18/09: the
  "stock ≤ 0" rule, extended). A notice, never a refusal.
- **`TillSession` handles codes in the order they were scanned**, whatever order the
  answers arrive in; a failed lookup does not stop the codes behind it.
- **Notices are neutral text naming the code and the reason:** unknown code, not sellable,
  StoreServer unavailable. **No semantic colour and no Almanac diamond in the POS** (Hakim,
  18/09): the brand deck reserves warning for states Almanac raises, and critical needs a
  CRITICAL label. Violet is for actions only. Styling is deliberately unfinished.
- **The window feeds every keystroke through the scanner** (D-063): a tunnel `TextInput`
  handler; Enter and Tab sent to the scanner as keys, because a suffix arrives as a key;
  `Flush` before non-text keys; `Reset` on a focus change; `Typed` inserted at the caret by
  hand. Enter after a code typed by hand looks it up. Search by name is Phase 1.
- **Its only configuration is StoreServer's address:** `--server=`, else `WAYMARK_SERVER`,
  else `http://localhost:5290/`.

**Rejected:** letting each lookup answer update the cart as it arrives (lines would land out
of scan order, and a late notice could hide a later line); colouring the stock and code
notices with warning and critical (brand deck, slide 11).

### D-069 — Phase 0.5 is a thin slice: hops 2–8 in four sessions, one commit each
Hakim, 19/09/2026. Hop 1 was built at Phase 1 depth (8 steps, a full rule set, notices).
The depth found real defects (F-22, the `%2F` route, the relative-path keys directory),
which is what a skeleton is for. The rest cost time and understanding, and "nothing merges
that cannot be explained" matters more than breadth. So the remaining hops follow one rule:
- **The minimum that makes the path real:** one store, cash, the happy path. Everything else
  goes on the Phase 1 list in `status.md` §6, not into the code.
- **Tests:** one happy path, plus tests on **the hop's one risky rule**, and only that rule
  is proven by breaking it (D-012).
- **One session per hop:** a one-paragraph hop spec with its decisions, then the build (with
  one ✍ piece Hakim may write), then a short "how it works" guide, then Hakim's review and
  **a commit per hop**.

The hops, merged into four sessions: **A** complete sale (hop 2); **B** outbox, tier-2 stub
and stub cloud (hops 3–5); **C** expiry evaluator (hop 6); **D** Integration Layer and
Local Admin (hops 7–8). Decided with it:
- **Stub cloud:** a tiny reader inside the outbox tests that parses what leaves and
  acknowledges it. The real transport is Phase 4.
- **Pseudonymisation is not exercised in 0.5.** Sales in the slice have no customer, and
  the basket carries no pseudonym (D-064). The boundary comes alive with the customer period
  record in Phase 2; Phase 0's key-custody tests still guard it.
- **Expiry window:** one 7-day placeholder for every category, in `parameter_registry`,
  labelled a placeholder. The per-category windows are Hakim's engine decision, in Phase 2.
- **Roles:** a manager may see and decide inventory cards, and a cashier is refused, both
  from `roles`. The staff member is named per request, with no login until Phase 1.

**Rejected:** keeping hop 1's depth (Phase 0.5 would take about three weeks, which is Phase 1
under another name); stopping 0.5 at hop 1 (the risky joins, sale ↔ outbox and the
Integration Layer, are exactly what the skeleton must prove before the screens are built).

### D-070 — A cash sale, completed in one unit of work (session A, hop 2)
Session A, 19/09/2026. `CompleteSaleHandler` (Application) stages the whole sale and the
executor commits it once (D-050): the transaction, its items, a stock movement per item
with the batch level lowered, one cash payment, and the tender rounding. The rules marked
**(provisional)** were chosen to keep the slice moving; each is waiting for Hakim's
confirmation.
- **The till sends codes and counts, never prices.** Every line is priced again on the
  server by the lookup's own rules (D-066); the till's cart is a preview (D-068).
- **Money:** each item goes through `SaleArithmetic` (moved from the generator into
  `Domain/Sales`, so a generated receipt and a real one run the same code). TVA is
  extracted from the TTC total (D-033), and the transaction's totals are the sums of its
  items. The payment is the exact total; the 5 DZD cash step goes to `rounding_variance`
  (D-034). A zero total writes no payment row (the CHECK refuses a zero amount).
- **Batches (provisional):** first in, first out, as the synthetic store sells; expired and
  empty batches are skipped, and a line splits across batches. What the records say is not
  there comes out of the newest batch, so the level goes negative rather than the sale
  being refused (CLAUDE.md §3.8). A variant never received has no batch to name
  (`stock_movements.batch_id` is required), so it is refused. `inventories.quantity` is
  lowered with each movement. `BatchAllocation` (Domain) holds the rule.
- **Invoice numbers (provisional):** `{store code}-{calendar year}-{000001}`, the generator's
  format, gapless. The number is read and staged inside the sale's transaction, so a
  refused sale uses none. StoreServer runs one sale at a time, and the unique index on
  (store, invoice number) refuses a duplicate if two ever raced.
- **Cash session (provisional):** the terminal's open session, or one the first sale opens
  with no float. Opening with a counted float is a cashier's action, in Phase 1.
- **Who sells:** the till is configured with its terminal and staff ids (`--terminal=`,
  `--staff=`); no login until Phase 1.
- **No `processing_log` entry:** a sale with no customer touches no personal data (D-045).
- **The wire:** `POST /api/sales`; a refusal is a 200 with its reason, as in the lookup.
  At the till, **no answer is "outcome unknown", never "failed"**: the cart stays, with a
  warning that the sale may have been recorded. A key that makes a repeated request
  harmless is Phase 1.
- **Staging:** handlers stage rows through `IStaging` (Domain), which the unit of work
  implements; `ISalesLedger` holds the sale's reads.

**Rejected:** trusting the till's prices (a stale or tampered preview would be sold);
refusing a sale when the records show no stock (the product is in the customer's hand);
numbering invoices outside the sale's transaction (a failed sale would leave a gap).

### D-071 — Writes are checked against the current store (session A, hop 2; closes F-21)
Hakim, 20/09/2026. The global filter keeps a context from *reading* another store's rows;
nothing kept it from *writing* one, and a row under the wrong `store_id` is invisible to the
store that made it and counted by the store it names (DPIA risk R9). `WaymarkDbContext`
now overrides both `SaveChanges` and `SaveChangesAsync` and refuses any added or modified
`IStoreScoped` row whose `store_id` is another store's, naming the entity and the store.
Two boundaries, each with its reason:
- **A null `store_id` is written.** On the entities that allow one it means every store (a
  promotion that is not store-specific) or none (processing outside a store), and the
  filter already reads those (D-030).
- **A context with no store configured is unchecked.** There is nothing to compare against,
  and such a context reads nothing belonging to a store; it is how the generator commissions
  a store and how tests seed one. StoreServer always configures the store.

`StoreWriteScopeTests` holds the rule, including the async path, which is the one the
application actually uses.

### D-072 — The sale's basket leaves in the sale's own transaction (session B, hops 3–5)
Session B, 20/09/2026. `CompleteSale` now also stages the anonymous basket as an `outbox`
row, and the executor commits it with the sale: **both rows or neither** (CLAUDE.md §3.6).
A sale whose basket was lost would teach the cloud a shop that sells less than it does.
- **The record** is built by `AnonymousBasket` (Application), a pure function tested on its
  own, following D-043 and D-064: lines at **product** grain (a product taken from two
  batches is one line), the store's **hour**, never the second, the weekday, the payment
  class (`cash` is the only tender in the slice), a discount flag, and a **fresh opaque
  basket id**. The outbox row's `entity_type` and `entity_id` stay null, because they would
  be the transaction, and nothing may join the basket back to the till's row.
- **The hour comes from the store's clock.** `IStoreCalendar` gained `Now` and `HourOfDay`
  beside `Today` (D-067): a basket at 00:30 in Algiers belongs to hour 0 of the new day,
  not hour 23 of yesterday in UTC.
- **The sequence** is the last one plus one, read inside the same transaction, so a refused
  sale spends no number and leaves no gap the cloud would read as a lost message
  (sync-design §2.2). `IX_outbox_sequence_number` is unique, so a duplicate is refused by
  the database; StoreServer runs one sale at a time (D-070). **The outbox belongs to the
  store database, not to a store**: the table has no `store_id`, because one store database
  is one store's.
- **Figures are formatted once:** `Contracts.Figures` turns stored integers into exact
  decimal text, and the outbox payloads and the till's answers now share it (D-044).
- **Tier 2 (D-065):** the sale hands its lines to `ITier2Writer`, and `NullTier2Writer`
  keeps nothing. *Provisional:* the call sits in the sale; Phase 2 decides whether the
  tier 1→2 transform runs per sale or nightly, writes the real DuckDB one, encrypted
  (O-23), and adds the pseudonym for sales that have a customer.
- **The stub cloud (hop 5)** is a reader in the tests: it parses each pending row as
  `AnonymousBasketRecord` and deletes it only after reading it (sync-design §2.3, an ack is
  what allows deletion). No production code and no transport: that is Phase 4.

**Rejected:** emitting after the sale commits (a crash between the two loses the basket, and
the outbox pattern exists to make that impossible); numbering the sequence outside the
transaction; putting the transaction id on the outbox row "for tracing" (it is exactly the
join D-043 forbids).

---

### D-073 — The expiry evaluator compares, and the card argues its case (session C, hop 6)
Session C, 20/09/2026. The first thing in Waymark that offers an opinion. `EvaluateExpiry`
(Application) reads one parameter, reads what is on the shelf and writes a card per batch
inside the window; the rule itself is `NearExpiry` in Domain, pure, so it can be argued with
in a test that has no database. Marked **(provisional)** where it is waiting for Hakim.
- **It compares and nothing more** (CLAUDE.md §5): two dates subtracted, one comparison, and
  a sum of what the remaining stock cost. No fitting, no history, no iteration. The window is
  the engine's number; the store only holds it up against what it has.
- **The window is `near_expiry_window_days` in `parameter_registry`**, global scope, seven
  days for every category, `source = 'cold_start_default'` and a `method` that says in words
  that nobody measured it (D-069). `ColdStartParameters` installs it at start when the
  registry has none, and **never repairs it**: a window the engine or a shopkeeper has since
  set is theirs, and overwriting it each start would undo their decision silently. Every card
  carries the version it was judged against. **A missing window refuses the run** rather than
  being read as zero or as seven (D-037): "flag nothing" is indistinguishable from a shop
  where everything is fresh.
- **The window is inclusive** (provisional): a batch expiring on the seventh day is inside a
  seven-day window. Exclusive would put the seventh day silently outside it.
- **Already expired is `critical`, still in time is `warning`.** There is no third colour and
  no positive state: a batch that is fine gets no card at all (CLAUDE.md §6). Past the date
  the option offered is a **write-off**, not a markdown — a markdown then is not a cheaper
  sale, it is an illegal one.
- **The subject is the batch**, because expiry is a property of a delivery, not of a product.
  A batch holds every variant that came in that delivery, so the figures are summed across
  them: the money always adds up, and the quantity only when they share a selling unit —
  pieces and kilogrammes do not add, and `Quantity` refuses to pretend they do (D-036). The
  card then carries one figure fewer rather than a wrong one.
- **Derived figures round half-even**, always, whatever the store's own policy is
  (CLAUDE.md §3.1): a store's policy belongs to what it charges, and an analytical figure
  that moved with it would not compare across stores.
- **The Because block carries three figures**: days to expiry, units on hand, value at cost —
  cost and not price, because what is at risk is the money already spent. Structured, never a
  sentence (D-044); the headline is a rendering of it and the UI localises from the keys.
  **No interval**, deliberately: days on a calendar and units on a shelf are counts, not
  estimates, and the envelope reserves a null interval for exactly that. The day a card
  carries a forecast — how much will sell before the date — it gets a range, and that is the
  engine's work.
- **A card is addressed by rank, not by role code.** D-069 says a manager decides an
  inventory card and a cashier is refused, but `manager` is a code one shop uses and another
  does not; naming a role a store has never heard of fails on the foreign key, which is how
  this was found (the mini test store has only `owner` and `cashier`). So the evaluator asks
  for **the lowest rank strictly above the lowest one**, which is the manager in a three-role
  shop and the owner in a two-role one. A shop with one role addresses it to that role: a
  card nobody may see is worse than one everybody may. Hop 7 compares the same ranks.
- **Re-running replaces a batch's card** rather than adding a second one: the earlier card
  moves to `superseded` and a new one is written, matched on the dedupe key (store, type,
  subject type, subject — D-044). *Provisional:* D-044 describes updating the live row and
  writing a superseded copy; this keeps one live card per batch, which is the outcome that
  matters, with one fewer moving part.
- **It writes cards, it does not retire them** (provisional): a batch that has since sold out
  keeps its card. Silently deleting a suggestion a human has not answered is the one thing it
  must not do; whether such a card expires or is withdrawn is Phase 1's.
- **Nothing decides** (CLAUDE.md §4): no price changes, no stock moves, no decision row. And
  **no `processing_log` entry**: a batch is not a person (D-045).
- **The wire:** `POST /api/engine/expiry`, run on request. The engine "ran last night"; a
  nightly job is Phase 1's, and what the skeleton has to prove is that a comparison over real
  stock produces a card a human can be shown.

Found while proving the tests fail: **the evaluator's reads are scoped three times over** —
`inventories` and `batches` by the store filter, `batch_items` through its batch (D-062) —
and the cross-store test only fails when all three are lifted. Defence in depth, not a gap,
but worth knowing before anyone "simplifies" one of them away.

**Rejected:** naming `manager` in the code (see above); reading a missing window as a default
(absence is never zero); putting the comparison in Persistence next to the query (the rule
would then need a database to argue with); giving the card an interval to satisfy §5
literally (a fabricated range on a count is worse than an honest null).

---

### D-074 — A card is addressed by rank, answered by a person, and closed with the answer (session D, hops 7–8)
Session D, 20/09/2026. The Integration Layer's store half and the first screen. A card the
expiry evaluator wrote (D-073) is shown to somebody senior enough for it, and the one door it
leaves by needs a human on the other side. **The two points left provisional were confirmed
by Hakim on 21/09/2026**, and are marked below.
- **The role check is one function, `CardAudience.MayDecide`, in Domain**, and both the board
  and the decision go through the same copy. A rule about who may see what is the kind that
  gets duplicated — once in the API, once in the UI, once in a report — and the copies drift
  silently, because neither too strict nor too loose raises anything. ✍ **Hakim writes it**;
  until then it refuses everybody, which is the only safe direction to be wrong in (D-030),
  and seven tests that need it to say yes are skipped.
- **Ranks, not role codes** (D-073's finding): the card carries a role code, the check
  compares `roles.rank`, and the answer is "this rank and anything above it" — an owner
  locked out of what a manager can do is not a shop anybody would run.
- **A cashier is told how many cards are above their rank, never shown them.** An empty board
  and a quiet shop look identical, and only one of them is true. The count crosses; the cards
  do not.
- **Unknown, foreign or suspended staff get no board at all**, and are refused rather than
  given the most junior rank (D-037). The store filter on `staff` is what keeps a real manager
  from another shop out, and it is tested by breaking it.
- **The decision row and the card's move to `decided` commit together** (D-050) — the risky
  rule of the hop. A decision whose card stayed pending is answered twice; a card marked
  decided with no decision row is a suggestion that vanished with nobody's name on it, and
  `recommendation_decisions` is the audit trail the whole surface exists for.
- **A card is answered once.** Already decided, superseded or expired is refused: the decision
  table is append-only, and a second row makes "what was decided" a question with two answers.
- **The option has to be one of the card's.** An option id from another card passes the
  foreign key and records a decision about the wrong thing.
- **Accept and dismiss only. Confirmed 21/09/2026.** Adjust needs an amended payload and
  snooze a date, and both are CHECKs with nothing in the slice to satisfy them; refused on
  the way in, because it is a question about the request and not about the person. Phase 1
  adds both.
- **Accepting does not carry the intent out** (D-069): no price marked down, no stock written
  off, `applied_at` and the resulting entity left null for Phase 1 to fill.
- **No `processing_log` entry**, and this is a deliberate departure from D-069's wording
  ("Accept writes `recommendation_decisions` and the log"). `processing_log` records
  operations on **personal data**, named by a `Pseudonym` (D-045, D-061); a card about a batch
  names no data subject, and a log entry with no subject would be the guessing D-045 rules
  out. The staff member's action is recorded where it belongs — on the decision row,
  `decided_by`, append-only. **Confirmed 21/09/2026.** The first card whose subject is a
  customer makes viewing it a consultation, and that entry is written then — Phase 1 G7, and
  the Integration Layer's other half in Phase 2.
- **The wire:** `GET /api/recommendations?staff=` and `POST /api/recommendations/decide`. The
  staff member is named on the request, with no login until Phase 1 (D-069), and a refusal is
  a 200 with its reason as everywhere else in the slice. `DecisionRequest` lives in Contracts
  with every field nullable: a missing field is a refusal a person can read, not a framework's
  model-binding message. The board's own envelope is snake_case like the contracts, so one
  payload does not make a TypeScript reader change conventions halfway down.
- **Local Admin (hop 8)** is the Vite/React/TypeScript/Tailwind scaffold the README always
  said would come, with one page: the cards this person may act on, each with its Because
  block, its computed-at age and its options. It **renders what the store said and decides
  nothing** — no second copy of the audience rule. CSS logical properties throughout, the
  brand tokens at the top of one stylesheet, engine cards white with a cyan top edge, the
  urgency chip carrying a word. The dev server proxies `/api` to StoreServer rather than
  StoreServer enabling CORS: the store's server has no business accepting cross-origin calls
  so a dev server can be convenient.
- **The scaffold has never been run**: there is no Node on the machine it was written on. It
  is reviewed-but-unexecuted code, and `waymark-admin/README.md` says so at the top.

**Rejected:** filtering the board in the UI (the second copy of the rule, and the one that
ends up wrong); checking the audience after the request's details (leaks less, but the order
that reads correctly is "who are you, which card, may you"); writing a `processing_log` entry
for a batch card (an entry with no subject is the guess D-045 exists to prevent); a
`requires_reidentification` flag on the card (D-044 already rejected it: derivable from the
subject type and the role, and free to get out of step).
---

## 5. What the skeleton found

This is what a thin cut is for. Every one of these was invisible until something real ran
through the layer.

| Found | Where | Settled by |
| :---- | :---- | :---- |
| **`InvariantGlobalization` blocks IANA time-zone ids on Windows.** The store's zone could not be resolved at all | Hop 1, on the real process (F-22) | D-067: a hand-kept IANA→Windows map that refuses a zone it does not know |
| **ASP.NET Core leaves `%2F` encoded in a route value**, so the barcode `12/34` was looked up as `12%2F34` | Hop 1, end to end | D-066: the barcode moved to a query parameter, decoded exactly once. A regression check runs on the real process |
| **Relative storage paths** created a keys directory under `src/Waymark.StoreServer/` | Hop 1, twice | Absolute-path commands documented in `../status.md` §8 |
| **Writes were never checked against the current store.** The filter guarded reads only, and a row under another `store_id` is invisible to the store that made it and counted by the store it names (DPIA R9) | F-21, open since Phase 0 | D-071: a guard on both `SaveChanges` paths |
| **The evaluator named a role code its store might not have.** `manager` exists in the grocery catalogue and not in the mini test store, so the card's foreign key failed | Session C, on the real process | D-073: the audience is read off `roles.rank` — the lowest rank above the lowest |
| **The role check was written inverted** — a cashier could decide a manager's card and an owner could not, both failure modes at once | Session D | Four tests written *before* the code caught it immediately. Fixed 20/09 |
| **A test that proved nothing.** The staff test named an id that existed nowhere, so it exercised neither the store filter nor the active-status check | Session D, found by breaking the code | Two tests added: a foreign manager, and a suspended one |

Two properties worth knowing, found the same way:

- **`IX_outbox_sequence_number` is UNIQUE.** The gapless sequence has a database backstop, not
  only an application one (D-072).
- **The evaluator's reads are scoped three times over** — `inventories` and `batches` by the
  store filter, `batch_items` through its batch (D-062). The cross-store test only fails when
  all three are lifted. Defence in depth rather than a gap, but worth knowing before anyone
  "simplifies" one of them away.

## 6. What Phase 0.5 leaves open

**Confirmed by Hakim on 21/09/2026:** D-074's two points — no `processing_log` entry for a
card whose subject is a batch, and accept/dismiss only.

**Still provisional**, each with where it is settled:

| Rule | Settled in |
| :---- | :---- |
| D-070: FIFO by received date, shortfall from the newest batch | Phase 1 F1–F2, with receiving and adjustments |
| D-070: invoice `{store code}-{calendar year}-{000001}` | Phase 1 D1, with the receipt |
| D-070: the cash session a sale opens with no float | **Replaced** in Phase 1 C1 by a counted float |
| D-072: the tier-2 call sits inside the sale | Phase 2, which decides per sale or nightly and writes the real DuckDB one |
| D-073: the window is inclusive | Phase 2, with the per-category windows |
| D-073: supersede-and-insert rather than D-044's update-in-place | Phase 2, when the engine re-fits |
| D-073: a card whose batch has sold out keeps its card | Phase 1 I5, which retires cards and carries out an accepted markdown |

**Open questions carried forward:** O-23 (how tier 2 is encrypted and with which key — Phase
2), and **O-24 (which TVA rate applies when a product's categories disagree, or one has
none)**, which **blocks Phase 1 checkout** and is the first thing Phase 1 must settle.

**Deferred capability** — search by name, weighted items, promotional prices, on-account,
more than one tender, returns and voids, the Level-2 cache, logins, and the rest of the Admin
stack — is Phase 1 work, planned in `../phase-1-plan.md`.

## 7. Ways of working that held

- **The thin-slice rule (D-069).** Hop 1 was built at Phase 1 depth and cost understanding;
  the rest shipped the minimum that made the path real, and the phase finished in three days
  rather than three weeks.
- **One commit per hop**, with a reading guide written in the same session (`../status.md`
  §7). The guides are the map Phase 1 expands from.
- **Break the code on purpose (D-012), on the hop's one risky rule.** It caught a test that
  proved nothing — twice.
- **Tests first, then the code.** Session D's role check was handed over as failing tests and
  a guiding comment, and the inverted comparison was caught in seconds. This became the
  handover method for Phase 1.

## 8. What Phase 1 inherits

A path that runs end to end, and a rule for widening it: **every Phase 1 session names the
Phase 0.5 file it expands.** The backlog, the work split, the design gates and the tool ramp
are in `../phase-1-plan.md`.
