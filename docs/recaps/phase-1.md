# Phase 1 recap (till Block C): the till runs a counter and a shift

**Blocks A and B, built 22/09–02/10/2026, Block B reviewed 03/10/2026; Block C built
05/10–11/10 and reviewed 12/10/2026.** Phase 1 is not closed: Blocks D to J are still to come,
planned in `../phase-1-plan.md`. This file is extended as each block is reviewed.

A snapshot, not a live document (see `README.md` in this folder). Decisions are cited by
number; `../decisions.md` keeps each one's title, and this file holds the reasoning and the
rejected alternatives. Where this file and the code disagree, the code is right.

---

## 1. What was built

| Block | Sessions | What the till can do after it |
| :---- | :---- | :---- |
| **A** | A1–A5, reviewed 25/09 | Resolve a TVA rate and a price in force; know who may do what; read reason codes; draw the G1 shell from a tested screen model; sign in with a PIN |
| **C** | C1–C3, reviewed 12/10 | Sell only with its drawer open, on a counted float (C1); close on a count, with a variance and a Z number (C1); read an X and a Z, recomputed from the session's rows (C2), whole and by cashier (C3). With it, for the DPIA: an age confirmed before a customer is created, and the logbook's operations (§7) |
| **B** | B1–B10, reviewed 03/10 | Search and open past tickets (B1); hold and cancel tickets (B2); sell by weight (B3); give a discount (B4); override a price (B5); split a payment (B6); keep customers and a tab (B7); record cancels and struck lines (B8); refund (B9a) and spend store credit (B9b); move cash with no sale and clock in (B10) |

**Test count at the review's close:** see `../status.md` §1. Every rule below was proven by
taking it out and watching its test fail (D-012).

---

## 2. Decisions, Block A

### D-075 — A product whose categories conflict, or state no rate, takes the standard TVA rate
Algerian law is a catch-all: the 9 % rate is a restrictive list (CTCA art. 21 and 23), and
anything not cleanly on it is 19 %. **The rule is `TvaRate.Resolve`** (Domain, pure): categories
that agree give their rate; **no category, a null among the rates, or two different rates give
19 % marked `standard_fallback`**. A null forces the fallback even beside a stated rate:
[9 %, null] is 19 %. The standard rate is a constant, not configuration: it is law. **The
fallback is recorded, not silent**: `ProductForSale.TvaRateSource` crosses the wire, the till
shows nothing, and Admin (block E) lists every product selling on it. The composite, mixed and
indivisible-supply rules of the law are **how a person classifies a product in the catalogue**,
not code: no table records that a variant is a bundle. **Rejected:** refusing the sale
(`NoTaxRate`, `ConflictingTaxRates`, both deleted); deriving the source from the rate.

### D-076 — A promotional price is a price, not a discount (A1)
The lookup reads `prices` rows of type `retail` and `promotional`; a promotional row in force
wins, then the later `valid_from`; `valid_to` is exclusive. It is the unit price: nothing is
taken off, `discount_amount` stays zero, and no `promotion_id` is written. A promotional row
above retail is taken as written; one marked HT **refuses** rather than falling back to retail.
The `promotions` tables (percent, amount, bogo) are a discount engine needing the cart: **E2**.
**Rejected:** `min(retail, promotional)`, a rule nobody can see in the row.

### D-077 — Who may do what comes from `roles.rank`; a synthetic PIN never authenticates (A2)
No permissions table. A `Capability` names a minimum rank and `StaffPermissions.May` compares:
this rank and above. **No rank is never permission** (D-037). The ladder is private and throws
for an unmapped capability. Discount, void and no-sale need 2; a price override and credit
management need 3. `StaffPin.IsUsable` is asked before any PIN is checked: false for the
generator's `synthetic:no-login` and for anything blank. **Rejected:** a `role_permissions`
table with nothing to edit it until H2.

### D-078 — The append-only triggers are applied in one transaction (closes F-16)
One `ExecuteSqlRaw` per trigger was 29 durable commits on an encrypted file, on every
StoreServer start: up to 30 s on a slow disk. The loop now runs in one transaction, which also
makes the set atomic. `Migrate()` still runs outside any transaction. **Rejected:** a longer
test timeout; skipping triggers already present.

