# 📐 Catga v2.0 架构图集

完整的架构图可视化文档

---

## 🎯 核心架构总览

```mermaid
graph TB
    subgraph Client["客户端层"]
        WebAPI[Web API]
        Console[Console App]
        Worker[Worker Service]
    end

    subgraph Catga["Catga框架核心"]
        direction TB
        Mediator[CatgaMediator]
        
        subgraph Performance["性能优化"]
            FastPath[FastPath<br/>零分配路径]
            HandlerCache[HandlerCache<br/>Handler缓存]
            ObjectPool[ObjectPool<br/>对象池]
        end
        
        subgraph Pipeline["管道系统"]
            PipelineExec[Pipeline Executor]
            Behaviors[Behaviors<br/>10+ behaviors]
        end
        
        subgraph Handlers["处理器"]
            CommandH[Command Handlers]
            QueryH[Query Handlers]
            EventH[Event Handlers]
        end
    end

    subgraph Infrastructure["基础设施"]
        direction LR
        NATS[(NATS<br/>消息队列)]
        Redis[(Redis<br/>持久化)]
        K8s[Kubernetes<br/>服务发现]
    end

    subgraph Toolchain["开发工具链"]
        SourceGen[Source Generator<br/>自动注册]
        Analyzers[Analyzers<br/>代码检查]
    end

    WebAPI --> Mediator
    Console --> Mediator
    Worker --> Mediator
    
    Mediator --> FastPath
    Mediator --> HandlerCache
    Mediator --> ObjectPool
    Mediator --> PipelineExec
    
    PipelineExec --> Behaviors
    PipelineExec --> CommandH
    PipelineExec --> QueryH
    PipelineExec --> EventH
    
    Behaviors --> NATS
    Behaviors --> Redis
    
    Mediator -.-> K8s
    
    SourceGen -.生成代码.-> Handlers
    Analyzers -.检查.-> Client

    style Mediator fill:#4CAF50,stroke:#2E7D32,stroke-width:3px,color:#fff
    style FastPath fill:#FF9800,stroke:#F57C00,stroke-width:2px,color:#fff
    style NATS fill:#3949AB,stroke:#1A237E,stroke-width:2px,color:#fff
    style Redis fill:#DC382D,stroke:#B71C1C,stroke-width:2px,color:#fff
```

---

## 🔄 Command处理流程（标准路径）

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant API as API Controller
    participant M as Mediator
    participant Cache as Handler Cache
    participant P as Pipeline
    participant L as Logging Behavior
    participant V as Validation Behavior
    participant O as Outbox Behavior
    participant H as Command Handler
    participant DB as Database

    C->>+API: POST /api/orders
    API->>+M: SendAsync(CreateOrderCommand)
    
    Note over M: 检查RateLimit、<br/>ConcurrencyLimit
    
    M->>+Cache: GetRequestHandler<T>()
    Cache-->>-M: Handler (cached)
    
    alt FastPath可用（无Behaviors）
        M->>+H: HandleAsync(command)
        H->>DB: Save Order
        DB-->>H: Success
        H-->>-M: CatgaResult.Success
    else 标准Pipeline
        M->>+P: ExecuteAsync()
        
        P->>+L: Pre-processing
        L-->>-P: Continue
        
        P->>+V: Validate(command)
        V->>V: Check rules
        alt 验证失败
            V-->>P: Failure
            P-->>M: ValidationError
        else 验证成功
            V-->>-P: Continue
            
            P->>+O: Pre-processing
            O->>O: Save to Outbox
            O-->>-P: Continue
            
            P->>+H: HandleAsync(command)
            H->>+DB: BEGIN TRANSACTION
            H->>DB: INSERT Order
            DB-->>-H: Order Created
            H-->>-P: CatgaResult.Success
            
            P->>+O: Post-processing
            O->>O: Mark as Sent
            O-->>-P: Done
            
            P->>+L: Post-processing
            L->>L: Log Success
            L-->>-P: Done
        end
        
        P-->>-M: Final Result
    end
    
    M-->>-API: CatgaResult<OrderDto>
    API-->>-C: 201 Created

    rect rgb(200, 250, 200)
        Note over M,H: ✅ 成功路径
    end
