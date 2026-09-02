# 3 — Deployment, by tier

What physically runs on what hardware. Tier definitions from Operating Rules §2.

```mermaid
flowchart TB
    subgraph basic["BASIC — single-till épicerie"]
        b1["Terminal 1<br/>Windows 10<br/>POS + StoreServer + SQLite"]
        b2["Second device<br/>nightly local backup"]
        b1 --> b2
    end

    subgraph pro["PRO — supermarket, 3 terminals included"]
        p1["Primary terminal<br/>POS + StoreServer + SQLite"]
        p2["Terminal 2<br/>POS + hot replica"]
        p3["Terminal 3<br/>POS only"]
        p4["Mini-PC<br/>recommended, not required"]
        p2 -->|"LAN"| p1
        p3 -->|"LAN"| p1
        p2 -.->|"manual promotion if primary dies"| p1
    end

    subgraph ent["ENTERPRISE — multi-store"]
        e1["Dedicated store box<br/>required, one per store"]
        e2["Terminals"]
        e2 -->|"LAN"| e1
    end

    cloud["Algerian cloud<br/>engine, statistics, Cloud Admin"]

    basic -.->|"mTLS"| cloud
    pro -.->|"mTLS"| cloud
    ent -.->|"mTLS"| cloud

    style cloud fill:#E3F3F1,stroke:#0E8C86
```

**Why Basic requires no extra hardware:** demanding a mini-PC from an épicerie would kill
adoption. The hot replica at Pro removes the single point of failure — a cashier shutting
down the primary — without imposing cost on anyone who cannot carry it.

**Promotion is manual, never automatic.** Automatic failover across a partitioned LAN
produces split-brain, the only genuine data-loss scenario in the design. See
Waymark_Sync_Design §11.3.