### D-079 — `reason_codes` is read as data, and the list is what the database will accept (A3)
`IReasonCodes.ForAsync(appliesTo)`: the active reasons of one kind, ordered by `display_order`
**then by code** (the default order is 0 for every row, and SQLite returns ties in any order).
Inactive codes never cross. A kind nobody knows is a 400, not an empty list. Every column
recording *why* is a foreign key into the table, so the offered list is what the database will
accept. `requires_manager` is carried and enforces nothing: rank is `StaffPermissions`.

### D-080 — Archivo, IBM Plex Mono and IBM Plex Sans Arabic are bundled as static TTFs
In `Waymark.Pos/Assets/Fonts`, each with its OFL text. **Static cuts, not the variable file**:
Avalonia's weight selection on variable fonts is unreliable. Neither Latin family has Arabic
glyphs, so Plex Sans Arabic sits beside them. Admin gets its own copy at block E.

### D-081 — An item missing from the catalogue sells as "Divers", for now
Until O-25, "Nouvel article" sells a fixed catalogue item, one variant per TVA rate, at a price
typed at the till: a price override, so rank 3 and a reason. Divers lines are kept out of
Almanac's inputs by their category. Not built yet (with F-27's design). **Rejected:** a
free-text name and price, with no variant, no TVA category and no stock.

### D-082 — The till's shell decides nothing: a tested screen model and one palette file (A4)
`TillScreen.Build` turns what the till knows into everything it shows; the window only draws.
**Colours live only in `TillPalette`**, and a test holds every text to 4.5:1 and every edge to
3:1 in both themes. Two pairs of the G1 kit were corrected: the dark focus ring is `#C6B6EE`,
and the light page is `#F7F5F9`. A figure inside a sentence is its own Plex Mono run, so digits
are not shaped as Arabic. A line removed before payment stays on the ticket, struck, never
charged. Keys are the till's own control: Fluent buttons repaint on hover outside the palette.
**Rejected:** rules in the window; a retry that could record a sale twice.

### D-083 — Sign-in: Argon2id in the host, a session in StoreServer's memory, a lockout (A5)
A PIN is 4 to 8 ASCII digits, hashed with Argon2id (m = 19 MiB, t = 2, p = 1) as a PHC string,
by StoreServer behind Domain's `IPinHasher`. **Five wrong PINs in a row lock that person for
five minutes**, asked before any PIN is checked; sign-ins are handled one at a time. **The
session lives in StoreServer's memory**: a 32-byte token, one session per till. **A request
names no seller**: the server takes it from the session the header names, and only at the same
till. A restart ends every session and forgets every lockout (O-29). PINs are set with
`StoreServer --set-pin=<staffId>`. A stored hash asking for more than 1 GiB, 10 passes or 4
lanes is malformed (F-23). **Rejected:** PBKDF2; a `till_sessions` table; a lockout per till.

### D-084 — The till redraws only what changed and keeps the scanner's focus (block A review)
Found by driving the window headlessly. The ticket jumped to its first line on every redraw:
the window now keeps one scroll viewer and `TillScreen.Compare` says which regions changed.
Signed in, the window is not focusable and a touched key declines the focus, so the search
field keeps the scanner. A key's ground is transparent, so the whole key takes a touch.
**Fluent's accent is pinned to the palette**: no colour reaches the till from Windows. A till
started before StoreServer asks again for what it could not get.

### D-085 — An unconfirmed sale closes Encaisser until the cashier says they have checked (O-27)
A sale with no usable answer may have been recorded. `TillSession.Unconfirmed` is held apart
from the notice slot; while it is set Encaisser is unavailable and nothing is sent; "J'ai
vérifié" clears it. Interim, until I2 gives a sale a key the server recognises.

### D-086 — The till's window is tested in CI, headlessly (O-30)
`TillWindowTests` drives the real `TillWindow` on Avalonia's headless platform with Skia,
against a fake StoreServer, one test at a time. A defect found in the window gets its test
there. Not covered: a real Win32 window (DPI, touch gestures).

---

## 3. Decisions, Block B

### D-087 — A line is named by its own id; held and cancelled tickets live in the till's memory (B2)
A cart line has an id local to its ticket, never sent. **A repeat scan adds to the product's
latest line only if that line is plain** (not struck, weighed, discounted or overridden). The
− / + stops at one; the last unit goes with "Retirer la ligne", which leaves the line struck.
The count takes 1 to 9 999 typed. **"Attente" (F3) holds a ticket; "Annuler ticket" puts it in
"Brouillons"**: both in the till's memory, any cashier may take one up, a draft is gone when the
date changes. "Changer de caissier" puts a ticket with lines on hold. **Rejected:** parked
tickets on the server; a stepper that goes to zero.

