# Catga Examples

This directory contains complete examples demonstrating **Catga framework** capabilities.

## 📚 Available Examples

### 1. **OrderSystem** - Complete Order Management System 🛒

A production-ready order management system showcasing all Catga features.

**Features:**
- ✅ SQLite persistence with Entity Framework Core
- ✅ CQRS pattern (Commands, Queries, Events)
- ✅ 3 deployment modes: Standalone, Distributed (Redis), Cluster (NATS)
- ✅ Complete order lifecycle management
- ✅ Event-driven notifications and analytics
- ✅ Swagger UI for API testing
- ✅ Health checks and monitoring
- ✅ Test scripts and cluster deployment automation

**Quick Start:**
```bash
cd OrderSystem

# Run standalone mode (no dependencies)
dotnet run

# Run with Redis
$env:DeploymentMode="Distributed-Redis"
dotnet run

# Run 3-node NATS cluster
.\run-cluster.ps1

# Test the API
.\test-api.ps1
```

**What You'll Learn:**
- How to structure a CQRS application
- How to use SQLite with Catga
- How to deploy in different modes
- How to implement event handlers
- How to test distributed systems

📖 **[Full Documentation](OrderSystem/README.md)**

---

### 2. **RedisExample** - Redis Integration 🔴

Demonstrates Redis-based distributed features.

**Features:**
- ✅ Distributed lock
- ✅ Distributed cache
- ✅ Redis cluster support
- ✅ Graceful degradation when Redis is unavailable

**Quick Start:**
```bash
cd RedisExample

# Start Redis
docker run -d -p 6379:6379 redis:alpine

# Run example
dotnet run
```

**What You'll Learn:**
- Redis distributed lock usage
- Redis distributed cache integration
- Graceful fallback strategies
- Redis cluster configuration

📖 **[Full Documentation](RedisExample/README.md)**

---

## 🎯 Choosing an Example

| Example | Complexity | External Dependencies | Best For |
|---------|------------|----------------------|----------|
| **OrderSystem** | Advanced | Optional (SQLite included, Redis/NATS for distributed) | Learning complete CQRS systems |
| **RedisExample** | Beginner | Optional (works without Redis) | Learning Redis integration |

---

## 🚀 Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- (Optional) Docker for running Redis/NATS

### Running Examples

Each example can run independently:

```bash
# Clone the repository
git clone https://github.com/your-org/Catga.git
cd Catga/examples

# Choose an example
cd OrderSystem  # or RedisExample

# Run it
dotnet run
```

### Running Tests

Each example includes its own test scripts:

```bash
# OrderSystem API tests
cd OrderSystem
.\test-api.ps1

# Or use curl/Postman with the provided endpoints
```

---

## 📖 Example Structure

Each example follows this structure:

```
ExampleName/
├── Program.cs              # Main entry point
├── README.md               # Detailed documentation
├── *.csproj                # Project file
├── appsettings.json        # Configuration
├── test-*.ps1              # Test scripts (if applicable)
└── [Other files]           # Example-specific files
```

---

## 🔧 Configuration

All examples support environment-based configuration:

```bash
# Deployment mode
$env:DeploymentMode="Standalone"  # or "Distributed-Redis", "Cluster"

# Node ID (for clustering)
$env:NodeId="node-1"

# Redis connection
$env:ConnectionStrings__Redis="localhost:6379"

# NATS connection
$env:Nats__Url="nats://localhost:4222"

# Run the example
dotnet run
```

---

## 🐳 Docker Support

Run infrastructure with Docker:

```bash
# Redis
docker run -d -p 6379:6379 --name redis redis:alpine

# NATS with JetStream
docker run -d -p 4222:4222 -p 8222:8222 --name nats nats:latest -js

# Stop and remove
docker stop redis nats
docker rm redis nats
```

---

## 📊 Performance

All examples are optimized for performance:

- **Standalone mode**: ~10,000 operations/sec
- **Distributed mode (Redis)**: ~5,000 operations/sec
- **Cluster mode (NATS)**: ~8,000 operations/sec per node

*Results may vary based on hardware and network conditions.*

---

## 🎓 Learning Path

Recommended order for learning:

1. **Start with RedisExample** - Learn basic Catga concepts and Redis integration
2. **Move to OrderSystem** - Understand complete CQRS architecture
3. **Experiment with deployment modes** - Test Standalone → Distributed → Cluster
4. **Customize for your needs** - Use as templates for your projects

---

## 🤝 Contributing

Found an issue or have a suggestion? Please open an issue or submit a pull request!

- **Add new examples**: Follow the existing structure
- **Improve documentation**: Help others learn faster
- **Report bugs**: Use GitHub Issues

---

## 📝 License

MIT License - See [LICENSE](../LICENSE) for details

---

## 🔗 Related Resources

- **[Catga Documentation](../README.md)** - Main framework documentation
- **[Architecture Guide](../docs/CATGA_VS_MASSTRANSIT.md)** - Catga vs MassTransit comparison
- **[API Reference](../src/Catga/README.md)** - Core API documentation
- **[Contributing Guide](../CONTRIBUTING.md)** - How to contribute

---

## 💬 Need Help?

- 📚 **Documentation**: Check the example READMEs first
- 🐛 **Issues**: [GitHub Issues](https://github.com/your-org/Catga/issues)
- 💡 **Discussions**: [GitHub Discussions](https://github.com/your-org/Catga/discussions)

---

**Happy coding! 🚀**
