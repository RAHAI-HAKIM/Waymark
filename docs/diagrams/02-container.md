# 2 — Containers

The deployable pieces and the store/cloud line. The most useful single diagram in the
project.

```mermaid
flowchart TB
    subgraph premises["STORE PREMISES — authoritative for all operational data"]
        pos["Waymark.Pos<br/>Avalonia desktop"]
        server["Waymark.StoreServer<br/>ASP.NET Core"]
        localadmin["Local Admin<br/>TypeScript web app"]
        storedb[("waymark-store.db<br/>SQLite<br/>operational + stats tier 1")]
        identitydb[("waymark-identity.db<br/>SQLite<br/>mapping table only")]
        poscache[("POS Level-2 cache<br/>SQLite")]
        backup[("Local nightly backup<br/>second device")]
    end

    subgraph algeria["ALGERIAN CLOUD — pseudonymised only"]
        api["Cloud API"]
        engine["Almanac engine<br/>Python, nightly"]
        postgres[("Postgres<br/>tenants, subscriptions, sync state")]
        duckdb[("DuckDB, one file per tenant<br/>stats tiers 2 and 3")]
        cloudadmin["Cloud Admin<br/>TypeScript PWA"]
        cloudbackup[("Cloud backup<br/>never holds the mapping")]
    end

    pos -->|"local HTTP"| server
    pos --> poscache
    localadmin -->|"HTTP over LAN"| server
    server --> storedb
    server --> identitydb
    server --> backup
    identitydb -.->|"local backup only"| backup

    server <-->|"mTLS, store-initiated, both directions in one round trip"| api

    api --> postgres
    api --> duckdb
    engine --> duckdb
    engine --> postgres
    cloudadmin -->|"HTTPS, user session"| api
    api --> cloudbackup

    style premises fill:#FBFAFC,stroke:#5A3AA8,stroke-width:2px
    style algeria fill:#FBFAFC,stroke:#0E8C86,stroke-width:2px
    style identitydb fill:#FAECE7,stroke:#993C1D,stroke-width:2px
```

**Reading it.** Only one arrow crosses between the two boxes, and it is store-initiated.
The identity database is highlighted because it is the only store that must never
cross — not over sync, not over backup.

**Absent from the cloud by design:** any screen or table showing a customer name, phone or
email.
