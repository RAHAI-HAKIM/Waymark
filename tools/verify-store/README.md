# verify-store

An independent check of a generated store (Phase 0 final test). It recomputes every figure
from the raw rows, in Python, without the C# code that wrote them, so a shared mistake cannot
pass both.

```bash
python tools/verify-store/verify_store.py <run directory>
```

To also compare two runs table by table, name a second run, and optionally the tables that
may differ (the connectivity profile changes only the outbox):

```bash
python tools/verify-store/verify_store.py <run A> <run B> --except outbox,sync_state
```

It prints one line per check, failures first, and exits 1 on any failure.

## What it checks (39 checks)

| Area | Checks |
| :---- | :---- |
| File | `integrity_check`; no dangling foreign key |
| Lines | line total = round(price × quantity) − discount; TVA by subtraction from the net (D-033), under the transaction's own policy |
| Transactions | header totals are the sums of their lines; payments pay exactly the total, voids nothing; payment sequences 1..n |
| Cash | tender variance = nearest-500 rounding of the cash payment, ties away (D-034); expected cash = float + cash + tender variance + paid-in − paid-out − drops; variance = miscount |
| Invoices | gapless and unique per fiscal year; on every sale and refund, never on a void; the year is the year of issue |
| Stock | inventories = the sum of each batch's movements; no batch below zero at any point in id order; one sale movement per sold line; nothing sold past expiry or before arrival; batch status agrees with what is left |
| Ledgers | store credit chains exactly and never goes negative, and `customers.credit` caches it; a tab stays within [0, credit limit]; charges = on-account payments (D-055) |
| Returns | never more than was sold; always after the sale |
| Time | every id carries its row's time; every sale inside the run window |
| Outbox | the queue is exactly `last_acked + 1 .. last_sequence`; one basket per sale |
| Outputs | `latent-demand.csv` agrees with the till per variant per day; manifest row counts match |

Dates in the stock and CSV checks are UTC dates, which equal the store's local dates while
the store (UTC+1) closes before 23:00.