```

---

## 📢 Event发布流程（多Handler并发）

```mermaid
sequenceDiagram
    autonumber
    participant M as Mediator
    participant C as Handler Cache
    participant H1 as Event Handler 1
    participant H2 as Event Handler 2
    participant H3 as Event Handler 3
    participant Pool as ArrayPool<Task>
    
    M->>+C: GetEventHandlers<T>()
    C-->>-M: List<IEventHandler> (3 handlers)
    
    alt FastPath: 0 handlers
        M->>M: Return immediately (zero allocation)
    else FastPath: 1 handler
        M->>+H1: HandleAsync(event)
        H1-->>-M: Done
    else Standard Path: Multiple handlers
        Note over M,Pool: 使用ArrayPool优化<br/>（>16个Handler时）
        
        M->>+Pool: Rent(3)
        Pool-->>-M: Task[16] (rented)
        
        par 并发执行
            M->>+H1: HandleAsync(event)
            and
            M->>+H2: HandleAsync(event)
            and
            M->>+H3: HandleAsync(event)
        end
        
        Note over H1,H3: 隔离执行<br/>异常不影响其他Handler
        
        H1-->>-M: Done
        H2-->>-M: Done (with error)
        H3-->>-M: Done
        
        M->>M: Task.WhenAll(tasks[0..2])
        
        M->>+Pool: Return(array)
        Pool-->>-M: Returned
    end

    rect rgb(255, 245, 200)
        Note over M,H3: ⚡ ArrayPool优化<br/>减少GC压力
    end
```

---

## 🌐 分布式消息流

```mermaid
sequenceDiagram
    autonumber
    participant N1 as Node 1
    participant O1 as Outbox Store
    participant N as NATS JetStream
    participant N2 as Node 2
    participant I2 as Inbox Store
    participant Id2 as Idempotency Store

    N1->>+O1: SaveAsync(message)
    O1->>O1: INSERT INTO outbox<br/>Status=Pending
    O1-->>-N1: Saved
    
    Note over N1,O1: Outbox Pattern<br/>保证At-Least-Once
    
    loop Outbox Publisher (background)
        O1->>O1: GetPendingMessages()
        O1->>+N: Publish(message)
        N->>N: Persist to JetStream
        N-->>-O1: ACK
        O1->>O1: UPDATE Status=Published
    end
    
    N->>+N2: Deliver(message)
    N2->>+I2: TryLockMessage()
    alt 消息已处理
        I2-->>N2: Locked (duplicate)
        N2->>N2: Skip processing
    else 首次处理
        I2-->>-N2: Locked
        
        N2->>+Id2: IsProcessed(messageId)
        alt 已处理（幂等性检查）
            Id2-->>N2: true
            N2->>N2: Skip
        else 未处理
            Id2-->>-N2: false
            
            N2->>N2: Handle(message)
            N2->>+I2: MarkAsProcessed()
            I2-->>-N2: Done
            N2->>+Id2: RecordProcessed()
            Id2-->>-N2: Done
        end
    end
    
    N2->>+N: ACK
    N-->>-N2: Confirmed

    rect rgb(200, 230, 255)
        Note over O1,N: Outbox Pattern<br/>保证消息可靠发送
    end
    
    rect rgb(255, 230, 200)
        Note over N2,Id2: Inbox + Idempotency<br/>保证Exactly-Once处理
    end
