# CatgaMicroservice

A production-ready microservice built with Catga CQRS framework.

## Features

- ✅ **CQRS Pattern** - Clean separation of commands and queries
- ✅ **Distributed ID** - Globally unique IDs
- ✅ **Resilience** - Circuit breaker and concurrency limiting
- ✅ **Health Checks** - Liveness and readiness probes
- ✅ **Prometheus Metrics** - Observable and monitorable
- ✅ **Kubernetes Ready** - Production-grade deployment manifests
- ✅ **CI/CD** - Automated build and deployment pipeline
- ✅ **AOT Compatible** - Fast startup, low memory footprint

## Quick Start

```bash
# Run locally
dotnet run

# Run with Docker
docker build -t catga-microservice .
docker run -p 8080:8080 catga-microservice

# Deploy to Kubernetes
kubectl apply -f k8s/
```

## API Endpoints

- `GET /` - Service information
- `GET /health` - Health check
- `GET /health/live` - Liveness probe
- `GET /health/ready` - Readiness probe
- `GET /metrics` - Prometheus metrics (if enabled)
- `GET /swagger` - API documentation

## Configuration

Configure via `appsettings.json` or environment variables:

```json
{
  "DistributedId": {
    "WorkerId": 1,
    "DataCenterId": 1
  }
}
```

## Kubernetes Deployment

The service includes:
- **Deployment** with 3 replicas
- **Service** for internal communication
- **HorizontalPodAutoscaler** for auto-scaling (3-10 pods)
- **Health checks** for liveness and readiness

```bash
# Deploy
kubectl apply -f k8s/deployment.yaml

# Check status
kubectl get pods -l app=catga-microservice
kubectl get svc catga-microservice

# Scale manually
kubectl scale deployment catga-microservice --replicas=5
```

## Monitoring

### Prometheus Metrics

Metrics are exposed at `/metrics`:

- HTTP request duration
- Request success/failure rates
- Circuit breaker state
- Concurrency limiter utilization

### Health Checks

- **/health/live** - Is the service alive?
- **/health/ready** - Is the service ready to accept traffic?

## Development

```bash
# Run tests
dotnet test

# Build
dotnet build

# Publish (AOT)
dotnet publish -c Release /p:PublishAot=true
```

## CI/CD

GitHub Actions workflow:
1. Build and test on every push
2. Build Docker image
3. Push to registry on main branch
4. Deploy to Kubernetes (configure as needed)

## Project Structure

```
CatgaMicroservice/
├── Commands/          # Command handlers
├── Queries/           # Query handlers
├── k8s/               # Kubernetes manifests
├── .github/workflows/ # CI/CD pipelines
├── Program.cs         # Entry point
└── README.md          # This file
```

## Best Practices

1. **Commands** - Use for state changes
2. **Queries** - Use for data retrieval
3. **Health Checks** - Monitor dependencies
4. **Metrics** - Track performance
5. **Resilience** - Use circuit breakers

## Learn More

- [Catga Framework](https://github.com/yourorg/catga)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Kubernetes Best Practices](https://kubernetes.io/docs/concepts/)

