"""Independent checks of a generated store: every figure recomputed from raw rows.

Usage: python verify_store.py <run directory> [<run directory to compare> [--except t1,t2]]
Prints one line per check, FAIL lines first, and exits 1 on any failure.
"""
import calendar
import csv
import json
import sqlite3
import sys
from collections import defaultdict
from datetime import datetime

CROCKFORD = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
results = []


def check(name, ok, detail=""):
    results.append((ok, name, detail))


def rnd(num, den, policy):
    """Nearest integer to num/den; ties by policy (half_up = away from zero)."""
    if den < 0:
        num, den = -num, -den
    q, r = divmod(num, den)  # floor division, 0 <= r < den
    if 2 * r < den:
        return q
    if 2 * r > den:
        return q + 1
    if policy == "half_up":
        return q + 1 if num >= 0 else q
    return q if q % 2 == 0 else q + 1


def tender(amount, step=500):
    sign = -1 if amount < 0 else 1
    a = abs(amount)
    q, r = divmod(a, step)
    if r * 2 >= step:
        q += 1
    return sign * q * step


def ulid_ms(u):
    v = 0
    for ch in u[:10]:
        v = v * 32 + CROCKFORD.index(ch)
    return v


def ts(text):
    return datetime.strptime(text, "%Y-%m-%d %H:%M:%S")


