"""Adds weighed goods to a generated store, for trying B3 on the real till (D-090).

The generator sells whole units only (weighed produce joins it before J1), so seed-42 has nothing
sold by weight. This adds, to a generated store's *plaintext* file, before it is imported:

  - a kilogram unit weighed to the gram;
  - "Tomates Vrac" at 180,00 DA/kg, PLU 4011, weight labels;
  - "Olives noires Vrac" at 650,00 DA/kg, PLU 537, price labels;
  - a batch of each, 25 kg of tomatoes and 8 kg of olives.

It prints a label of each to scan, in the default format (D-090's "standard"). Run it once per
generated store, then import as docs/status.md §8 says:

    python tools/dev-weighed/add_weighed.py artifacts/generated/seed-42/waymark-store.db

Nothing here ships to a store: it writes rows the way a test fixture does, not through a command.
"""

import sqlite3
import sys

NOW = "2026-01-01 08:00:00"
TODAY = "2026-01-01"


def check_digit(twelve: str) -> str:
    total = sum(int(c) * (1 if i % 2 == 0 else 3) for i, c in enumerate(twelve))
    return str((10 - total % 10) % 10)


def label(prefix: str, plu: str, value: int) -> str:
    twelve = f"{prefix}{plu.zfill(5)}{value:05d}"
    return twelve + check_digit(twelve)


def main(path: str) -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    db = sqlite3.connect(path)
    db.execute("PRAGMA foreign_keys = ON")
    stores = [row[0] for row in db.execute("SELECT store_id FROM stores")]
    if not stores:
        sys.exit("No store in this file: is it a generated store?")

    if db.execute("SELECT 1 FROM variants WHERE plu IN ('4011', '537')").fetchone():
        sys.exit("This store already has the weighed products (PLU 4011 or 537). Nothing was changed.")

    with db:
        db.execute(
            "INSERT OR IGNORE INTO units_of_measure (unit_code, name_ar, name_fr, dimension, decimal_places, created_at) "
            "VALUES ('kg', 'كيلوغرام', 'kilogramme', 'weight', 3, ?)", (NOW,))
        db.execute(
            "INSERT INTO categories (category_id, category_name, slug, tax_rate, created_at, updated_at) "
            "VALUES ('DEVWEIGHEDCATEGORY00000000', 'Vrac (dev B3)', 'vrac-dev-b3', 900, ?, ?)", (NOW, NOW))

        products = [
            # id suffix, product, variant, PLU, barcode type, price per kg in centimes, grams in stock
            ("TOMATES", "Tomates", "Vrac", "4011", "weight_embedded", 18_000, 25_000),
            ("OLIVES", "Olives noires", "Vrac", "537", "price_embedded", 65_000, 8_000),
        ]
        for suffix, product, variant, plu, barcode_type, price, grams in products:
            product_id = f"DEVWEIGHEDPRODUCT{suffix}"[:26]
            variant_id = f"DEVWEIGHEDVARIANT{suffix}"[:26]
            db.execute(
                "INSERT INTO products (product_id, product_name, created_at, updated_at) VALUES (?, ?, ?, ?)",
                (product_id, product, NOW, NOW))
            db.execute(
                "INSERT INTO product_category (product_id, category_id, is_primary, added_at) "
                "VALUES (?, 'DEVWEIGHEDCATEGORY00000000', 1, ?)", (product_id, NOW))
            db.execute(
                "INSERT INTO variants (variant_id, product_id, variant_name, plu, barcode_type, selling_unit_code, "
                "is_weighted, created_at, updated_at) VALUES (?, ?, ?, ?, ?, 'kg', 1, ?, ?)",
                (variant_id, product_id, variant, plu, barcode_type, NOW, NOW))
            for store in stores:
                batch_id = f"DEVWEIGHEDBATCH{suffix}{store[-4:]}"[:26]
                db.execute(
                    "INSERT INTO prices (variant_id, valid_from, store_id, price, created_at) VALUES (?, ?, ?, ?, ?)",
                    (variant_id, TODAY, store, price, NOW))
                db.execute(
                    "INSERT INTO batches (batch_id, product_id, store_id, received_date, created_at) VALUES (?, ?, ?, ?, ?)",
                    (batch_id, product_id, store, TODAY, NOW))
                db.execute(
                    "INSERT INTO batch_items (batch_id, variant_id, quantity_received, unit_code, unit_cost, created_at) "
                    "VALUES (?, ?, ?, 'kg', ?, ?)", (batch_id, variant_id, grams, price // 2, NOW))
                db.execute(
                    "INSERT INTO inventories (store_id, variant_id, batch_id, quantity, updated_at) VALUES (?, ?, ?, ?, ?)",
                    (store, variant_id, batch_id, grams, NOW))

    print("Added: Tomates Vrac (PLU 4011, 180,00 DA/kg, weight labels) and Olives noires Vrac (PLU 537, 650,00 DA/kg, price labels).")
    print("On the till:")
    print("  type 4011, Entrée, then a weight such as 0,556 and Entrée   -> a typed weight")
    print(f"  scan {label('21', '4011', 556)}                               -> 0,556 kg of tomatoes, from a weight label")
    print(f"  scan {label('22', '537', 100)}                               -> 100,00 DA of olives, from a price label")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
