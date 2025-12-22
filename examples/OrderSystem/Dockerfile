# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files
COPY ["examples/OrderSystem/OrderSystem.csproj", "examples/OrderSystem/"]
COPY ["src/Catga/Catga.csproj", "src/Catga/"]
COPY ["src/Catga.AspNetCore/Catga.AspNetCore.csproj", "src/Catga.AspNetCore/"]
COPY ["src/Catga.Cluster/Catga.Cluster.csproj", "src/Catga.Cluster/"]
COPY ["src/Catga.Persistence.InMemory/Catga.Persistence.InMemory.csproj", "src/Catga.Persistence.InMemory/"]
COPY ["src/Catga.Persistence.Redis/Catga.Persistence.Redis.csproj", "src/Catga.Persistence.Redis/"]
COPY ["src/Catga.Persistence.Nats/Catga.Persistence.Nats.csproj", "src/Catga.Persistence.Nats/"]
COPY ["src/Catga.Serialization.MemoryPack/Catga.Serialization.MemoryPack.csproj", "src/Catga.Serialization.MemoryPack/"]
COPY ["src/Catga.Transport.InMemory/Catga.Transport.InMemory.csproj", "src/Catga.Transport.InMemory/"]
COPY ["src/Catga.Transport.Redis/Catga.Transport.Redis.csproj", "src/Catga.Transport.Redis/"]
COPY ["src/Catga.Transport.Nats/Catga.Transport.Nats.csproj", "src/Catga.Transport.Nats/"]
COPY ["src/Catga.SourceGenerator/Catga.SourceGenerator.csproj", "src/Catga.SourceGenerator/"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Restore dependencies
RUN dotnet restore "examples/OrderSystem/OrderSystem.csproj"

# Copy source code
COPY . .

# Build
WORKDIR "/src/examples/OrderSystem"
RUN dotnet build "OrderSystem.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "OrderSystem.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app
EXPOSE 5000

# Copy published app
COPY --from=publish /app/publish .

# Set entrypoint
ENTRYPOINT ["dotnet", "OrderSystem.dll"]
