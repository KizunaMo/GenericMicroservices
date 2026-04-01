# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# 先複製 .csproj（包含 Demo.Contracts，restore 需要知道相依）
COPY Demo.RealTime/Demo.RealTime.csproj Demo.RealTime/
COPY Demo.Contracts/Demo.Contracts.csproj Demo.Contracts/
RUN dotnet restore Demo.RealTime/Demo.RealTime.csproj

# 複製完整原始碼再 publish
COPY Demo.RealTime/ Demo.RealTime/
COPY Demo.Contracts/ Demo.Contracts/
RUN dotnet publish Demo.RealTime/Demo.RealTime.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Demo.RealTime.dll"]
