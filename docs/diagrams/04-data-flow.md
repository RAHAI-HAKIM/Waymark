# 4 — Personal data flow and the pseudonymisation boundary

The diagram for the DPIA. Shows where personal data moves and, more importantly, where it
stops.

```mermaid
flowchart LR
    subgraph local["ON THE PREMISES"]
        direction TB
        capture["Capture at POS or Admin<br/>name, phone, email, consent"]
        tier1[("Statistics tier 1<br/>raw, identified")]
        mapping[("Mapping table<br/>customer_id to pseudonym_key<br/>separate file, separate key")]
        pseudo["Pseudonymisation<br/>THE BOUNDARY"]
        outbox[("Outbox<br/>already tier 2")]
        inbox[("Inbox<br/>pseudonym-keyed")]
        integ["Integration Layer, store half<br/>resolves identity<br/>checks objection_flag<br/>writes processing_log"]
        surface["POS and Local Admin<br/>real customer shown"]
    end

    subgraph cloudside["ALGERIAN CLOUD"]
        direction TB
        tier2[("Statistics tiers 2 and 3<br/>pseudonymous")]
        almanac["Almanac engine<br/>sees pseudonyms only"]
        recs["Recommendations<br/>keyed by pseudonym"]
    end

    capture --> tier1
    capture --> mapping
    tier1 --> pseudo
    mapping -.->|"read only, never leaves"| pseudo
    pseudo --> outbox
    outbox ==>|"crosses the boundary"| tier2
    tier2 --> almanac
    almanac --> recs
    recs ==>|"crosses the boundary"| inbox
    inbox --> integ
    mapping -.->|"read only, never leaves"| integ
    integ --> surface

    style local fill:#FAECE7,stroke:#993C1D,stroke-width:2px
    style cloudside fill:#E3F3F1,stroke:#0E8C86,stroke-width:2px
    style pseudo fill:#EDE7FA,stroke:#5A3AA8,stroke-width:2px
    style mapping fill:#FFEEED,stroke:#93292F,stroke-width:2px
```

**The claim this diagram supports.** Nothing carrying a direct identifier crosses either
thick arrow. The mapping table is read at two points, both on the premises, and travels
nowhere.

**Product improvement data (DPIA §2.7)** is not shown because it is not personal data:
aggregates over at least 20 data subjects, computed at the store, fixed metric list.

**On erasure**, the mapping row is deleted and the pseudonym is nulled on the cloud's
transaction rows — unlinking rather than key destruction. See Waymark_Sync_Design §9.
