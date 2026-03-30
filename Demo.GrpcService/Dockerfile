# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY Demo.GrpcService/Demo.GrpcService.csproj Demo.GrpcService/
RUN dotnet restore Demo.GrpcService/Demo.GrpcService.csproj

COPY Demo.GrpcService/ Demo.GrpcService/
RUN dotnet publish Demo.GrpcService/Demo.GrpcService.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Demo.GrpcService.dll"]
