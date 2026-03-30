# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Worker 參考了 Demo.Contracts，需要同時 restore 兩個專案
COPY Demo.Worker/Demo.Worker.csproj Demo.Worker/
COPY Demo.Contracts/Demo.Contracts.csproj Demo.Contracts/
RUN dotnet restore Demo.Worker/Demo.Worker.csproj

COPY Demo.Worker/ Demo.Worker/
COPY Demo.Contracts/ Demo.Contracts/
RUN dotnet publish Demo.Worker/Demo.Worker.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
# MassTransit 依賴 Microsoft.AspNetCore.App，需要 aspnet image（非純 runtime）
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Demo.Worker.dll"]
