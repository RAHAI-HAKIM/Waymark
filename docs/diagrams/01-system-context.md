# 1 — System context

Waymark as one box. No internals. This is the diagram for a slide.

```mermaid
flowchart TB
    cashier["Cashier<br/>operates the till"]
    retailer["Retailer or manager<br/>configures, decides"]
    shopper["Shopper<br/>data subject"]
    waymark["WAYMARK<br/>Retail management system<br/>with the Almanac engine"]
    supplier["Supplier<br/>receives purchase orders"]
    hardware["Till hardware<br/>printer, drawer, scanner, scale"]
    anpdp["ANPDP<br/>supervisory authority"]

    cashier -->|"sells, scans, refunds"| waymark
    retailer -->|"accepts or adjusts recommendations"| waymark
    shopper -->|"consent, rights requests"| waymark
    waymark -->|"receipts, drawer pulse, weight"| hardware
    waymark -->|"purchase orders"| supplier
    waymark -.->|"declaration, breach notice"| anpdp

    style waymark fill:#EDE7FA,stroke:#5A3AA8,stroke-width:2px
```

**Note.** The shopper is a data subject, not a user. They never touch the software; the
retailer is the controller and captures consent on their behalf.
