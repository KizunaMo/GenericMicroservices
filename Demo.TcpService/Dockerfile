# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Demo.TcpService/Demo.TcpService.csproj Demo.TcpService/
RUN dotnet restore Demo.TcpService/Demo.TcpService.csproj

COPY Demo.TcpService/ Demo.TcpService/
RUN dotnet publish Demo.TcpService/Demo.TcpService.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
# TCP Service 也不是 Web 服務，用精簡的 runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Demo.TcpService.dll"]
