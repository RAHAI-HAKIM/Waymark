# 8 — Recommendation and intent lifecycles

Two state machines. The Integration Layer implements both.

## Recommendation

```mermaid
stateDiagram-v2
    [*] --> Issued: department emits via the envelope
    Issued --> RoleGated: Integration Layer checks privileges
    RoleGated --> Pending: no privileged user present
    RoleGated --> Delivered: privileged user present
    Pending --> Delivered: privileged user logs in
    Delivered --> Accepted: retailer accepts
    Delivered --> Adjusted: retailer changes the number
    Delivered --> Dismissed: retailer dismisses
    Delivered --> Snoozed: retailer defers
    Snoozed --> Delivered: snooze elapses
    Accepted --> [*]: downstream action taken
    Adjusted --> [*]: downstream action taken
    Dismissed --> [*]: logged as a decision
```

Every terminal transition writes to the statistics module, because the decision itself is
a signal — revealed preference feeds parameter tuning later.

`action_type` is `binary` (accept or decline) or `menu` (pick one of several), per
System_Architecture. Group 3's markdown ladder needs `menu`.

## Intent

```mermaid
stateDiagram-v2
    [*] --> Queued: Cloud Admin decision
    Queued --> InTransit: store polls
    InTransit --> Validating: arrives at the store
    Validating --> Applied: preconditions hold, within window
    Validating --> RejectedStale: past its expiry window
    Validating --> RejectedInvalid: preconditions broken
    RejectedStale --> FreshRequest: recomputed against current data
    RejectedInvalid --> FreshRequest: recomputed, with the reason
    FreshRequest --> [*]: enters the pending queue
    Applied --> [*]
```

**No silent terminal state on the rejection paths.** Both rejections become a fresh
decision request the retailer sees. Expiry windows per intent type are tabulated in
Waymark_Sync_Design §6.3.