```

---

## 🏗️ 集群拓扑

```mermaid
graph TB
    subgraph LoadBalancer["负载均衡器"]
        LB[Nginx / HAProxy<br/>:80]
    end

    subgraph CatgaCluster["Catga集群"]
        N1[Node 1<br/>:8081]
        N2[Node 2<br/>:8082]
        N3[Node 3<br/>:8083]
    end

    subgraph MessageBus["消息总线"]
        NATS[NATS Cluster<br/>:4222]
        
        subgraph JetStream["JetStream"]
            Stream1[Stream: ORDERS]
            Stream2[Stream: EVENTS]
        end
    end

    subgraph Storage["持久化存储"]
        Redis1[(Redis Primary<br/>:6379)]
        Redis2[(Redis Replica<br/>:6380)]
        
        subgraph RedisData["Redis数据"]
            Outbox[Outbox Messages]
            Inbox[Inbox Messages]
            Idempotency[Idempotency Keys]
        end
    end

    subgraph ServiceDiscovery["服务发现"]
        K8s[Kubernetes<br/>Service Discovery]
        DNS[DNS]
    end

    subgraph Observability["可观测性"]
        Prom[Prometheus<br/>Metrics]
        Jaeger[Jaeger<br/>Tracing]
        Health[Health Checks]
    end

    LB -->|Round Robin| N1
    LB -->|Round Robin| N2
    LB -->|Round Robin| N3

    N1 <-->|Pub/Sub| NATS
    N2 <-->|Pub/Sub| NATS
    N3 <-->|Pub/Sub| NATS

    NATS --> Stream1
    NATS --> Stream2

    N1 -->|Write| Redis1
    N2 -->|Write| Redis1
    N3 -->|Write| Redis1
    
    Redis1 -.Replication.-> Redis2

    Redis1 --> Outbox
    Redis1 --> Inbox
    Redis1 --> Idempotency

    N1 -.Register.-> K8s
    N2 -.Register.-> K8s
    N3 -.Register.-> K8s
    
    K8s -.Update.-> DNS

    N1 -.Export.-> Prom
    N2 -.Export.-> Prom
    N3 -.Export.-> Prom

    N1 -.Spans.-> Jaeger
    N2 -.Spans.-> Jaeger
    N3 -.Spans.-> Jaeger

    N1 -->|/health| Health
    N2 -->|/health| Health
    N3 -->|/health| Health

    style LB fill:#2196F3,color:#fff
    style N1 fill:#4CAF50,color:#fff
    style N2 fill:#4CAF50,color:#fff
    style N3 fill:#4CAF50,color:#fff
    style NATS fill:#3949AB,color:#fff
    style Redis1 fill:#DC382D,color:#fff
    style K8s fill:#326CE5,color:#fff
```

---

## 🔧 源生成器工作流

```mermaid
sequenceDiagram
    autonumber
    participant Dev as Developer
    participant Roslyn as Roslyn Compiler
    participant SG as Source Generator
    participant Syntax as Syntax Tree
    participant Gen as Generated Code
    participant Build as Build Output

    Dev->>Dev: 编写Handler
    
    Note over Dev: public class CreateOrderHandler<br/>: IRequestHandler<...>

    Dev->>Roslyn: dotnet build
    Roslyn->>+SG: Initialize()
    SG-->>-Roslyn: Ready

    Roslyn->>+SG: Execute(context)
    SG->>+Syntax: AnalyzeSyntaxTree()
    
    Syntax->>Syntax: Find IRequestHandler
    Syntax->>Syntax: Find IEventHandler
    Syntax->>Syntax: Collect Metadata
    
    Syntax-->>-SG: Handler List

    SG->>SG: ValidateHandlers()
    
    alt 验证失败
        SG->>Roslyn: ReportDiagnostic(error)
        Roslyn-->>Dev: ❌ Build Error
    else 验证成功
        SG->>+Gen: GenerateCode()
        
        Gen->>Gen: Generate Attribute
        Note over Gen: [CatgaHandler]<br/>public sealed class...
        
        Gen->>Gen: Generate Registration
        Note over Gen: public static class<br/>CatgaGeneratedHandlerRegistrations
        
        Gen->>Gen: Generate Pipeline
        Note over Gen: Pre-compiled<br/>pipeline methods
        
        Gen-->>-SG: Generated Source

        SG->>Roslyn: AddSource(name, code)
        SG-->>-Roslyn: Complete
        
        Roslyn->>Roslyn: Compile All
        Roslyn->>+Build: Output Assembly
        Build-->>-Dev: ✅ Build Success
    end

    rect rgb(200, 255, 200)
        Note over SG,Gen: 编译时代码生成<br/>零反射、100% AOT
    end
