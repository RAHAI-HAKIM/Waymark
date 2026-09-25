# tools

Build, database and development scripts. Nothing here ships to a store.

| Folder | What it is |
| :---- | :---- |
| `generate-model/` | The one-shot that bootstrapped the EF model from `schema_v7_1.sql` (D-023). Never re-run |
| `enrich-catalogue/` | The one-shot that completed `docs/products.csv` into the grocery catalogue |
| `verify-store/` | An independent check of a generated store: 39 invariants recomputed from raw rows |
| `till-harness/` | The till's window driven headlessly against a fake StoreServer: scrolling, focus, colours, start-up (D-084) |
