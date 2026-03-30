# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Demo.RealTime/Demo.RealTime.csproj Demo.RealTime/
RUN dotnet restore Demo.RealTime/Demo.RealTime.csproj

COPY Demo.RealTime/ Demo.RealTime/
RUN dotnet publish Demo.RealTime/Demo.RealTime.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Demo.RealTime.dll"]
