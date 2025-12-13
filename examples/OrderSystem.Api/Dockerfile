# Stage 1: Build Vue frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app/client-app
COPY examples/OrderSystem.Api/client-app/package*.json ./
RUN npm ci
COPY examples/OrderSystem.Api/client-app/ ./
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY Directory.*.props ./
COPY src/ src/
COPY examples/OrderSystem.Api/ examples/OrderSystem.Api/
COPY examples/OrderSystem.ServiceDefaults/ examples/OrderSystem.ServiceDefaults/

# Restore and publish
WORKDIR /src/examples/OrderSystem.Api
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published app
COPY --from=backend-build /app/publish .
# Copy frontend build
COPY --from=frontend-build /app/client-app/../wwwroot ./wwwroot

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Catga__Transport=InMemory
ENV Catga__Persistence=InMemory
ENV Catga__DevelopmentMode=false

EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderSystem.Api.dll"]
