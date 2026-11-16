# Render / Docker — Trade With Beast API (TradeWithBeast.csproj, net10.0)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TradeWithBeast.csproj .
RUN dotnet restore TradeWithBeast.csproj

COPY . .
RUN dotnet publish TradeWithBeast.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# Persist SQLite and backups via bind mount, e.g.:
#   -v /var/lib/tradewithbeast:/var/lib/tradewithbeast

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -f http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "TradeWithBeast.dll"]