```

---

## 🎯 性能优化策略图

```mermaid
mindmap
  root((Catga性能优化))
    内存优化
      ValueTask
        减少Task分配
        零分配路径
      ArrayPool
        Task数组池化
        Buffer池化
      ObjectPool
        RequestContext池化
        自定义对象池
      零拷贝
        Span<T>
        ReadOnlySpan<T>
        IBufferWriter
    CPU优化
      AggressiveInlining
        热路径内联
        减少方法调用
      HandlerCache
        Handler实例缓存
        ConcurrentDictionary
      FastPath
        无Behavior直接执行
        单Handler优化
      批处理
        批量消息处理
        批量数据库操作
    并发优化
      无锁设计
        ConcurrentDictionary
        Interlocked原子操作
        SemaphoreSlim
      ValueTask异步
        避免Task分配
        ConfigureAwait(false)
      并行执行
        Event并发处理
        ArrayPool优化
    AOT优化
      源生成器
        编译时注册
        零反射
      分析器
        编译时检查
        自动修复
      类型约束
        泛型约束明确
        DynamicallyAccessedMembers
```

---

## 📊 数据流向图

```mermaid
flowchart LR
    subgraph Input["输入"]
        HTTP[HTTP Request]
        gRPC[gRPC Call]
        Queue[Message Queue]
    end

    subgraph Processing["处理层"]
        Mediator[Mediator<br/>统一入口]
        
        subgraph Commands["Commands"]
            CreateCmd[Create]
            UpdateCmd[Update]
            DeleteCmd[Delete]
        end
        
        subgraph Queries["Queries"]
            GetQuery[Get]
            ListQuery[List]
            SearchQuery[Search]
        end
        
        subgraph Events["Events"]
            CreatedEvt[Created]
            UpdatedEvt[Updated]
            DeletedEvt[Deleted]
        end
    end

    subgraph Storage["存储层"]
        WriteDB[(Write DB<br/>Primary)]
        ReadDB[(Read DB<br/>Replica)]
        Cache[(Cache<br/>Redis)]
    end

    subgraph Output["输出"]
        Response[HTTP Response]
        EventBus[Event Bus<br/>NATS]
        Notification[Notifications]
    end

    HTTP --> Mediator
    gRPC --> Mediator
    Queue --> Mediator

    Mediator --> CreateCmd
    Mediator --> UpdateCmd
    Mediator --> DeleteCmd
    Mediator --> GetQuery
    Mediator --> ListQuery
    Mediator --> SearchQuery

    CreateCmd --> WriteDB
    UpdateCmd --> WriteDB
    DeleteCmd --> WriteDB
    
    WriteDB -.Replicate.-> ReadDB
    
    GetQuery --> Cache
    ListQuery --> ReadDB
    SearchQuery --> ReadDB
    
    Cache -.Miss.-> ReadDB
    ReadDB -.Update.-> Cache

    CreateCmd -.Publish.-> CreatedEvt
    UpdateCmd -.Publish.-> UpdatedEvt
    DeleteCmd -.Publish.-> DeletedEvt

    CreatedEvt --> EventBus
    UpdatedEvt --> EventBus
    DeletedEvt --> EventBus

    CreateCmd --> Response
    GetQuery --> Response
    EventBus --> Notification

    style Mediator fill:#4CAF50,stroke:#2E7D32,stroke-width:3px,color:#fff
    style WriteDB fill:#FF5722,color:#fff
    style ReadDB fill:#2196F3,color:#fff
    style Cache fill:#FF9800,color:#fff
    style EventBus fill:#9C27B0,color:#fff
```

---

**🎨 所有架构图使用Mermaid语法，可在GitHub、Markdown编辑器中直接渲染！**