### D-088 — B1: one field that scans, types a code or searches a name
A scan is an exact barcode; typed digits are a barcode, then a PLU; text with a letter is a
name search. **A name matches** when every word typed starts a word of the product and variant
name, case and accents folded by a table (`NameSearch`, Domain); at least 2 characters, at most
20 results; a name holding a typed word whole comes first, **then the names the first typed
word begins** (block B review: "lait" then Entrée sold biscuits). Results float under the
field; a touch sells, like a scan. `3*` makes the next scan add three. **Rejected:** a second
field for barcodes.

### D-089 — B1: past tickets, read-only
"Tickets" lists this till's sales of the day; a number typed opens one, read-only, with
today's catalogue names and no customer. **Today's tickets at this till are anyone's; an
earlier day or another till needs rank 2** (`ViewOtherTickets`), or a manager's PIN (D-109).
The store's day runs midnight to midnight in its own zone.

### D-090 — B3: weighed goods are typed or read from a scale label; a label's price is exact
Three ways in: a weight typed (kg, no more decimals than the unit allows, at most 99,999); a
**weight label**; a **price label**. The lookup tries the barcode, the PLU, then the label,
read by the store's mask (`stores.scale_label_format`, presets and a custom one;
`--scale-format=`). **A price label's figure is exact (closes O-26)**: the quantity is worked
back from it and rounded to the unit's step, so stock absorbs the gram and money never moves;
`transaction_items.quantity_source` says which figure the row keeps exact. **The server
prices**; a weighed line never merges and takes no `3*`. A line over several batches is priced
once and split (D-103). **Rejected:** charging weight × price against a label's printed price
(Law 04-02); the gap as a discount nobody decided.

