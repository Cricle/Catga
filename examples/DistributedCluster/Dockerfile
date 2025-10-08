# Catga DistributedCluster Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Catga.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Copy all project files
COPY ["src/Catga/Catga.csproj", "src/Catga/"]
COPY ["src/Catga.SourceGenerator/Catga.SourceGenerator.csproj", "src/Catga.SourceGenerator/"]
COPY ["src/Catga.Serialization.MemoryPack/Catga.Serialization.MemoryPack.csproj", "src/Catga.Serialization.MemoryPack/"]
COPY ["src/Catga.Transport.Nats/Catga.Transport.Nats.csproj", "src/Catga.Transport.Nats/"]
COPY ["src/Catga.Persistence.Redis/Catga.Persistence.Redis.csproj", "src/Catga.Persistence.Redis/"]
COPY ["examples/DistributedCluster/DistributedCluster.csproj", "examples/DistributedCluster/"]

# Restore dependencies
RUN dotnet restore "examples/DistributedCluster/DistributedCluster.csproj"

# Copy all source code
COPY . .

# Build and publish
WORKDIR "/src/examples/DistributedCluster"
RUN dotnet publish "DistributedCluster.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 8080

# Copy published app
COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "DistributedCluster.dll"]

