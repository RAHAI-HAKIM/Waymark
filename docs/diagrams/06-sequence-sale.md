# 6 — Completing a sale

The walking skeleton's spine. The domain rows and the outbox row are written in one
database transaction, both or neither, and the executor is the only thing that commits
(D-050).

```mermaid
sequenceDiagram
    autonumber
    participant C as Cashier
    participant P as Waymark.Pos
    participant S as StoreServer
    participant D as waymark-store.db
    participant PS as Pseudonymisation
    participant API as Cloud API

    C->>P: scans barcode (scanner types digits + Enter)
    P->>S: GET variant by barcode
    S->>D: indexed lookup
    D-->>S: variant, price, stock
    S-->>P: line item
    P-->>C: line appears (target under 100 ms)

    C->>P: tender, cash received
    P->>S: CompleteSale command

    rect rgb(237, 231, 250)
    note over S,D: one SQLite transaction, committed once by CommandExecutor
    S->>D: stage transaction, items, payments, stock movements, rounding_variance
    opt customer attached
        S->>PS: PseudonymFor(customer)
        PS-->>S: Pseudonym, for processing_log (and tier 2, see below)
        S->>D: stage processing_log row
    end
    S->>D: stage outbox row: anonymous basket record (D-043), next sequence number
    S->>D: commit
    end

    S-->>P: sale complete
    P->>P: ESC/POS receipt, then drawer pulse (a print failure never fails the sale)
    P-->>C: receipt printed, drawer opens

    note over S,API: separately, asynchronously
    S->>API: POST outbox batch, plus last_received_seq
    API-->>S: ack with new last_applied_seq
    S->>D: delete acknowledged outbox rows
```

**Why the ack matters.** Outbox rows are deleted only after acknowledgement. A lost ack
costs a harmless replay; deleting early costs data.

**Offline changes nothing above the dividing note.** The outbox simply grows.

**Open for Phase 0.5.** Tier 2 lives in DuckDB (D-043), which cannot join this SQLite
transaction, so when and how it is fed from tier 1 is still to decide (`../status.md`
§6). The monthly customer record is emitted by a periodic job, not by the sale.
