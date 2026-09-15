# Waymark.Generator: the synthetic store

A console tool that builds a year (or any length) of plausible trading for a fake Algerian
épicerie. The data goes into a real `waymark-store.db`, created and migrated exactly the way
a till creates its own. Everything that needs data before real shops exist starts here:
- Phase 1 screens;
- engine statistics and backtests;
- sync testing;
- the demo.

- **Spec:** `docs/decisions.md` D-046 (what it must do) and D-054 (how it was built, and every choice with its rejected alternative).
- **Status:** complete for Phase 0 (W10, steps S0–S10). The recap is `docs/recaps/phase-0.md`.

---

## 1. Run it

```bash
dotnet run --project src/Waymark.Generator -- --config src/Waymark.Generator/inputs/configs/grocery-dz.json --out artifacts/generated/seed-42
```

| Option | Meaning |
| :---- | :---- |
| `--config <file>` | The behaviour configuration. Required |
| `--out <dir>` | Output directory. Refused if it already holds a `waymark-store.db`: a run never overwrites a store |
| `--seed <n>` | Overrides `run.master_seed`. Same seed, same store |
| `--days <n>` | Overrides `run.history_months`. Short runs for quick looks and tests |
| `--connectivity <p>` | `always_on`, `flaky` or `offline_stretch`, overriding `connectivity.profile` |

A full grocery year takes about 70 s on the development laptop. About 90% of that is
`SaveChanges`.

Every input is validated before anything is written, and every problem is reported at once
with its file and line. **If a run fails, nothing is left behind.**

## 2. What it writes

| File | What it is | Loaded into the store? |
| :---- | :---- | :---- |
| `waymark-store.db` | The store: commissioning, a year of trading, supply, the mess, the outbox. Foreign keys, CHECKs and the 14 append-only triggers were live throughout | It *is* the store |
| `latent-demand.csv` | One row per variant per day: `store_open, on_hand_open, latent, sold, substituted, lost, sold_as_substitute, on_hand_close` | **Never.** Ground truth for evaluating the engine; a store only knows what it sold |
| `report.md` | The distributions a reviewer reads instead of the code: hours, weekdays, basket sizes, Ramadan and pay cycle, service level, spoilage, lead times, payments, the till, the outbox | No |
| `manifest.json` | Seed, config and catalogue hashes, window, store id, parameter sources (guess/literature/interview counts), row counts, connectivity profile, daily outbox backlog, hashes of the CSV and report | No |

**Opening a generated store in StoreServer** (tested on a full year; work on a copy, since
StoreServer re-applies its startup migration and triggers):

```bash
Waymark__Storage__DataDirectory=<dir> Waymark__Store__StoreId=<manifest store_id> Waymark__Store__Currency=DZD dotnet run --project src/Waymark.StoreServer --no-launch-profile --urls http://127.0.0.1:5199
```

Then `GET /health` returns `"status": "up"`.

---

## 3. How a run works

```
inputs ─► validate ─► commission ─► opening stock ─► day loop × N days ─► final state ─► report + manifest
```

1. **Load and validate** (`Configuration/`, `Catalogues/`). The config and the six catalogue
   files are read strictly: unknown fields, missing fields and bad references are errors.
   Nothing touches the disk until every check passes.
2. **Commission** (`Simulation/ReferenceData.cs`), at 08:00 on the commissioning date:
   - the store, roles, staff and tills;
   - categories and subcategories (each subcategory carries the VAT class);
   - suppliers, products, variants, supplier terms and every price row;
   - customers enrolling over the commissioning weeks, with their consent events.
3. **Opening stock** (`OpeningStock.cs`): one batch per product on the first morning, in
   whole cartons, capped at the batch's remaining shelf life.
