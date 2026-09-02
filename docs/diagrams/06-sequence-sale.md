# 6 — Completing a sale

The walking skeleton's spine. Note that the transaction rows and the outbox row are
written in one database transaction — both or neither.

```mermaid
sequenceDiagram
    autonumber
    participant C as Cashier
    participant P as Waymark.Pos
    participant S as StoreServer
    participant D as waymark-store.db
    participant PS as Pseudonymisation
    participant O as Outbox
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
    note over S,O: one SQLite transaction
    S->>D: write transaction, items, stock movements
    S->>D: write statistics tier 1 (identified)
    S->>PS: pseudonymise the tier-2 payload
    PS-->>S: pseudonym-keyed event
    S->>O: write outbox row with next sequence number
    end

    S-->>P: sale complete
    P->>P: ESC/POS byte stream to printer, then drawer pulse
    P-->>C: receipt printed, drawer opens

    note over S,API: separately, asynchronously
    S->>API: POST outbox batch, plus last_received_seq
    API-->>S: ack with new last_applied_seq
    S->>O: delete acknowledged rows
```

**Why the ack matters.** Outbox rows are deleted only after acknowledgement. A lost ack
costs a harmless replay; deleting early costs data.

**Offline changes nothing above the dividing note.** The outbox simply grows. Same code
path.
