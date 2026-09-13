# Waymark — System architecture

What the parts are and what they do: the operations catalogue, the statistics module, the
engine departments and the Integration Layer. Owner: Hakim. Compacted 13/09/2026; the
Stage 2 module sketch and the DB_design_v5 ERD were removed (the schema is
`src/Waymark.Persistence/schema_current.sql`, and the pictures are `diagrams/`).

---

## 1. The modules

| Module | Job |
| :---- | :---- |
| **Operational** (§2) | Everything POS and Admin do outside the engine's reasoning. The source of truth |
| **Statistics** (§3) | Fed by every transaction and decision. Owns the tier 1→2 pseudonymisation boundary and feeds the engine |
| **Almanac engine** (§4) | Five departments producing recommendations, reports and notifications. Sees pseudonyms only |
| **Integration Layer** (§5) | Takes department output, gates it by role, delivers or queues it, and writes decisions back |

Two apps sit on top. The **POS** runs at every checkout: cashiers log in by role, and
engine notifications show only to someone entitled to act. **Admin** is where the
retailer manages everything and decides on recommendations.

---

## 2. Operations catalogue

★ = a Waymark addition beyond checkout-only software. The role in brackets is who may do
it by default; *R* = retailer, *M* = manager.

**A. POS / checkout (cashier)**
- Scan or search, add to cart. Adjust quantity or remove a line before completion
- Weighted/PLU entry (manual weight until scale integration)
- Discount per line or per transaction, with a reason code [R/M unless pre-decided]
- Price override behind manager PIN [R/M] ★
- Attach customer by phone or card lookup
- Split payment across tenders; cash with change, card, mobile wallet
- Receipt print (digital later); reprint a past receipt
- Void a line or the whole transaction before finalisation
- **No-sale drawer open, logged: the shrinkage-audit point** [R/M unless granted]
- Park and resume a transaction
- Return or refund linked to the original transaction [R/M unless policy allows]
- Store credit [R/M]; paid-in / paid-out petty cash [R/M]
- Clock in/out at the terminal ★
- Engine notifications at checkout, permission-gated ★
- Stock lookup mid-sale

**B. Shift and end of day [R].** X-report (read-only mid-shift); Z-report (gross by tender,
count, voids, no-sales, discounts, overrides, cash variance); float at start and counted
total at end, with variance flagged; per-cashier handover. This is the main internal
shrinkage control, and skipping it would be a regression for retailers.

**C. Catalogue.** Add or edit products and variants, by hand, from CSV, or by scanning
against a seed catalogue ★ [R/M]. Category hierarchy [R/M]; bulk price update [R]; shelf
labels [R/M]; discontinue or archive [R]; bundles [R].

**D. Inventory [R/M].** Receive against a PO, creating batch and inventory rows; manual
adjustment with reason (damage, loss, correction); physical and cycle counts; stock by
store, variant and batch; flag near-expiry batches ★ [R]. Stock transfer between stores is
*deferred*.

**E. Customers.** Profile with privacy settings; loyalty accrual and redemption (manual);
purchase history [R/M]; manual tier override [R/M]; customer-specific discount [R].
Compliance duties, from the first record: notice, two consents, objection, rights (Build
Plan Phase 1).

**F. Staff [R].** Accounts, roles and PINs; clock in/out and basic scheduling; sales per
staff member ★; deactivate or terminate.

**G. Suppliers and purchasing [R/M].** Supplier record and terms (Supplier-Variant); create
a PO by hand or from a recommendation ★; PO status (pending, partial, received); delivery
discrepancies.

**H. Promotions and pricing [R].** Promotion scoped to product or variant with a validity
window; activate, deactivate, schedule; price history ★.

**I. Returns (admin).** Approve returns above a threshold; return-reason analytics [R/M].

**J. Store administration [R].** Store profile; terminals; roles and permissions;
multi-store *viewing* (transfers deferred).

**K. Engine interaction ★ [R].** Recommendation feed by department; accept (triggers the
real action, e.g. a PO), adjust, or dismiss; explanation view; snooze. This is the headline
difference from checkout-only software.

