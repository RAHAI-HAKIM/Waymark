# 4 — Personal data flow and the pseudonymisation boundary

The diagram for the DPIA. Shows where personal data moves and, more importantly, where it
stops.

```mermaid
flowchart LR
    subgraph local["ON THE PREMISES"]
        direction TB
        capture["Capture at POS or Admin<br/>name, phone, email, consent"]
        tier1[("Statistics tier 1<br/>raw, identified")]
        key["Tenant key<br/>HMAC-SHA256, held only by<br/>Waymark.Pseudonymisation"]
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
    tier1 --> pseudo
    key -.->|"never leaves the premises"| pseudo
    pseudo --> outbox
    outbox ==>|"crosses the boundary"| tier2
    tier2 --> almanac
    almanac --> recs
    recs ==>|"crosses the boundary"| inbox
    inbox --> integ
    key -.->|"never leaves the premises"| integ
    integ --> surface

    style local fill:#FAECE7,stroke:#993C1D,stroke-width:2px
    style cloudside fill:#E3F3F1,stroke:#0E8C86,stroke-width:2px
    style pseudo fill:#EDE7FA,stroke:#5A3AA8,stroke-width:2px
    style key fill:#FFEEED,stroke:#93292F,stroke-width:2px
```

**The claim this diagram supports.** Nothing carrying a direct identifier crosses either
thick arrow. The pseudonym is `HMAC-SHA256(tenant_key, "waymark:customer:v1:" ‖
customer_id)` truncated to 128 bits (decisions.md D-039), and the key is used at two
points, both on the premises. It is never in a backup and never in a sync payload, which
is what stops anyone holding the cloud data from recomputing who is who.

*Revised 11/09/2026: was a mapping table in a separate file. There is no mapping and no
second database. What is kept separately is the key.*

**Product improvement data (DPIA §2.7)** is not shown because it is not personal data:
aggregates over at least 20 data subjects, computed at the store, fixed metric list.

**On erasure**, the pseudonym is nulled on the cloud's transaction rows and identity
columns in `processing_log` are purged — unlinking rather than key destruction, because
destroying a mapping alone would leave the history linked to itself and still able to
single someone out. See `Waymark_Implementation` §9.9.
