# Architecture Diagrams

## 1. Container diagram

```mermaid
graph TB
    Browser["User Browser"]

    subgraph FE["Next.js 15 · App Router"]
        RSC["Server Components<br/>public + SEO pages"]
        CC["Client Components<br/>interactions"]
        API["Server-side fetch layer"]
    end

    subgraph BE["ASP.NET Core 8 Web API · Marketplace.API"]
        MW["Middleware pipeline<br/>logging · exceptions · rate limit · CORS"]
        CTL["Controllers"]
        HUB["SignalR Hub<br/>/hubs/marketplace"]
        SVC["Application Services"]
    end

    subgraph IN["Marketplace.Infrastructure"]
        EF["EF Core DbContext<br/>repositories · UoW"]
        RD["Redis cache"]
        JOB["Hangfire / BackgroundService"]
        PG["Payment gateways<br/>Mock · CoD · Khalti · eSewa · Stripe"]
    end

    subgraph DOM["Marketplace.Domain"]
        ENT["Entities · Value Objects"]
        RULE["Business rules · State machines"]
    end

    DB[("PostgreSQL")]
    REDIS[("Redis")]
    EXT["E-mail / Object storage / Webhook senders"]

    Browser --> RSC
    Browser --> CC
    RSC --> API
    CC --> API
    Browser -.websocket.-> HUB

    API --> MW --> CTL --> SVC
    HUB --> SVC
    SVC --> EF
    SVC --> RD
    SVC --> PG
    SVC --> RULE
    JOB --> SVC
    EF --> DB
    RD --> REDIS
    PG --> EXT

    style DOM fill:#1b1436,stroke:#7c5cff,color:#eae6ff
    style FE fill:#0f2a4a,stroke:#2f7fd4,color:#e6f1ff
    style BE fill:#132a1f,stroke:#2fa36b,color:#e6fff0
    style IN fill:#3a2410,stroke:#d9822b,color:#fff2e6
```

## 2. Clean Architecture dependency rule

```mermaid
graph LR
    API["Marketplace.API"]
    APP["Marketplace.Application"]
    DOM["Marketplace.Domain"]
    INF["Marketplace.Infrastructure"]

    API --> APP
    INF --> APP
    APP --> DOM
    INF -.implements.-> APP
    API -.DI registration.-> INF

    style DOM fill:#1b1436,stroke:#7c5cff,color:#eae6ff
    style APP fill:#0f2a4a,stroke:#2f7fd4,color:#e6f1ff
    style API fill:#132a1f,stroke:#2fa36b,color:#e6fff0
    style INF fill:#3a2410,stroke:#d9822b,color:#fff2e6
```

The Domain has **no NuGet package references**. A unit test fails the build if that ever changes.

## 3. Multi-seller order split

```mermaid
graph TD
    C["Cart<br/>4 items from 3 sellers"]
    C --> V{"Validate<br/>price · stock · coupon · address"}
    V --> R["Reserve inventory<br/>atomic + concurrency token"]
    R --> O["Order #1001<br/>single marketplace order"]
    O --> SA["SellerOrder A · TechWorld<br/>OrderItem A1, A2"]
    O --> SB["SellerOrder B · FashionHub<br/>OrderItem B1"]
    O --> SC["SellerOrder C · HomeEssentials<br/>OrderItem C1"]
    SA --> CA["Commission A · 10% snapshot"]
    SB --> CB["Commission B · 12% snapshot"]
    SC --> CC["Commission C · 8% snapshot"]
    SA --> P["Payment"]
    SB --> P
    SC --> P
    P --> W["Webhook → verify signature<br/>idempotency → confirm"]
    W --> N["Notify customer + 3 sellers"]

    style O fill:#1b1436,stroke:#7c5cff,color:#eae6ff
    style P fill:#132a1f,stroke:#2fa36b,color:#e6fff0
```

## 4. Inventory concurrency

```mermaid
sequenceDiagram
    autonumber
    participant A as Customer A
    participant B as Customer B
    participant API
    participant DB as PostgreSQL

    A->>API: checkout (qty 1)
    B->>API: checkout (qty 1)
    API->>DB: BEGIN
    API->>DB: UPDATE inventory SET reserved = reserved + 1
    API->>DB: WHERE id = @id AND available - reserved >= 1
    alt rows affected = 0
        API->>DB: ROLLBACK
        API-->>B: 409 insufficient_stock
    else rows affected = 1
        API->>DB: INSERT inventory_transactions (RESERVE)
        API->>DB: COMMIT
        API-->>A: 201 order created
    end
    Note over API,DB: A single conditional UPDATE is the lock.<br/>No SELECT-then-UPDATE race is possible.
```

## 5. Order state machine

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Confirmed : seller/admin confirms
    Pending --> Cancelled : customer or admin cancels
    Confirmed --> Processing : seller starts picking
    Confirmed --> Cancelled : cancel before dispatch
    Processing --> Packed : packed
    Processing --> Cancelled : cancel while unshipped
    Packed --> Shipped : handed to carrier
    Shipped --> Delivered : delivery confirmed
    Delivered --> Returned : return approved
    Delivered --> Completed : auto-close after window
    Completed --> [*]
    Cancelled --> [*]
    Returned --> [*]
```

`Completed`, `Cancelled` and `Returned` are terminal. Illegal transitions throw `InvalidOrderStateTransitionException` and are also rejected by the FluentValidation rule on `UpdateOrderStatusRequest`.

## 6. Payment & refund flow

```mermaid
graph LR
    O["Order (paid=false)"] --> PC["POST /api/payments"]
    PC --> G["IPaymentGateway.CreateAsync"]
    G --> P["Payment (Initiated)"]
    P --> R{"Redirect / QR<br/>(Khalti·eSewa·Stripe)"}
    R --> WH["POST /api/payments/webhook"]
    WH --> S{"HMAC signature<br/>valid?"}
    S -- no --> RJ["409 rejected + logged"]
    S -- yes --> ID{"Event id already<br/>processed?"}
    ID -- yes --> OK["200 OK (idempotent no-op)"]
    ID -- no --> TX["BEGIN · verify amount+order<br/>mark succeeded · release reservation"]
    TX --> CM["Create commissions"]
    TX --> CM
    TX --> NT["Notifications + SignalR"]
    CM --> FIN["COMMIT"]
    FIN --> OK

    style RJ fill:#4a1414,stroke:#e04a4a,color:#ffe6e6
    style FIN fill:#132a1f,stroke:#2fa36b,color:#e6fff0
```

Refund path: customer request → admin review → approve → gateway refund → payment refunded → inventory returned → order state updated → both parties notified. Rejected refunds record a mandatory reason and stay auditable.

## 7. Multi-layer caching

```mermaid
graph TD
    U["Browser / Next.js router cache"] --> TQ["TanStack Query<br/>staleTime per domain"]
    TQ --> NX["Next.js fetch cache<br/>revalidateTag on mutation"]
    NX --> API["ASP.NET Core API"]
    API --> RD[("Redis<br/>catalog, categories, stores")]
    API --> PG[("PostgreSQL<br/>source of truth")]

    style PG fill:#132a1f,stroke:#2fa36b,color:#e6fff0
    style RD fill:#3a1414,stroke:#e07a2b,color:#fff0e6
```

Financial and stock state is never served from any cache layer above PostgreSQL.