4. **The day loop** (`DayLoop.cs`), one store-local day at a time. Each day:
   - **latent demand** per variant: `Poisson(base_rate × weekday × month × Ramadan phase × events × pay cycle)`, a pure function of the seed, the variant and the day (`DemandModel.cs`);
   - **baskets** cut from that demand, with arrival times inside opening hours, a till, maybe a regular, a tender (`BasketAssembler.cs`);
   - then **every event in time order**, with the simulated clock advanced to each so ids and timestamps carry the moment:

   | Event | Class | What happens |
   | :---- | :---- | :---- |
   | Cycle count (before opening) | `StockCounter` | One subcategory counted in turn; shrinkage found and posted |
   | Expiry write-off | `SupplyChain` | Lots past their last day of sale come off the shelf |
   | Drawer opens, shifts start | `CashDrawer`, `DayLoop` | Float, rota (staff take opening intervals in turn) |
   | Paid-in | `CashDrawer` | Occasional float top-up |
   | Delivery | `SupplyChain` | Order received (maybe short), one batch per product, receipt movements |
   | Supplier visit | `SupplyChain` + `NaiveShopkeeperPolicy` | The shopkeeper orders, on the supplier's delivery days only |
   | Customer | `DayLoop` + `SaleWriter` | FIFO from sellable lots, substitution if out, maybe a mis-ring and void, discounts, split payment (store credit, on account, tender), tender rounding, returns scheduled, anonymous basket emitted |
   | Return | `SaleWriter` | Refund transaction, `returns` row, `return_in` if restocked, store credit if refunded that way |
   | Paid-out, midday drop | `CashDrawer` | Cash out of the drawer |
   | Drain | `Outbox` | Sends the outbox if the profile's link is up |
   | Drawer closes | `CashDrawer` | `expected = float + cash + paid_in − paid_out − drops + tender_variance`; sometimes a miscount |

   - **One `SaveChanges` per day.** A generator bug fails on the day it happens, at insert, rather than producing plausible wrong data.
5. **Final state** (`FinalState.cs`, `SupplyChain.WritePending`, `Outbox.WriteFinal`): rows
   that describe a state rather than an event are written once the run is over:
   - `inventories`;
   - batch statuses (`depleted`, `written_off`);
   - customer credit and last-order caches;
   - purchase orders still on their way;
   - the queued outbox and `sync_state`.
6. **Outputs** (`Reporting/`): the report is read back from the finished database and the
   CSV; the manifest fingerprints everything.

### The rules that make it trustworthy

- **Addressed randomness, never a shared generator** (`Randomness/`).
  - Every random value is `Stream(name).Uniform(coordinates…)`, a hash of the seed, the stream name and the coordinates. `demand(variant 17, day 42)` is the same number whatever else the run did.
  - Changing the ordering rule cannot move the demand it is judged against, and a connectivity profile cannot move a sale.
  - Current streams: `arrival basket_mix basket_size cash count customer_attach customer_enrolment customer_frequency customer_traits delivered_shelf_life delivery_time demand discount fill lead link opening_cover opening_shelf_life payment payment_mix return return_visit substitution terminal void`, plus `DeriveSeed("ids")` for ULIDs. **Adding a behaviour means adding a new stream name**; reusing one couples two behaviours.
- **The real arithmetic.** Money goes through `Money`, `SplitTaxInclusive`, `ToCashTender`
  and the store's rounding policy stamped on each transaction (D-031–D-034, D-053). Stock
  goes through `Quantity`/`QuantityDelta`. A generated receipt recomputes like a real one.
- **The simulated clock only moves forward**, and `SeededIdGenerator` stamps ULIDs with it,
  so ids sort in business time.
- **Determinism is a canonical dump** (tables by name, rows by key), never file bytes. The
  same seed gives the same dump, the same CSV bytes and the same report bytes.
- **No stock below zero, no expired unit sold, no sale outside opening hours.** Each is a
  test.

---

## 4. Inputs: what a store *is* and how it *behaves*

### 4.1 The catalogue: `inputs/catalogues/<name>/` (what it is)

| File | Holds |
| :---- | :---- |
| `store.json` | Code, name, type, currency, **rounding policy**, UTC offset, tills, roles, staff, **opening hours per day type** (`normal`, `friday`, `ramadan`, `aid`), reason codes, notices |
| `units.csv` | Units of measure (the generator sells whole units only) |
| `categories.csv` | Category → subcategory, **VAT class** (`vat_bp`), sensitive flag |
| `suppliers.csv` | Supplier code, **delivery weekdays** (`sun\|wed`), stated lead time, payment terms |
| `catalogue.csv` | One row per variant: SKU, EAN-13 (prefix 200), category, product, variant, prices at commissioning, supplier, carton size, shelf life, **base daily rate**, seasonality profile, substitutability tier (1–3) |
| `price_changes.csv` | Dated retail and purchase price changes |

