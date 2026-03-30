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
# Worker 不是 Web 服務（沒有 HTTP），用精簡的 runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Demo.Worker.dll"]
