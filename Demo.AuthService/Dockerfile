# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Demo.AuthService/Demo.AuthService.csproj Demo.AuthService/
RUN dotnet restore Demo.AuthService/Demo.AuthService.csproj

COPY Demo.AuthService/ Demo.AuthService/
RUN dotnet publish Demo.AuthService/Demo.AuthService.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Demo.AuthService.dll"]