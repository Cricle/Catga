# Catga Kubernetes 集成指南

## 🎯 架构概述

Catga 在 Kubernetes 环境中的架构：

```
┌─────────────────────────────────────────┐
│         Kubernetes Cluster              │
│                                         │
│  ┌───────────────────────────────────┐ │
│  │  Catga 微服务 (Deployment)        │ │
│  │  - order-service (3 replicas)     │ │
│  │  - inventory-service (3 replicas) │ │
│  │  - payment-service (3 replicas)   │ │
│  └───────────────────────────────────┘ │
│              ↓ ↑                        │
│  ┌───────────────────────────────────┐ │
│  │  消息系统 (StatefulSet)           │ │
│  │  - NATS JetStream (3 nodes)       │ │
│  │    或                              │ │
│  │  - Redis Cluster (6 nodes)        │ │
│  └───────────────────────────────────┘ │
│              ↓ ↑                        │
│  ┌───────────────────────────────────┐ │
│  │  K8s 基础设施                      │ │
│  │  - DNS 服务发现                    │ │
│  │  - Service 负载均衡                │ │
│  │  - Health Check 健康检查           │ │
│  │  - HPA 自动扩缩容                  │ │
│  └───────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

## 🚀 快速开始

### 方案 1: Helm Chart 部署（推荐）

创建 Helm Chart 结构：

```
catga-app/
├── Chart.yaml
├── values.yaml
└── templates/
    ├── nats.yaml
    ├── order-service.yaml
    ├── inventory-service.yaml
    └── payment-service.yaml
```

#### values.yaml

```yaml
# NATS 配置
nats:
  enabled: true
  replicas: 3
  jetstream:
    enabled: true
    memoryStore:
      enabled: true
      size: 1Gi

# 服务配置
services:
  order:
    image: order-service:latest
    replicas: 3
    port: 8080
    env:
      NATS_URL: "nats://nats:4222"
      
  inventory:
    image: inventory-service:latest
    replicas: 3
    port: 8080
    env:
      NATS_URL: "nats://nats:4222"
```

#### 部署

```bash
helm install catga-app ./catga-app
```

### 方案 2: 手动部署

#### 1. 部署 NATS JetStream

```bash
kubectl apply -f - <<EOF
apiVersion: v1
kind: Service
metadata:
  name: nats
  labels:
    app: nats
spec:
  selector:
    app: nats
  clusterIP: None  # Headless service for StatefulSet
  ports:
  - name: client
    port: 4222
  - name: cluster
    port: 6222
  - name: monitor
    port: 8222
---
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: nats
spec:
  serviceName: nats
  replicas: 3
  selector:
    matchLabels:
      app: nats
  template:
    metadata:
      labels:
        app: nats
    spec:
      containers:
      - name: nats
        image: nats:2.10-alpine
        args:
        - "-js"
        - "-sd"
        - "/data"
        - "-cluster"
        - "nats://0.0.0.0:6222"
        - "-routes"
        - "nats://nats-0.nats:6222,nats://nats-1.nats:6222,nats://nats-2.nats:6222"
        ports:
        - containerPort: 4222
          name: client
        - containerPort: 6222
          name: cluster
        - containerPort: 8222
          name: monitor
        volumeMounts:
        - name: data
          mountPath: /data
        resources:
          requests:
            memory: "256Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "500m"
  volumeClaimTemplates:
  - metadata:
      name: data
    spec:
      accessModes: [ "ReadWriteOnce" ]
      resources:
        requests:
          storage: 10Gi
EOF
```

#### 2. 部署 Catga 微服务

```bash
kubectl apply -f - <<EOF
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
    spec:
      containers:
      - name: api
        image: order-service:latest
        env:
        - name: NATS_URL
          value: "nats://nats:4222"
        - name: ASPNETCORE_URLS
          value: "http://+:8080"
        ports:
        - containerPort: 8080
          name: http
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "500m"
---
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
  ports:
  - port: 80
    targetPort: 8080
  type: ClusterIP
EOF
```

## 💻 应用代码配置

### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. 添加健康检查
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddNatsHealthCheck();  // 可选：检查 NATS 连接

// 2. 配置 Catga
builder.Services.AddCatga();
builder.Services.AddCatgaJsonSerialization();

// 3. 配置 NATS 传输（使用 K8s DNS）
var natsUrl = builder.Configuration["NATS_URL"] ?? "nats://nats:4222";

builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var opts = NatsOpts.Default with 
    { 
        Url = natsUrl,
        Name = Environment.GetEnvironmentVariable("HOSTNAME") // K8s Pod 名称
    };
    return new NatsConnection(opts);
});

builder.Services.AddSingleton<IMessageTransport>(sp =>
{
    var connection = sp.GetRequiredService<INatsConnection>();
    var serializer = sp.GetRequiredService<IMessageSerializer>();
    var logger = sp.GetRequiredService<ILogger<NatsMessageTransport>>();
    return new NatsMessageTransport(connection, serializer, logger);
});

// 4. 配置分布式中介器
builder.Services.AddSingleton<IDistributedMediator>(sp =>
{
    var localMediator = sp.GetRequiredService<ICatgaMediator>();
    var transport = sp.GetRequiredService<IMessageTransport>();
    var logger = sp.GetRequiredService<ILogger<DistributedMediator>>();
    return new DistributedMediator(localMediator, transport, logger);
});

var app = builder.Build();

// 5. 映射健康检查端点
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Name == "self"
});

app.MapHealthChecks("/health/ready");

app.Run();
```