**L. Social and e-commerce ★.** Flag an order as social-sourced; track that volume [R/M].

---

## 3. Statistics module

Fed by the operational module: every transaction and every retailer or engine decision.

| Tier | Holds | Where |
| :---- | :---- | :---- |
| 1, raw events | Real identifiers | `waymark-store.db`, local, never syncs |
| 2, computed cache | **Pseudonymised** at transaction grain. The boundary is tier 1→2 | Local DuckDB (D-043) |
| 3, consumption | What the engine reads, derived from the two D-043 outbox streams | Cloud DuckDB per tenant |

The pseudonym is `HMAC-SHA256(tenant_key, "waymark:<population>:v1:" ‖ id)` truncated to
128 bits, with no mapping table (D-039). About 4 statistics reach the dashboard; the rest
feed departments and reports.

**The statistics list** (suggested, 30):
1. Online/social sales share
2. Customer behaviour: frequency, basket, preferred categories, spend pattern,
   inter-purchase time
3. New customer acquisition
4. New vs returning
5. One-time customers
6. RFM
7. Profitability: COGS by product, category, store, period
8. Demand tracking
9. Gross profit breakdown
10. Net sales after returns and discounts
11. Net payments by method
12. Sales reversals
13. ABC classification
14. Inventory adjustments: count, qty, value, reasons
15. Units sold
16. Transfers *(deferred)*
17. Inventory snapshot at a date
18. Inventory valuation
19. Days of inventory remaining *(a prediction)*
20. Sell-through
21. Turnover
22. SKU analysis: active, new, inactive
23. Basket analysis / co-purchase
24. Average order quantity
25. Order volume over time
26. Returns: qty, value, rate, reasons
27. Promotion performance: before, during, after, incremental
28. Sales performance by product, variant, category, supplier, employee, store, time
29. Supplier performance
30. Bundle performance

---

## 4. Almanac engine

Output reaches the retailer in three places: a department section (reports, analysis,
decisions), dashboard tiles ("Product A is underperforming"), and real-time notifications
("B just crossed its reorder point"). Each links through to the detailed report.

### 4.1 Inventory
Continuously tracks stock, value, incoming, outgoing, damaged or lost, adjustments and
days remaining.

- **Group 1, stock health:** current stock and value by variant, store and batch; incoming
  from open POs; days of inventory remaining (the one prediction, using the demand rate);
  the adjustment feed.
- **Group 2, replenishment:** reorder point, safety stock, order quantity, timing, expected
  stockout date. **Accept creates a PO**, handing off to Supply for supplier choice. The
  action is `binary`.
- **Group 3, risk and waste:** overstock, dead stock and slow movers, declining products
  (from Sales & Demand trend), near-expiry detection. The **near-expiry clearance
  strategy** evaluates a sequential 3-stage markdown and outputs the discount path that
  maximises expected recovery. The action is `menu`: pick a stage or dismiss.

### 4.2 Sales & Demand
Rolling demand forecasts by product and category with intervals; variance analysis
explaining misses; anomaly detection with an attempted explanation; product performance
(growth, decline, contribution, margin, turnover, trend, customer penetration) with
conclusions. Mostly shown, not actioned.

### 4.3 Customer (entitlement-gated)
Segmentation (RFM, frequency, spend, categories, basket composition, recency), churn and
inactivity, CLV. Personalised suggestions, such as a bundle or promotion for a customer,
reach the checkout only after retailer approval. Hard rules: **non-sensitive categories
only**, evaluated across all category links; **credit and tier outputs are informational,
never actionable**; all customer-facing output is approved by the retailer.

### 4.4 Planning (mostly on demand)
1. **Order planning:** budget-constrained multi-product ordering (budget, forecast, cost,
   expiry)
2. **Promotion and price sensitivity sandbox:** best, worst and expected profit before a
   campaign
3. **New-product EVSI pilot tester:** order store-wide, skip, or run a 2-week pilot
4. **Risk and cash-flow simulator:** 1 000 simulated months of stockouts, holding cost and
   margin
