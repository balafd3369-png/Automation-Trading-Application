# Render / Docker — Trade With Beast API (TradeWithBeast.csproj, net8.0)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY TradeWithBeast.csproj .
RUN dotnet restore TradeWithBeast.csproj

COPY . .
RUN dotnet publish TradeWithBeast.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "TradeWithBeast.dll"]
