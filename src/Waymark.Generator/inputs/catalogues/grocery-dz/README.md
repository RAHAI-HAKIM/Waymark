# grocery-dz — the synthetic épicerie's catalogue

What the store **is**. How it **behaves** is `../../configs/grocery-dz.json`. Loaded by
`CsvCatalogueSource` and checked by `CatalogueValidator` before anything is written
(decisions.md D-046, D-054).

| File | Holds |
| :---- | :---- |
| `store.json` | Identity, rounding policy, UTC offset, terminals, roles, staff, opening hours per day type, reason codes, notices |
| `units.csv` | Units of measure. Everything here sells by the `piece` and is bought by the `carton` |
| `categories.csv` | Hakim's 8 categories and 41 subcategories. **The VAT class is on the subcategory** (the schema only has `categories.tax_rate`) |
| `suppliers.csv` | 5 suppliers: delivery weekdays (`sun|wed`), stated lead time, payment terms |
| `catalogue.csv` | 399 variants: prices as at commissioning, supplier, carton size, shelf life, demand |
| `price_changes.csv` | Dated retail and purchase price changes during 2025 |

## Where the values came from

`docs/products.csv` is Hakim's list of 399 real Algerian products — category, product,
variant, nothing else. **Every other value is a guess**, added by
`tools/enrich-catalogue/enrich.py` from family rules with seeded jitter. The rules are
written at the top of that script, which is the place to read the reasoning. The script is a
one-shot: edit these CSVs directly from now on, because re-running it overwrites them.

Two defects in the source were fixed on the way: "Café, Thé & Petit Déjeuner" was not
quoted, so its comma split the row, and one category was truncated to "Détergents &
Produits d me".

## catalogue.csv columns

| Column | Meaning |
| :---- | :---- |
| `sku` | `GDZ-0001`… in source order. Unique |
| `barcode` | EAN-13 with the **in-store prefix 200**, so it can never scan as a real product. Check digit valid |
| `category`, `subcategory` | Must match a row of `categories.csv` |
| `product_name`, `variant_name` | From the source. Variants sharing a product name share a subcategory and a supplier |
| `net_content` | Parsed from the variant name: `250 g`, `1500 ml`, `40 pcs`. Informational |
| `selling_unit` | `piece` for everything |
| `retail_price_dzd` | TTC shelf price at commissioning. Ends in 5 below 100 DZD, in 0 above, except a quarter of non-regulated variants carry 1–4 DZD more, so cash tender rounding happens (Hakim, 14/09). Regulated oil and sugar at their fixed prices |
| `purchase_price_dzd` | HT cost per selling unit: retail net of VAT, less a margin drawn from the family's band |
| `supplier_code` | Must match `suppliers.csv` |
| `purchase_unit`, `units_per_purchase_unit` | Ordered in cartons of this many pieces |
| `shelf_life_days` | Empty for non-perishables (hygiene, cleaning) |
| `base_daily_rate` | Pieces a day before seasonality. The catalogue sums to 420 a day |
| `seasonality_profile` | A profile name defined in the configuration |
| `substitutability_tier` | 1 easily substituted, 2 some loyalty, 3 brand-loyal |
