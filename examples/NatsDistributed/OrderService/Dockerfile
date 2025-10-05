# Dockerfile for OrderService
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 复制项目文件
COPY ["examples/NatsDistributed/OrderService/OrderService.csproj", "examples/NatsDistributed/OrderService/"]
COPY ["src/Catga/Catga.csproj", "src/Catga/"]
COPY ["src/Catga.Nats/Catga.Nats.csproj", "src/Catga.Nats/"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# 还原依赖
RUN dotnet restore "examples/NatsDistributed/OrderService/OrderService.csproj"

# 复制所有文件并构建
COPY . .
WORKDIR "/src/examples/NatsDistributed/OrderService"
RUN dotnet build "OrderService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 创建健康检查文件
RUN touch /tmp/healthy

ENTRYPOINT ["dotnet", "OrderService.dll"]

