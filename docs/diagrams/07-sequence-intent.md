# 7 — A Cloud Admin decision reaching the store

The only channel with real difficulty. Both the success and the rejection paths are shown,
because silent rejection is the failure that destroys trust in sync.

```mermaid
sequenceDiagram
    autonumber
    participant R as Retailer on phone
    participant CA as Cloud Admin
    participant API as Cloud API
    participant S as StoreServer
    participant D as waymark-store.db
    participant Q as Pending queue

    R->>CA: accepts a reorder recommendation
    CA->>API: queue intent (recommendation_id, quantity, preconditions, expiry 48 h)
    API-->>CA: queued, not applied
    CA-->>R: "queued — will apply at next sync"

    note over API,S: store polls every 3 minutes
    S->>API: POST outbox, last_received_seq
    API-->>S: intent batch

    S->>S: validate preconditions and expiry

    alt preconditions hold, within window
        S->>D: create purchase order
        S->>D: write processing_log
        S-->>API: applied
    else expired (older than 48 h)
        S->>Q: fresh decision request, recomputed on current data
        S-->>API: rejected as stale
    else precondition broken (product discontinued, batch sold out)
        S->>Q: fresh decision request with the reason
        S-->>API: rejected as invalid
    end

    Q-->>R: surfaces on next login, in either Admin
```

**Why re-confirmation was rejected.** If the store is offline, the Cloud Admin the
retailer is looking at is already stale. Asking them to re-confirm gives them a second chance
to decide on the same stale picture. Fixed expiry windows per intent type instead — see
`../sync-design.md` §6.3.

**Idempotency.** If the same recommendation is decided both in Cloud Admin and at the
store, `recommendation_id` is the key: first decision wins, second is a no-op with a
notice.