5. **Knapsack/MIP allocation:** scales reorders back to budget or shelf volume
6. **Multi-period batcher:** Wagner-Whitin, non-perishables only

### 4.5 Supply
Tracks average lead time and its variability, on-time delivery, fulfilment rate, quantity
discrepancies, purchase price and its evolution, availability. From these: supplier
recommendation, comparison and risk. **Supplier selection under risk** infers the
retailer's risk aversion from which suppliers they actually pick, rather than asking.

### 4.6 How departments operate

| Department | Trigger | Feedback loop | Cold start |
| :---- | :---- | :---- | :---- |
| Inventory | Transactions, plus daily or weekly | Revealed preference: consistently ordering below G2 softens the safety margin; ignoring G3 flags for a category retunes its markdown threshold | G1 works day one. G2 falls back to deterministic thresholds. G3 overstock and decline need history. **Expiry needs only dates: day one** |
| Sales & Demand | Daily | None beyond display; the retailer decides | Deterministic heuristics until real history |
| Customer | Transactions, plus daily or weekly per statistic | None | Static rules that need no data |
| Planning | On demand | Decisions inform later runs | Disabled or simple estimates without history |
| Supply | On PO creation (supplier sort); reports | Observed preferences join the ranking | A plain list, unless price clearly differs |

### 4.7 Cold-start reference: the parameter registry seed
Each row becomes a registry parameter with `version` and `computed_at` (channel F,
`sync-design.md` §8).

| Parameter | Default | Used by |
| :---- | :---- | :---- |
| Stockout rate baseline | 8–12% | Inventory G2, risk simulator |
| Overall shrinkage | 2–3% of revenue | Risk simulator |
| Perishable-category waste | 4–8% (by category) | Inventory G3 |
| Safety-stock service level | 90% blended; ABC-tiered when available (A 95%+, B 80–90%, C 70–80%) | Inventory G2 |
| z-score | 1.28 (90%), 1.65 (95%), 2.33 (99%) | Inventory G2 |
| Near-expiry markdown path | 20–25% / 40–50% / 60–70% | Near-expiry clearance |
| Lead-time uncertainty | Wide and conservative until ~5 POs complete | Supply, EVSI, safety stock |
| Supplier ranking weight | 50/50 price/reliability until revealed preference exists | Supplier selection |

---

## 5. Integration Layer

Every department's output passes through it. The shape is the recommendation envelope
(D-044): `{department, explanation, urgency, action_type: binary | menu, options[],
source}` plus type, Because factors and executable option payloads.

- **Role gating.** If nobody entitled is present, the output waits in the **pending
  queue** and surfaces when a privileged user logs in.
- **Decisions.** Accept, adjust or dismiss is written back to the statistics module (the
  decision is itself a signal) and, on approval, becomes an Application command that edits
  the operational DB, carrying the department as its source.
- **Cloud half:** emits pseudonym-keyed recommendations.
- **Store half:** resolves a pseudonym to a customer **only at delivery** to POS or Admin;
  writes `processing_log` for every resolution; checks `objection_flag` before any
  customer-directed output.

State machines: `diagrams/08-state-lifecycles.md`.

---

## 6. Data placement and global readiness

Placement is drawn in `diagrams/02-container.md` and `04-data-flow.md`. **The store**
holds the full operational DB, direct identifiers, the keys, consent records, and tiers 1
and 2. **The Algerian cloud** holds tier 3, the D-043 streams, non-personal operational
data (products, batches, suppliers, POs), the engine and backups. Nothing carrying a direct
identifier crosses.

Global-readiness rules, which are cheap now and expensive later:
- no hardcoded region or residency assumptions;
- retention periods as configuration;
- consent notice text and versions as data;
- legal basis as a field;
- no cross-region data paths, analytics included;
- the payment provider as a plug-in, with Waymark owning subscription state, entitlements
  and invoices.

Anything beyond these is premature (Operating Rules).