`grocery-dz` is 399 real Algerian products (Hakim's `docs/products.csv`). The other columns
were added by `tools/enrich-catalogue/enrich.py` from written family rules. Its README and
the catalogue's README explain every column. **Edit the CSVs directly now.** Re-running the
script overwrites hand edits, although today it reproduces the files exactly.

**A second catalogue needs no code change.** The test fixture
`src/tests/Waymark.Generator.Tests/Fixtures/mini/` (a hardware shop, `half_even`, closed on
Fridays, prices off the cash step) is the proof and the template.

### 4.2 The configuration: `inputs/configs/<name>.json` (how it behaves)

**Every behavioural number is `{ "value", "source", "note" }`.** `source` is `guess`,
`literature` or `interview`, and the note says why. A parameter without all three is
refused, and so is a misspelt field. The files are plain JSON. The loader tolerates
comments, but editors flag them, so they were removed (15/09). The reasoning lives in each
parameter's `note`.

| Section | Parameters (grocery-dz value) | Drives |
| :---- | :---- | :---- |
| `run` | `master_seed` 42 · `start_date` 2025-01-01 · `history_months` 12 · `commissioning_days_before_start` 45 | The window |
| `customers` | `initial_count` 120 · `marketing_consent_share` 0.35 · `objection_share` 0.03 · `french_language_share` 0.2 | The customer base and consent |
| `opening_stock` | `cover_days` 7–21 · `remaining_shelf_life` 0.5–0.95 | Day-one shelf |
| `calendar` | `weekday_factors` · `hourly_traffic` per day type · `ramadan` periods (1446, 1447) · `stock_up_days` 7 · `post_aid_quiet_days` 7 · `events` (Aïd al-Adha, rentrée) · `payday` spike/pre-payday/trough and their effects on demand, basket size, on-account | When and how much |
| `sales` | `basket_units` (1–50, mean 7.2) · `substitution_by_tier` 0.6/0.3/0.05 · `payment_shares` cash 0.95 card 0.04 wallet 0.01 · `customer_attach_share` 0.18 · `opening_float` 5000 | The till |
| `supply` | `memory_days` 14 · `reorder_cover_days` 4 · `order_up_to_cover_days` 12 · `slow_mover_rate` 0.3 · `ramadan_over_order` 1.5 · `ramadan_lookahead_days` 14 · `late_delivery_max_days` 2 · `fill_rate` 0.92 · `short_delivery_fraction` 0–0.8 · `delivered_shelf_life` 0.8–1.0 · `visit_minutes_after_opening` 90 · `delivery_window_minutes` 180 | The naive shopkeeper and his suppliers |
| `mess` | discounts (`discount_line_share` 0.02, 5–20%) · `void_share` 0.01 · returns (`return_line_share` 0.003, 1–5 days, `restock_share` 0.4) · store credit (`store_credit_refund_share` 0.5, `store_credit_use_share` 0.8) · `on_account_share` 0.15 · cash (`paid_out_daily_chance` 0.25, `paid_in_daily_chance` 0.05, `drop_threshold` 20000, `cash_count_discrepancy_chance` 0.08 up to 300) · counts (`count_interval_days` 7, `count_shrinkage_chance` 0.08, `count_found_chance` 0.02) · `reason_codes` per event kind (names from store.json) | What makes real data untidy |
| `connectivity` | `profile` · `drain_interval_minutes` 3 · `batch_size` 500 · `flaky_link_up_share` 0.7 · `offline_start_day` 150 · `offline_days` 21 | Outbox and `sync_state` only |
| `seasonality_profiles` | 23 named profiles: 12 monthly multipliers, 4 Ramadan-phase multipliers, event multipliers | Named in `catalogue.csv` |

### 4.3 Changing things: where to turn which knob

| You want… | Change | Note |
| :---- | :---- | :---- |
| More or fewer customers a day | `base_daily_rate` in `catalogue.csv`, or `sales.basket_units` | **Footfall = demand ÷ basket size.** It is not a parameter, so it can never disagree with demand. Today ~420 units ÷ 7.2 ≈ 58 baskets a day |
| A different seasonal shape for a product | Its `seasonality_profile`, or the profile's multipliers | Profiles are shared by name |
| Next year's Ramadan or Aïd | `calendar.ramadan`, `calendar.events` | Validation refuses a run reaching a Ramadan it does not know |
| A worse or better shopkeeper | `supply.*` | `NaiveShopkeeperPolicy` is the **baseline every engine claim is measured against**; change it knowingly |
| More stockouts | Lower `fill_rate`, higher `late_delivery_max_days`, lower `order_up_to_cover_days` | Visible in the report's "Demand against the shelf" |
| Test sync under an outage | `--connectivity offline_stretch` | Sales stay identical to the always-on run |
| Cash rounding | Prices off the 5 DZD step in `catalogue.csv`, or discounts | A quarter of grocery prices already are |
| A different kind of shop | A new catalogue directory and config | Copy the mini fixture |
| A new behaviour | Code in `Simulation/`, **a new random stream**, a new config section of sourced parameters, a validator rule, an invariant test proven to fail (D-012), a report row | And a D-054 paragraph |

After any change, read the new `report.md` before trusting the store.

---

## 5. Using it in later phases

- **Phase 0.5 and 1, screens and handlers.** A generated store is realistic fixture data for:
  - the POS product lookup and cart (barcodes, prices valid by date, VAT per subcategory);
  - Local Admin lists (stock by batch with expiry, purchase orders, cash sessions, returns, customers with consent);
  - the `CompleteSale` handler, whose output can be compared with what `SaleWriter` writes, since both must satisfy the same invariants.
- **Phase 0.5, sync.** The outbox holds real `anonymous_basket` payloads with gapless
  sequences. `offline_stretch` gives a stub cloud three weeks of backlog to drain.
- **Phase 2, the engine.**
  - Tier 1 is `waymark-store.db`.
  - `latent-demand.csv` is the answer key: censored demand, stockout days, substitution.
  - `NaiveShopkeeperPolicy` is the baseline an ordering recommendation must beat.
  - Several seeds give several stores for backtests and interval coverage.
- **Tests anywhere.** `GeneratorRun.Execute` is callable in-process (the generator tests do
  this); use short `--days` and the mini catalogue for speed.
- **The demo.** A full year from `grocery-dz`, opened in StoreServer.

### Known limits (deliberate, or deferred)

| Limit | Why | Where it is tracked |
| :---- | :---- | :---- |
| No `processing_log` rows | The generator is not an application access site | D-054 |
| No customer period records in the outbox | No spend-banding scheme yet; the generator may not compute pseudonyms | D-049, D-054 |
| On-account tabs are never settled | The schema has nowhere to record repayment | F-16 |
| A return and its refund transaction are not directly linked | `returns` has no refund column | F-17 |
| The database is plaintext | The database key is not implemented | F-1, O-20 |
| Whole units only, one selling unit per variant, no promotions, no loyalty points, no inbox or intents, no drain backoff, default fiscal year | Not needed for Phase 0's exit; each would be a new behaviour (§4.3) | — |
| Every behavioural number is a guess or literature | Two afternoons with épiciers turn a dozen into interviews | D-046, manifest `parameter_sources` |

## 6. Where the code is

| Folder | Holds |
| :---- | :---- |
| `Program.cs`, `GeneratorArguments.cs`, `GeneratorRun.cs` | CLI, one run start to finish, the manifest |
| `Configuration/` | Config records, the strict JSON loader, `Sourced<T>`, validation |
| `Catalogues/` | The six-file catalogue, CSV reader, validation, EAN-13 |
| `Randomness/` | `RandomSource` (addressed streams), distributions |
| `Calendar/` | `SimulatedClock`, `CalendarModel` (day types, Ramadan phases, pay cycle, opening intervals) |
| `Simulation/` | Commissioning, demand, baskets, the day loop and everything it calls |
| `Writing/` | `StoreDatabase` (created like a till's), `CanonicalDump` |
| `Reporting/` | `latent-demand.csv`, `report.md` |
| `inputs/` | Catalogues and configs, copied beside the build |
| `src/tests/Waymark.Generator.Tests/` | 215 tests: inputs, randomness, calendar, commissioning, sales, supply, the mess, connectivity, report. Every invariant was proven to fail by breaking its code |

Architecture (enforced by tests): nothing that ships references the generator. It reaches
none of Pseudonymisation, Sync, Hardware or the hosts, and uses no cryptography (CLAUDE.md
§2.1).
