# Dockerfile for OrderApi
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 复制项目文件
COPY ["examples/OrderApi/OrderApi.csproj", "examples/OrderApi/"]
COPY ["src/Catga/Catga.csproj", "src/Catga/"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# 还原依赖
RUN dotnet restore "examples/OrderApi/OrderApi.csproj"

# 复制所有文件并构建
COPY . .
WORKDIR "/src/examples/OrderApi"
RUN dotnet build "OrderApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 健康检查
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "OrderApi.dll"]

