# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# DataService 參考了 Demo.Contracts，restore 時必須同時看到兩個 .csproj
COPY Demo.DataService/Demo.DataService.csproj Demo.DataService/
COPY Demo.Contracts/Demo.Contracts.csproj Demo.Contracts/
RUN dotnet restore Demo.DataService/Demo.DataService.csproj

# 複製兩個專案的所有原始碼
COPY Demo.DataService/ Demo.DataService/
COPY Demo.Contracts/ Demo.Contracts/
RUN dotnet publish Demo.DataService/Demo.DataService.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Demo.DataService.dll"]
