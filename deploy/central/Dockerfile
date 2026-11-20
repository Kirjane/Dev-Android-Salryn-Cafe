# Salryn Cafe Central Sync Service — Multi-stage .NET 10 container
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Salryn.Sync.Contracts/Salryn.Sync.Contracts.csproj src/Salryn.Sync.Contracts/
COPY src/Salryn.Sync.Central.Server/Salryn.Sync.Central.Server.csproj src/Salryn.Sync.Central.Server/
RUN dotnet restore src/Salryn.Sync.Central.Server/Salryn.Sync.Central.Server.csproj

COPY src/Salryn.Sync.Contracts/ src/Salryn.Sync.Contracts/
COPY src/Salryn.Sync.Central.Server/ src/Salryn.Sync.Central.Server/
RUN dotnet publish src/Salryn.Sync.Central.Server/Salryn.Sync.Central.Server.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system salryn && useradd --system --gid salryn --home /app salryn \
    && mkdir -p /var/lib/salryn-cafe-central/data /var/lib/salryn-cafe-central/backups \
    && chown -R salryn:salryn /var/lib/salryn-cafe-central /app

COPY --from=build /app/publish .
USER salryn

ENV ASPNETCORE_URLS=http://0.0.0.0:8443
ENV SALRYN_CAFE_CENTRAL_DB_PATH=/var/lib/salryn-cafe-central/data/salryn-cafe-central.db
ENV SALRYN_CAFE_CENTRAL_REQUIRE_HTTPS=true

EXPOSE 8443
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -fsS -H "X-Forwarded-Proto: https" http://127.0.0.1:8443/api/sync/v1/health || exit 1

ENTRYPOINT ["dotnet", "Salryn.Sync.Central.Server.dll"]
