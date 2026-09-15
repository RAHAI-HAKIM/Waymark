# enrich-catalogue

**One-shot.** Turned `docs/products.csv` (399 products, three columns) into the complete
`grocery-dz` catalogue under `src/Waymark.Generator/inputs/catalogues/grocery-dz/` on
14/09/2026 (decisions.md D-054).

```bash
python tools/enrich-catalogue/enrich.py
```

Deterministic: the same input and seed produce byte-identical files.

> **Do not re-run it after editing the CSVs by hand**: it overwrites them. It is kept, like
> `tools/generate-model`, so every added value can be traced to a written rule rather than
> to nobody.

All the reasoning is at the top of `enrich.py`: the subcategories and their VAT class, the
five suppliers, margin bands, and one family per product line with a reference size, price,
shelf life, demand rate, seasonality profile and carton size. Everything it adds is a guess.
