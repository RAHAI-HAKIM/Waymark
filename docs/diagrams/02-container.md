# 2 — Containers

The deployable pieces and the store/cloud line. The most useful single diagram in the
project.

```mermaid
flowchart TB
    subgraph premises["STORE PREMISES — authoritative for all operational data"]
        pos["Waymark.Pos<br/>Avalonia desktop"]
        server["Waymark.StoreServer<br/>ASP.NET Core"]
        localadmin["Local Admin<br/>TypeScript web app"]
        storedb[("waymark-store.db<br/>SQLite, SQLCipher<br/>operational + stats tier 1")]
        tenantkey["keys\ directory<br/>tenant key + database key<br/>DPAPI-wrapped"]
        tier2[("Statistics tier 2<br/>DuckDB<br/>pseudonymised, transaction grain")]
        poscache[("POS Level-2 cache<br/>SQLite")]
        backup[("Local nightly backup<br/>second device")]
    end

    subgraph algeria["ALGERIAN CLOUD — pseudonymised only"]
        api["Cloud API"]
        engine["Almanac engine<br/>Python, nightly"]
        postgres[("Postgres<br/>tenants, subscriptions, sync state")]
        cloudadmin["Cloud Admin<br/>TypeScript PWA"]
        cloudbackup[("Cloud backup<br/>never holds the tenant key")]
        tier3[("Statistics tier 3<br/>DuckDB per tenant")]
    end

    pos -->|"local HTTP"| server
    pos --> poscache
    localadmin -->|"HTTP over LAN"| server
    server --> storedb
    server --> tier2
    server --> tenantkey
    server --> backup
    tenantkey -.->|"local backup only, never cloud"| backup

    server <-->|"mTLS, store-initiated, both directions in one round trip"| api

    api --> postgres
    api --> tier3
    engine --> tier3
    engine --> postgres
    cloudadmin -->|"HTTPS, user session"| api
    api --> cloudbackup

    style premises fill:#FBFAFC,stroke:#5A3AA8,stroke-width:2px
    style algeria fill:#FBFAFC,stroke:#0E8C86,stroke-width:2px
    style tier2 fill:#E3F3F1,stroke:#0E8C86,stroke-width:2px
    style tenantkey fill:#FAECE7,stroke:#993C1D,stroke-width:2px
```

**Reading it.** Only one arrow crosses between the two boxes, and it is store-initiated.
The keys are highlighted because they must never cross, over sync or the cloud backup
(D-039, D-042). `waymark-store.db` is SQLCipher-encrypted by design; the database key is
not implemented yet (O-20).

**Absent from the cloud by design:** any screen or table showing a customer name, phone or
email.