## 🔧 配置管理

### ConfigMap

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: catga-config
data:
  appsettings.json: |
    {
      "Catga": {
        "EnableLogging": true,
        "EnableTracing": true,
        "DefaultQoS": "AtLeastOnce"
      },
      "Logging": {
        "LogLevel": {
          "Default": "Information",
          "Catga": "Debug"
        }
      }
    }
```

### 使用 ConfigMap

```yaml
spec:
  containers:
  - name: api
    volumeMounts:
    - name: config
      mountPath: /app/appsettings.json
      subPath: appsettings.json
  volumes:
  - name: config
    configMap:
      name: catga-config
```

### Secrets

```bash
# 创建 Secret
kubectl create secret generic catga-secrets \
  --from-literal=redis-password=your-password \
  --from-literal=nats-token=your-token
```

```yaml
# 使用 Secret
env:
- name: NATS_TOKEN
  valueFrom:
    secretKeyRef:
      name: catga-secrets
      key: nats-token
```

## 📊 监控和观测

### Prometheus 监控

```yaml
apiVersion: v1
kind: Service
metadata:
  name: order-service
  annotations:
    prometheus.io/scrape: "true"
    prometheus.io/port: "8080"
    prometheus.io/path: "/metrics"
spec:
  # ... service definition
```

### 应用代码

```csharp
// 添加 Prometheus 指标
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddCatgaInstrumentation()  // Catga 指标
               .AddPrometheusExporter();
    });

app.MapPrometheusScrapingEndpoint();  // /metrics
```

### 分布式追踪（Jaeger）

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddCatgaInstrumentation()
               .AddJaegerExporter(options =>
               {
                   options.AgentHost = "jaeger-agent";
                   options.AgentPort = 6831;
               });
    });
```

## 🔄 自动扩缩容

### Horizontal Pod Autoscaler (HPA)

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
  # 基于自定义指标
  - type: Pods
    pods:
      metric:
        name: catga_messages_per_second
      target:
        type: AverageValue
        averageValue: "1000"
```

## 🛡️ 高可用和容错

### Pod Disruption Budget

```yaml
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: order-service-pdb
spec:
  minAvailable: 2
  selector:
    matchLabels:
      app: order-service
```

### 反亲和性（避免单点故障）

```yaml
spec:
  template:
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
          - weight: 100
            podAffinityTerm:
              labelSelector:
                matchExpressions:
                - key: app
                  operator: In
                  values:
                  - order-service
              topologyKey: kubernetes.io/hostname
```

## 📝 最佳实践

### 1. 资源限制

```yaml
resources:
  requests:
    memory: "128Mi"
    cpu: "100m"
  limits:
    memory: "512Mi"
    cpu: "1000m"
```

### 2. 优雅关闭

```csharp
var app = builder.Build();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStopping.Register(async () =>
{
    // 停止接收新消息
    var mediator = app.Services.GetRequiredService<IDistributedMediator>();
    await mediator.StopAsync();
    
    // 等待现有消息处理完成
    await Task.Delay(TimeSpan.FromSeconds(5));
});

app.Run();
```

### 3. 服务网格集成（Istio）

```yaml
apiVersion: networking.istio.io/v1beta1
kind: VirtualService
metadata:
  name: order-service
spec:
  hosts:
  - order-service
  http:
  - timeout: 30s
    retries:
      attempts: 3
      perTryTimeout: 10s
    route:
    - destination:
        host: order-service
```

## 🔍 故障排查

### 查看日志

```bash
# 查看 Pod 日志
kubectl logs -f deployment/order-service

# 查看特定 Pod
kubectl logs order-service-xxxxx-yyyyy

# 查看 NATS 日志
kubectl logs statefulset/nats
```

### 调试连接

```bash
# 进入 Pod
kubectl exec -it order-service-xxxxx-yyyyy -- /bin/sh

# 测试 NATS 连接
nc -zv nats 4222

# 查看环境变量
env | grep NATS
```

### 常见问题

1. **NATS 连接失败**
   ```bash
   # 检查 NATS Service
   kubectl get svc nats
   kubectl describe svc nats
   
   # 检查 NATS Pods
   kubectl get pods -l app=nats
   ```

2. **消息丢失**
   ```bash
   # 检查 JetStream 状态
   kubectl exec -it nats-0 -- nats stream ls
   kubectl exec -it nats-0 -- nats stream info catga-messages
   ```

3. **性能问题**
   ```bash
   # 查看资源使用
   kubectl top pods
   kubectl top nodes
   ```

## 🚀 CI/CD 集成

### GitHub Actions 示例

```yaml
name: Deploy to K8s

on:
  push:
    branches: [ main ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    
    - name: Build Docker Image
      run: |
        docker build -t ${{ secrets.REGISTRY }}/order-service:${{ github.sha }} .
        docker push ${{ secrets.REGISTRY }}/order-service:${{ github.sha }}
    
    - name: Deploy to Kubernetes
      run: |
        kubectl set image deployment/order-service \
          api=${{ secrets.REGISTRY }}/order-service:${{ github.sha }}
        kubectl rollout status deployment/order-service
```

---

**总结**：Catga 与 Kubernetes 的完美结合，充分利用云原生生态，实现高可用、可扩展的分布式 CQRS 架构。