### D-091 — B4: a discount given at the counter, rank 2, percent or amount, on a line or the ticket
A discount a person decides then, with a `discount` reason; one created in advance is the
`promotions` engine (E2). **Rank 2**; a cashier's needs a manager's PIN, checked in StoreServer
only, and the row's `authorised_by` is the manager. A ticket discount follows the ticket: a
percent re-applies, an amount stays, capped. **The arithmetic is `Discounts`** (Domain,
Hakim's): a percent rounded once, a ticket discount worked out once on the lines' totals and
split with `Allocate`, each line's total spread over its batch rows the same way. The till
previews with the same function; the sale sends what was given, never money. The approval is
`POST /api/till/authorise`, kept by the server (D-105). **Rejected:** cashiers discounting alone
under a threshold; dropping a ticket discount when the ticket changes.

### D-092 — B5: a price override is recorded beside the price it replaced, within a band, by rank 3
`transaction_items.list_price`, `override_reason_code`, `override_authorised_by`; `sell_price`
stays what was charged. **The band** (`PriceOverride.Check`, Hakim's): up to +20 % above the
price in force, rounded down; down to any price above zero, with a warning below the batch's
cost. Per line only; a weighed line takes none. The same migration settled F-28 (a ticket
discount's reason, authoriser and note). **Rejected:** down only; no record beyond the price;
rank 2.

### D-093 — The manager step floats; a key reads its own touch
The PIN step is a card over the whole screen on a scrim, closed by ✕ or Échap. **A key acts on
a press released over it**, read from the pointer: Avalonia's `Tapped` lost every other digit
of a PIN typed at a cashier's pace. The scanner's silence timer, firing a clock tick early,
waits for the rest of the window. **Rejected:** a second OS window.

### D-094 — What edits a line stays in the rail; what freezes the ticket floats
A discount, a price or a weight stays in the rail beside the ticket; the payment and the PIN
float in one shared frame. **While one floats, scans are ignored**, and the notice slot says
so. **Rejected:** everything in the rail; everything floating.

### D-095 — B6: split tender: card and BaridiMob parts, the rest in cash, rounded once
The parts are exact and never rounded; **only the cash rest rounds**, once, whatever the order.
The parts may not exceed the total. `Tender.Settle` (Domain, Hakim's) is asked by the till and
again by the server on its own total. A part's reference is optional; **one that reads as a
card number** (13 to 19 digits passing Luhn) is refused and never stored. **Nothing is taken of
the cash handed over and no change is worked out** (Hakim, twice). **Rejected:** each part
rounding in the order typed; a required reference.

### D-096 — B7: the tab (le carnet): tenant settings, customers at the till, a limit with its history
**The customer module and the carnet settings are the tenant's**, keys of `system_config`:
`customer_module`, `max_credit_limit`, `credit_overdue_days`, `tab_as_part`. **The rules are
`Tab`** (Domain, Hakim's): the balance is the ledger's sum; repayments pay the oldest charges
first; a charge is refused with no tab, frozen, overdue or past the limit, **and only past the
limit may an owner let one through**, named on the charge. The tab is a tender part: exact,
once per ticket. Every limit change is a `credit_limit_events` row. A customer is created by
rank 2 with a name, a number and the information notice in force. **Every look at or change to
a named customer is a `processing_log` row under their pseudonym.** A cash repayment is a
`paid_in` and a `payment` pointing at it (D-108 for its rounding). **Rejected:** a per-store
switch; warning past the limit instead of refusing.

### D-097 — B8: a cancelled ticket is recorded, not prevented; a PIN only after "Encaisser"
A PIN asked every time is a PIN that gets shared. Every cancel is recorded with a `void`
reason: a `transactions` row `voided`, priced as its sale would be, with no batch, stock
movement, payment, number or outbox row. **The record comes first**: the till lets the ticket
go only once the server has written it. **A manager's PIN is asked only of a cashier
cancelling after the payment panel was opened**: the customer may have paid. Lines struck
before a sale is paid are rows with `removed_at` and `removed_by`, outside every total. **The
owner's flags are `Voids.Flags`** (Hakim's), shown in the Z-report and Admin, never on the
till. Extended by D-106. **Rejected:** a PIN on every cancel; cancelling offline.

### D-098 — B9a: a refund is its own ticket, linked to the sale; the tab first, then cash or credit
A completed transaction with the next number, its lines the sold rows negated on the same
batches, one `returns` row per sold row. **What a line gives back is what it was paid**
(`Refunds.Share`, Hakim's), split over its units with `Allocate`; a weighed line comes back
whole. **Where the money goes:** the tab first, as a negative charge; the rest in cash rounded
once, or store credit. No cap on cash. **Who refunds is the tenant's `refund_min_rank`.**
Restock is the cashier's per line; an expired batch never goes back. The till asks a quote
first. **Rejected:** money back part by part the way it was paid; a PIN on every refund.

### D-099 — B7, B9a: the customer screens at the till
The search and the creation float; **the carnet takes the ticket's place**, and its opening is
logged. The customer key (F5) exists only with the module on. Creation asks a manager's PIN of
a cashier; changing the tab asks the owner's. A refund to store credit on a ticket with no
customer attaches one first. Left out on purpose: how the notice was handed, "numéro déjà
pris", printed receipts (D2).

### D-100 — A customer is found by full name, or by number
The one field takes a name or the 10 digits. **A name is kept narrow** (`CustomerNameSearch`):
a full name only, whole words, **three at most**, and more list nobody and log nobody; the
number is masked in a name's results. The search asks itself 3 s after the last key.
**Rejected:** a search from the first letters, a list of strangers at every key.

### D-101 — B9b: store credit is spent as a part; a refund gives it back as credit; it may expire
A part like a card, once per ticket, for an attached customer. `StoreCredit.Age` and
`MayRedeem` (Hakim's): the balance is the sum of `credit_movements`, the oldest credit is spent
first, and with `credit_expiry_days` credit unspent that long has expired; **an expiry is
written when it is found**. A refund gives back as credit, first, the share the sale paid in
credit, so a refund never turns credit into cash. **Rejected:** a PIN to spend credit.

### D-102 — B10: cash in and out with no sale; the clock is its own key
"Petite caisse" records a `paid_in` or `paid_out` on the cash session with a `cash_movement`
reason, whose `reason_codes.direction` says which way it moves money. **Who takes cash out is
the tenant's `paid_out_min_rank`.** "Pointage": a person types their PIN and clocks in or out
(`shifts`); signing in opens no shift. **Rejected:** shifts tied to sign-in.

---

## 4. Decisions, Block B review (03/10)

### D-103 — A line is priced once, then split over its batches
The scan answers weight × price rounded once; the sale rounded each batch row on its own, so
0,660 kg at 1 285,50 showed 848,43 and charged 848,44, and a card part of the till's total was
refused as more than the ticket. **Every line is now priced once** (`SaleArithmetic.Line`) and
split over its batch rows with `Allocate`, weighted by what each batch gave
(`SaleArithmetic.Split`); the TVA is still each row's own (D-033). A counted line is unchanged:
nothing was rounded. A split weighed row may sit a centime off its own product;
`SaleArithmetic.LineRecomputes` checks such a line whole. **Rejected:** per batch with the scan
splitting too, which still drifts when stock moves between scan and payment.

### D-104 — A ticket's lines keep their place: `transaction_items.line_number`
Ids do not sort within a millisecond, so a past ticket, its refund screen and its receipt read
in any order. Each row carries its line's place from 1; **rows of one line share it**; struck
lines follow the lines sold; rows written before the column hold 0 and read as before.
**Rejected:** ordered ids, an order that stays implicit.

### D-105 — An approval belongs to the till, for the store's day
Tied to the cashier's session, a manager's approval died at "Changer de caissier", and the
ticket on hold could be neither paid nor cancelled. It now belongs to the terminal and stands
**for the store's day it was given on**, midnight to midnight in the store's zone, as a
cashier's view of past tickets does (D-089) and as the till's drafts do (Hakim, 03/10: not a
count of hours); the row still names who typed the PIN. An approval the server no longer holds
(a restart) is refused as `approval_expired`, and the cashier gives it again. **A cancel is
never refused for an approval**: what nobody is on record as having allowed is left off, and
the ticket is recorded at the price in force. **Rejected:** ending with the session; refusing
the change of cashier; a table; 24 hours, which carries an evening's discount into the morning.

### D-106 — A line struck after "Encaisser" needs the cancel's PIN; a ticket struck empty is cancelled
D-097's theft works one line at a time: take the cash, close the panel, strike the lines. **A
cashier striking a line once payment was opened needs a manager's PIN**
(`Voids.StrikeNeedsAuthorisation`), checked by the server, and the row names who allowed it
(`removed_authorised_by`). **A ticket whose every line was struck is recorded as a cancel**,
worth nothing, before the till lets it go: it used to be dropped when the cashier changed.
A lowered count is not covered (see the findings register). **Rejected:** recording only, which
needs the sale to keep when payment was opened.

### D-107 — A refusal a cashier meets is a code; the till owns the sentence
The server's English sentences reached a French or Arabic till as written. The refusals met in
ordinary work carry a `RefusalCodes` code and what it names (a person, an amount as exact
text); `TillText.Refusal` says it in French and Arabic, amounts written the till's way. A
refusal with no code is said as "Refusé par le serveur", the server's words behind it.
**Rejected:** codes for every refusal the till's own screens already prevent.

### D-108 — A tab repaid in cash rounds like a cash sale
A tab is owed to the centime; cash moves in 5 DA steps. **Paying the whole due clears the tab
exactly**; the `paid_in` is the due rounded to the step, and the difference is a
`rounding_variance` row naming the tab's movement (`Tab.Repay`). The whole due is named by its
exact figure or its rounded one. **A part of the due is a multiple of the step.** A due the
step rounds to nothing is cleared with no `paid_in`. **Rejected:** exact repayments, which
leave 1,00 owed, overdue after 30 days.

### D-109 — A manager's PIN opens an earlier day's ticket for a cashier (widens D-089)
A customer returning with yesterday's receipt could not be served without changing cashier.
An earlier day's or another till's ticket asked by someone below rank 2 is answered
`pin_required`; a manager's authorisation for `ViewOtherTickets` opens that ticket, and its
refund cites it. The till forgets it when the ticket closes.

---

## 5. What the Block B review found

Walked every feature against a live StoreServer (about 150 operations, a database audit, 34
parallel requests), every screen at two sizes, and took 28 rules out one at a time.

**Fixed without a decision:** the anonymous basket said `cash` whatever was paid (now the
method, `mixed` or `none`); the server sold a line of 2 147 483 647 units and any typed weight
(the till's ceilings are now the server's, `SaleLimits`); an unknown cash direction was
recorded as cash in; a refused discount showed the exception's own text; the paid screen
called the difference between total and cash "rounding" and every part "Carte"; a refund's and
a repayment's figures had the wrong sign; Échap did not close Brouillons; "Continuer" could be
pushed out of view; the manager step listed every cashier (now those whose PIN would be
accepted); one till had two pad layouts for a PIN; a sale of sixty lines timed out at three
seconds though it was recorded (a sale now waits twenty).

**Design steps taken after the review (03/10):** a narrow till (below 1200 px) gives the ticket
the room the rail can spare, wraps a line's keys and lays the carnet's tiles two by two; a line's
chips go under its name when the name needs the line; tickets on hold are counted on the bar, and
the count brings the oldest back; a panel's first choice is drawn first; a refund in cash says
again what to hand back; "Encaisser" is closed while the server is out of reach; a cancelled ticket
taken back no longer carries its struck lines.

**Left open:** the findings register in `../status.md` §3.

---

## 6. Decisions, Block C (the shift)

### D-110 — A tenant key sets a rank, above the ladder or below it; only where a key exists (C1, 05/10)
D-098's rule was "a setting raises and never lowers". Hakim, 05/10: the owner may hand closing
to a cashier, so a floor is the wrong shape. **A setting now sets**: what is needed is the
setting where there is one, the ladder's rank otherwise (`StaffPermissions.May`, three
arguments). What stops a setting of 1 handing price overrides to every cashier is
**`StaffPermissions.IsConfigurable`**: refunds, paid-outs, reading an X and closing have a
tenant key, and a setting on any other capability throws. `ReadXReport` and `CloseSession` sit
at 2 on the ladder, with `x_report_min_rank` and `close_session_min_rank`. **Rejected:** closing
at 3 on the ladder, which leaves a drawer nobody can close on the owner's evening off; a
`role_permissions` table before H2 needs one.

### D-111 — C1: a till sells only in an open cash session, opened with a counted float and closed with a count
Replaces D-070's session, which the first sale opened with nothing in it and nothing closed.

- **Nothing touches the drawer without an open session.** A sale, a cancel, a refund, cash in or
  out and a tab's repayment are refused as `no_open_session`. Anyone signed in opens, with a
  typed float of zero or more: one figure, not a count by denomination (Hakim, 05/10).
- **What the drawer should hold is `Drawer.Expected`** (Hakim's): float + cash sales − cash
  refunds + paid-in + tab repayments − paid-out − drops + tender rounding. **Only a ticket's
  rounding row is added**: a tab repayment's `paid_in` is the rounded cash already (D-108).
  `cash_sessions.variance` is counted minus expected and nothing else (D-034).
- **The count at close is required; whether it is blind is the tenant's** (`blind_close`).
  Blind, whoever counts is sent neither the expected figure, the variance nor the threshold,
  unless they close alone. The count freezes when confirmed, or the note's question would find
  the drawer's figure in a dozen tries.
- **A variance is recorded, never refused.** Past `variance_alert_value`, short or over, a note
  is asked first, then the PIN. No recount once sent.
- **Who closes is `close_session_min_rank`** (D-110); below it a manager's PIN, and the row
  names both: `closed_by` counted, `closed_authorised_by` allowed it.
- **A closed session is final**: three triggers refuse its update, delete and replace. A till
  has one session open and gives each Z number once, from 1, per terminal (two unique indexes);
  a store's figure for a day is the sum of its tills', a report and not a second number.
- **A session belongs to the store's day it opened on**, and nothing closes it by itself.
- **Tickets are settled first**: no count while a ticket has lines or one is on hold.
- **One session, many cashiers.** Changing cashier opens and closes nothing; the variance is the
  drawer's (Hakim, 05/10: cashiers swap tills too often for a count at each swap).
- A paid-out larger than the drawer should hold is refused (`Drawer.Covers`), naming no figure.

**Rejected:** a handover that closes and reopens; a stored count at each swap; the Z's figures
stored beyond expected, counted and variance; a PIN that could turn a variance down.

### D-112 — C2: an X and a Z are one report, recomputed from the session's rows; a Z is judged by what was in force at its close
`ShiftReport` (Domain, pure) turns a session's tickets, lines and payments into its figures, for
the open session (X) or a close by its number (Z). **Nothing is stored but what the close wrote.**

- **It reconciles twice, and says so when it does not**: the takings by tender come to the
  tickets' totals, and a Z's rows still come to the `expected_cash` stored at its close. A
  report that fails either carries a critical notice.
- **Cash is what the tickets say** (Hakim, 08/10), to the centime; the cash step's rounding is
  the drawer's own line, never a variance (D-034). **Sales and refunds are separate lines**, and
  "Total encaissé" is net of refunds. These two answers replace what the board drew.
- **The thresholds are frozen at the close** (Hakim, 08/10): `void_alert_count`,
  `void_alert_value` and `variance_alert_value` on the session's row. A Z is judged by them for
  good, an X by today's: otherwise raising a threshold un-flags every past Z.
- **Who reads**: whoever reaches `ReadXReport` as the shop set it, or a manager's PIN, which the
  till forgets when the report closes. Nobody else is shown a figure: under a blind close a
  report's cash lines are enough to work the drawer out.
- **The flags are the owner's**: `ReadShiftFlags`, rank 3, no tenant key, since they name
  people. They follow whoever reads; everyone else is told how many there are. Whether a cancel
  is flagged is `Voids.Flags`'s to say. A cancelled ticket has no number: a flag names it by
  who and when.
- **The till lists its last 30 closes**; older ones and a store's day across tills are Admin's.
- **Closed with it**: a cash refund the drawer cannot cover is refused; a refund carries its
  sale's rounding policy, not the day's; a session open past the store's day says so on the bar.

**Rejected:** the Z's figures stored at the close (a second copy to drift from the rows); flags
behind a tenant key; an X shown to a cashier with its drawer section hidden.

### D-118 — C3: a report by cashier is the same report split by who did each thing; it is the owner's, and anyone else reads their own column
`ShiftReport.ByCashierOf` splits a session's rows by person and **applies the session's own
rules to each person's part**, for an X and a Z alike. Nothing is stored and no column was added.

- **Whose is each thing** (Hakim, 11/10): a sale is whoever rang it; a refund whoever refunded;
  a cancel `voided_by`; a struck line `removed_by`; a discount and an override the cashier who
  rang the ticket, never the person whose PIN allowed them; cash in, cash out and a tab repaid
  the movement's `staff_id`. `CompleteSale` writes `removed_by` as the ticket's own cashier, so a
  strike is on its ticket's column in practice.
- **Everyone who did anything has a column**, the opening and the count at the close included,
  in the order of their first operation, even when they are one.
- **No cash a person "should hold", no variance per person**: nobody counted at the swap (D-111).
- **A tender's line is its sales less its refunds**, so the lines come to "Total encaissé".
- **The hours are a person's first and last recorded operation**, named so: sign-ins are not
  kept (D-083).
- **It reconciles a third time**: the people come to the session, line by line
  (`SharesReconcile`). The session's column is the report's own figures, never the columns added up.
- **Who reads** (Hakim, 11/10): the flags' rule, `ReadShiftFlags`. Everyone who reads the report
  is told how many people there are and is sent their own column (the board); the rest is one
  closed frame, opened by the owner's PIN, forgotten when the report closes
  (`ShiftReportEndpoints.WhoReads`). The board's "PIN responsable" reads "PIN propriétaire".

**Rejected:** the section open to whoever reads the report (a manager would read cancels per
person, the flags' substance); sales to managers and cancels to the owner (two rules for one
table); the session's column summed from the people's (it would prove nothing).

---

## 7. Decisions for the DPIA, taken during Block C (10/10)

### D-113 — A customer is created only once their age is confirmed; a minor is enrolled on their legal representative's consent
Whoever creates a customer confirms the person is 18 or older, or is a minor whose parent has
read the notice and agreed. The till asks it on the creation panel; the server refuses a
creation that says neither (`age_not_confirmed`) and reads anything unknown as nothing said.

- **An adult leaves no trace of it**: nothing on `customers`, nothing in `processing_log`.
- **A minor is enrolled with one `consent_events` row**: processing, granted, verbal, for the
  information notice, **`given_by` = `legal_representative`** (a new column, `subject` by
  default), in the customer's own unit of work. Marketing consent is never taken here.
- **That row implies the person is a minor, and that is accepted** (Hakim, 10/10). Nothing else
  says it: no flag, no date of birth, no log entry.

**Rejected:** a date of birth; a flag on the customer; nothing recorded for a minor (the DPIA
needs the representative's consent evidenced); `legal_representative` as a `method`.

### D-114 — Waymark keeps its own logbook in the cloud; a technician's actions also go in the store's (A-10)
Loi 25-11 art. 41 bis 3 requires a logbook from the processor too. `processor_log` in the cloud
records every operation Waymark performs, under D-045's rules. A technician's actions on a till
go to the store's `processing_log` with `actor_type = 'processor'`, so the retailer sees them;
a session summary with no subjects goes to `processor_log`. **Rejected:** cloud only; store
only; mirroring each action with its subject. Tests owed: H23.

### D-115 — The logbook's operation list is art. 41 bis 3's (A-11)
Migration `LogbookOperations`, which rebuilt `processing_log`, `erasure_ledger` and
`retention_policies` once each while no store is live.

- `processing_log.operation` adds `recording`, `organisation`, `storage`, `extraction`, `use`,
  `dissemination`, `alignment`, `interconnection`, `locking`, `encryption`, `destruction`.
  `modification` stands for adaptation or alteration, `disclosure` for making available. The
  trigger for each value is DPIA v2 §2.2.1.
- `actor_type` adds `processor`, held to D-061 as staff are. `subject_type` adds `supplier`,
  there and on `erasure_ledger`.
- **Of the new operations, `extraction`, `dissemination`, `locking` and `destruction` name the
  one person they are about** (Hakim, 10/10); the other seven may run over many.
  `ProcessingEventTests` lists every operation on one side or the other.
- **A bulk operation is one row that names no one, with `subject_count`**: nullable, above zero,
  only where `subject_id` is null. A bulk `destruction` is a system task's.
- `retention_policies.entity_type` adds `receivable`, `supplier_contact`,
  `data_subject_request`, `tier2_record`. A `receivable` policy can only unlink or archive: the
  ledger refuses a delete. Cloud-only data gets its retention in the cloud's own policy.
- The rollup's counters table already existed on the store's side (`processing_counters`).

**Rejected:** adding only the operations that occur today; a mapping with no migration. H24 is
`ProcessingEventTests` and five tests in `ProcessingLogWriterTests`.

### D-116 — The operational channel carries no personal data
Outbox channel `B_operational` carries no customer, staff or supplier-contact field. `D_decisions`
is to be checked too: `recommendation_decisions.decided_by` is a staff id. Tests owed: H21.

### D-117 — Marketing messages, SMS included (Phase 2)
Loi 18-07 art. 37: SMS is a "similar technology", so every marketing message needs prior
marketing consent; each names the sender and carries a free opt-out, and an opt-out is a
withdrawal event. The SMS gateway is a subprocessor located in Algeria. Tests owed: H22.

---

## 8. What the Block C review found (12/10)

Read the Build Plan, the operations catalogue and D-110 to D-118 against the code; ran thirteen
extreme cases through the real handlers on a real database; then drove a live encrypted StoreServer
on a freshly generated store, about 180 requests as two cashiers and the owner: openings, sales,
cancels, a refund, cash in and out, a probed blind close, twenty sales and closes at once, the
largest float the wire takes, and 45 of the generator's 365 closes read back by today's rules.

**What held.** Every report reconciled its three ways, the generator's year included, checked
against a recomputation written apart. A refund in a later session left the earlier Z as it was. A
ticket worth nothing, one struck empty and one whose cash rounds to nothing kept the report in
balance. Eighteen sales and three closes sent together gave one close, one Z number and no error.
Each till numbers its own closes. The threshold is exclusive at its edge on both sides.

**Fixed without a decision.** A session's tickets were read as a list of ids, one SQL variable
each: past 32 766 tickets the X, a paid-out and **the close itself** failed, and nothing closes a
session by itself, so a drawer left open for weeks could never be closed again. The ledger now reads
them as subqueries (and is about twice as fast on a session of 2 000 tickets). A close whose answer was lost and
which the cashier sent again left its count panel over a drawer already shut: the till now reads the
drawer again and shows the opening panel, which names the last Z.

**Kept as tests** (`CompleteSaleTests.ShiftReview.cs`): the long session; the refund in a later
session; the odd tickets; and people ordered by the hour through a day and a night, since every
other integration test runs at one instant.

**Decided the same day** (D-119 to D-121, in `../decisions.md`): the server keeps a blind count,
which stands until cash moves; a close is whole;
a blind close with the reports handed below it is refused; a clock set back is said for what it is;
a note is 500 characters at most. Left as they are: a Z names who validated, not who counted, and a
PIN typed to read a report stands for the day. Admin's, as H3: printing a Z and reading an old one.

**Left open:** `../status.md` §3 (F-37 as reduced, F-52 to F-54) and O-38, the DPIA and the
figures a Z shows about named staff, marked high priority.