def verify(run):
    db = sqlite3.connect(f"{run}/waymark-store.db")
    q = lambda sql, *a: db.execute(sql, a).fetchall()
    one = lambda sql, *a: db.execute(sql, a).fetchone()[0]

    check("integrity_check", q("PRAGMA integrity_check") == [("ok",)])
    fk = q("SELECT DISTINCT \"table\", parent FROM pragma_foreign_key_check")
    check("no dangling foreign key", not fk, str(fk[:5]))

    policy = one("SELECT rounding_policy FROM stores")
    vat = dict(q("""SELECT v.variant_id, c.tax_rate FROM variants v
                    JOIN product_category pc ON pc.product_id = v.product_id AND pc.is_primary = 1
                    JOIN categories c ON c.category_id = pc.category_id"""))
    check("every variant has one primary category with a VAT rate", len(vat) == one("SELECT count(*) FROM variants"))

    # ---- lines
    bad_lines = []
    for (iid, tid, vid, qty, price, disc, tax, total, tpol) in q("""
            SELECT i.transaction_item_id, i.transaction_id, i.variant_id, i.quantity, i.sell_price,
                   i.discount_amount, i.tax_amount, i.line_total, t.rounding_policy
            FROM transaction_items i JOIN transactions t USING (transaction_id)"""):
        gross = rnd(price * qty, 1000, tpol)
        # A refund line carries the discount as a positive column while its total is negated.
        expected_total = gross - disc if qty > 0 else gross + disc
        net = rnd(total * 10000, 10000 + vat[vid], tpol)
        if total != expected_total or tax != total - net:
            bad_lines.append((iid, qty, price, disc, total, expected_total, tax, total - net))
    check("line total = round(price x qty) - discount, and TVA by subtraction (D-033)", not bad_lines, str(bad_lines[:3]))
    check("every transaction is stamped with the store's policy (D-053)",
          one("SELECT count(*) FROM transactions WHERE rounding_policy <> ?", policy) == 0)

    # ---- transactions
    bad_tx = q("""SELECT t.transaction_id FROM transactions t
                  JOIN (SELECT transaction_id, sum(line_total) lt, sum(tax_amount) tx, sum(line_total - tax_amount) net,
                               sum(CASE WHEN quantity > 0 THEN discount_amount ELSE -discount_amount END) d
                        FROM transaction_items GROUP BY transaction_id) s USING (transaction_id)
                  WHERE t.total_amount <> s.lt OR t.tax_total <> s.tx OR t.subtotal <> s.net
                     OR t.subtotal + t.tax_total <> t.total_amount OR t.discount_total <> s.d""")
    check("header totals are the sums of their lines", not bad_tx, str(bad_tx[:3]))
    check("no transaction without lines", one("SELECT count(*) FROM transactions t WHERE NOT EXISTS (SELECT 1 FROM transaction_items i WHERE i.transaction_id = t.transaction_id)") == 0)

    bad_pay = q("""SELECT t.transaction_id, t.total_amount, coalesce(sum(p.amount), 0) FROM transactions t
                   LEFT JOIN transaction_payments p USING (transaction_id)
                   GROUP BY t.transaction_id
                   HAVING (t.status = 'voided' AND count(p.payment_id) > 0)
                       OR (t.status <> 'voided' AND coalesce(sum(p.amount), 0) <> t.total_amount)""")
    check("payments pay exactly the total; voids pay nothing", not bad_pay, str(bad_pay[:3]))
    seq = q("""SELECT transaction_id, group_concat(sequence) FROM
               (SELECT transaction_id, sequence FROM transaction_payments ORDER BY transaction_id, sequence) GROUP BY transaction_id""")
    check("payment sequences run 1..n", all(s == ",".join(str(i) for i in range(1, len(s.split(",")) + 1)) for _, s in seq))

    # ---- tender rounding
    var = defaultdict(int)
    for ref, amount in q("SELECT reference_id, amount FROM rounding_variance WHERE source = 'cash_tender'"):
        var[ref] += amount
    bad_tender = []
    cash_by_tx = defaultdict(int)
    for tid, amount in q("SELECT transaction_id, amount FROM transaction_payments WHERE payment_method = 'cash'"):
        cash_by_tx[tid] += amount
    for tid, amount in cash_by_tx.items():
        if tender(amount) - amount != var.get(tid, 0):
            bad_tender.append((tid, amount, tender(amount) - amount, var.get(tid, 0)))
    stray = [t for t in var if t not in cash_by_tx]
    check("tender variance = nearest-500 rounding of the cash payment, ties away (D-034)", not bad_tender, str(bad_tender[:3]))
    check("no tender variance without a cash payment", not stray, str(stray[:3]))

    # ---- drawer
    bad_sessions = []
    for (sid, flt, counted, expected, variance) in q("SELECT session_id, opening_float, counted_cash, expected_cash, variance FROM cash_sessions"):
        cash = one("""SELECT coalesce(sum(p.amount), 0) FROM transaction_payments p JOIN transactions t USING (transaction_id)
                      WHERE t.cash_session_id = ? AND p.payment_method = 'cash'""", sid)
        tv = one("""SELECT coalesce(sum(v.amount), 0) FROM rounding_variance v JOIN transactions t ON t.transaction_id = v.reference_id
                    WHERE t.cash_session_id = ?""", sid)
        moves = dict(q("SELECT movement_type, sum(amount) FROM cash_movements WHERE session_id = ? GROUP BY 1", sid))
        exp = flt + cash + tv + moves.get("paid_in", 0) - moves.get("paid_out", 0) - moves.get("drop", 0) + moves.get("float_add", 0) - moves.get("float_remove", 0)
        if exp != expected or counted - expected != variance:
            bad_sessions.append((sid, exp, expected, counted, variance))
    check("expected cash = float + cash + tender variance + paid_in - paid_out - drops; variance = miscount", not bad_sessions, str(bad_sessions[:3]))
    check("the drawer never goes negative at close", one("SELECT count(*) FROM cash_sessions WHERE expected_cash < 0") == 0)

    # ---- invoices
    by_year = defaultdict(list)
    for (inv,) in q("SELECT invoice_number FROM transactions WHERE invoice_number IS NOT NULL"):
        code, year, n = inv.rsplit("-", 2)
        by_year[(code, year)].append(int(n))
    gaps = {k: (len(v), max(v)) for k, v in by_year.items() if sorted(v) != list(range(1, len(v) + 1))}
    check("invoice numbers are gapless and unique per fiscal year", not gaps, str(gaps))
    check("every completed sale and refund has an invoice number, no void does",
          one("SELECT count(*) FROM transactions WHERE (status = 'voided') = (invoice_number IS NOT NULL)") == 0)
    wrong_year = one("SELECT count(*) FROM transactions WHERE invoice_number IS NOT NULL AND substr(invoice_number, length(invoice_number) - 10, 4) <> substr(occurred_at, 1, 4)")
    check("an invoice's year is the year it was issued", wrong_year == 0, str(wrong_year))

    # ---- stock
    bad_inv = q("""SELECT i.batch_id, i.quantity, coalesce(m.s, 0) FROM inventories i
                   LEFT JOIN (SELECT batch_id, variant_id, sum(quantity_changed) s FROM stock_movements GROUP BY 1, 2) m
                     ON m.batch_id = i.batch_id AND m.variant_id = i.variant_id
                   WHERE i.quantity <> coalesce(m.s, 0)""")
    check("inventories = the sum of each batch's movements", not bad_inv, str(bad_inv[:3]))
    orphan_moves = one("""SELECT count(*) FROM (SELECT batch_id, variant_id FROM stock_movements GROUP BY 1, 2) m
                          WHERE NOT EXISTS (SELECT 1 FROM inventories i WHERE i.batch_id = m.batch_id AND i.variant_id = m.variant_id)""")
    check("every batch with movements has an inventory row", orphan_moves == 0, str(orphan_moves))
    check("no level below zero", one("SELECT count(*) FROM inventories WHERE quantity < 0") == 0)
    running = defaultdict(int)
    below = []
    for (bid, vid, qty, mid) in q("SELECT batch_id, variant_id, quantity_changed, movement_id FROM stock_movements ORDER BY movement_id"):
        running[(bid, vid)] += qty
        if running[(bid, vid)] < 0:
            below.append(mid)
    check("no batch ever goes below zero, in id (time) order", not below, str(below[:3]))
    bad_sale_moves = q("""SELECT i.transaction_item_id FROM transaction_items i JOIN transactions t USING (transaction_id)
                          WHERE t.status <> 'voided' AND t.original_transaction_id IS NULL
                            AND NOT EXISTS (SELECT 1 FROM stock_movements m WHERE m.reference_id = t.transaction_id
                                            AND m.movement_type = 'sale' AND m.batch_id = i.batch_id AND m.variant_id = i.variant_id
                                            AND m.quantity_changed = -i.quantity)""")
    check("every sold line has its sale movement", not bad_sale_moves, str(bad_sale_moves[:3]))
    check("sale movements match sold lines one for one",
          one("SELECT count(*) FROM stock_movements WHERE movement_type = 'sale'")
          == one("SELECT count(*) FROM transaction_items i JOIN transactions t USING (transaction_id) WHERE t.status <> 'voided' AND t.original_transaction_id IS NULL"))
    expired = one("""SELECT count(*) FROM transaction_items i JOIN transactions t USING (transaction_id) JOIN batches b USING (batch_id)
                     WHERE i.quantity > 0 AND t.status <> 'voided'
                       AND (b.expiration_date < substr(t.occurred_at, 1, 10) OR b.received_date > substr(t.occurred_at, 1, 10))""")
    check("nothing is sold past its expiry or before it arrived (UTC date)", expired == 0, str(expired))
    status = one("""SELECT count(*) FROM batches b JOIN (SELECT batch_id, sum(quantity) q FROM inventories GROUP BY 1) i USING (batch_id)
                    WHERE (b.status IN ('depleted', 'written_off')) <> (i.q = 0)""")
    check("a batch is depleted or written off exactly when nothing is left", status == 0, str(status))

    # ---- store credit and the tab
    bal = defaultdict(int)
    bad_credit = []
    for (cid, amt, after, mid) in q("SELECT customer_id, amount, balance_after, movement_id FROM credit_movements ORDER BY customer_id, occurred_at, movement_id"):
        bal[cid] += amt
        if bal[cid] != after or bal[cid] < 0:
            bad_credit.append(mid)
    check("store credit chains exactly and never goes negative", not bad_credit, str(bad_credit[:3]))
    check("customers.credit caches the ledger",
          all(bal.get(cid, 0) == credit for cid, credit in q("SELECT customer_id, credit FROM customers")))
    limits = dict(q("SELECT customer_id, credit_limit FROM customers"))
    owed = defaultdict(int)
    bad_tab = []
    for (cid, amt, mid) in q("SELECT customer_id, amount, movement_id FROM receivable_movements ORDER BY customer_id, occurred_at, movement_id"):
        owed[cid] += amt
        if owed[cid] < 0 or limits.get(cid) is None or owed[cid] > limits[cid]:
            bad_tab.append((mid, owed[cid], limits.get(cid)))
    check("a tab stays within [0, credit limit] (D-055)", not bad_tab, str(bad_tab[:3]))
    check("sum of charges = sum of on-account payments",
          one("SELECT coalesce(sum(amount), 0) FROM receivable_movements WHERE movement_type = 'charge'")
          == one("SELECT coalesce(sum(amount), 0) FROM transaction_payments WHERE payment_method = 'on_account'"))
    check("store credit spent = store credit payments",
          one("SELECT coalesce(-sum(amount), 0) FROM credit_movements WHERE movement_type = 'redeem'")
          == one("SELECT coalesce(sum(p.amount), 0) FROM transaction_payments p JOIN transactions t USING (transaction_id) WHERE p.payment_method = 'store_credit' AND t.original_transaction_id IS NULL"))

    # ---- returns
    over = one("""SELECT count(*) FROM (SELECT r.transaction_item_id, sum(r.quantity_returned) q, i.quantity
                  FROM returns r JOIN transaction_items i USING (transaction_item_id) GROUP BY 1 HAVING q > i.quantity)""")
    check("no line is returned more than it was sold", over == 0, str(over))
    late = one("""SELECT count(*) FROM returns r JOIN transaction_items i USING (transaction_item_id) JOIN transactions t USING (transaction_id)
                  WHERE r.created_at <= t.occurred_at""")
    check("a return comes after its sale", late == 0, str(late))

    # ---- time and ids
    window = json.load(open(f"{run}/manifest.json", encoding="utf-8"))
    bad_ids = []
    for table, key, stamp in [("transactions", "transaction_id", "created_at"), ("transaction_items", "transaction_item_id", "created_at"),
                              ("transaction_payments", "payment_id", "created_at"), ("stock_movements", "movement_id", "created_at"),
                              ("receivable_movements", "movement_id", "occurred_at"), ("cash_movements", "movement_id", "occurred_at"),
                              ("credit_movements", "movement_id", "occurred_at"), ("rounding_variance", "variance_id", "created_at"),
                              ("returns", "return_id", "created_at"), ("batches", "batch_id", "created_at")]:
        for (k, s) in q(f"SELECT {key}, {stamp} FROM {table}"):
            if abs(ulid_ms(k) / 1000 - calendar.timegm(ts(s).timetuple())) > 1:
                bad_ids.append((table, k, s))
    check("every id carries its row's time (ULIDs in business time)", not bad_ids, str(bad_ids[:3]))
    first, last = window.get("first_day"), window.get("last_day")
    if first and last:
        out = one("SELECT count(*) FROM transactions WHERE substr(occurred_at, 1, 10) < ? OR substr(occurred_at, 1, 10) > ?", first, last)
        check("every sale falls inside the run window", out == 0, str(out))

    # ---- outbox
    state = dict(q("SELECT state_key, state_value FROM sync_state"))
    seqs = [s for (s,) in q("SELECT sequence_number FROM outbox ORDER BY sequence_number")]
    last_seq = int(state.get("last_sequence", 0))
    acked = int(state.get("last_acked_sequence", 0))
    check("the queued outbox is exactly last_acked+1 .. last_sequence",
          seqs == list(range(acked + 1, last_seq + 1)), f"{acked} {last_seq} {seqs[:3]}..{seqs[-3:]}")
    sales = one("SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL")
    check("one anonymous basket per sale", last_seq == sales, f"{last_seq} vs {sales}")

    # ---- latent demand CSV against the till
    sold = defaultdict(int)
    for (sku, day, qty) in q("""SELECT v.sku, substr(t.occurred_at, 1, 10), sum(i.quantity) / 1000 FROM transaction_items i
                                JOIN transactions t USING (transaction_id) JOIN variants v USING (variant_id)
                                WHERE t.status <> 'voided' AND t.original_transaction_id IS NULL GROUP BY 1, 2"""):
        sold[(sku, day)] = qty
    mismatch = []
    with open(f"{run}/latent-demand.csv", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            key = (row["sku"], row["date"])
            units = int(row["sold"]) + int(row["sold_as_substitute"])
            if units != sold.get(key, 0):
                mismatch.append((key, units, sold.get(key, 0)))
            if int(row["latent"]) != int(row["sold"]) + int(row["substituted"]) + int(row["lost"]):
                mismatch.append((key, "latent split"))
            sold.pop(key, None)
    check("latent-demand.csv sold = the till's units per variant per day (UTC date)", not mismatch, str(mismatch[:3]))
    check("no sale the CSV does not know", not sold, str(list(sold.items())[:3]))

    # ---- manifest
    counts = window.get("row_counts") or window.get("rows") or {}
    wrong = {t: (n, one(f'SELECT count(*) FROM "{t}"')) for t, n in counts.items() if n != one(f'SELECT count(*) FROM "{t}"')}
    check("manifest row counts match the database", bool(counts) and not wrong, str(wrong)[:200] if counts else "no row counts in manifest")
    return db


def dump(db, exclude):
    out = {}
    for (t,) in db.execute("SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"):
        if t in exclude:
            continue
        rows = db.execute(f'SELECT * FROM "{t}"').fetchall()
        out[t] = sorted(rows, key=repr)
    return out


def main():
    run = sys.argv[1]
    db = verify(run)
    if len(sys.argv) > 2:
        other = sqlite3.connect(f"{sys.argv[2]}/waymark-store.db")
        exclude = set(sys.argv[4].split(",")) if len(sys.argv) > 4 and sys.argv[3] == "--except" else set()
        a, b = dump(db, exclude), dump(other, exclude)
        diff = [t for t in set(a) | set(b) if a.get(t) != b.get(t)]
        check(f"canonical dump equals {sys.argv[2]} (except {sorted(exclude)})", not diff, str(diff))
    fails = [r for r in results if not r[0]]
    for ok, name, detail in sorted(results, key=lambda r: r[0]):
        print(("PASS " if ok else "FAIL ") + name + ("" if ok or not detail else "  :: " + detail[:300]))
    print(f"{len(results) - len(fails)}/{len(results)} passed")
    sys.exit(1 if fails else 0)


main()
