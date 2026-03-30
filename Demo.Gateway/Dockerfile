# ── Stage 1：Build ────────────────────────────────────────────
# sdk:8.0 含完整編譯器，用來把 .cs 編譯成二進位
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# 先只複製 .csproj，讓 Docker 快取 restore 結果
# 只有 .csproj 變動時才重新 restore（套件沒變就不重跑，加速 build）
COPY Demo.Gateway/Demo.Gateway.csproj Demo.Gateway/
RUN dotnet restore Demo.Gateway/Demo.Gateway.csproj

# 複製所有原始碼，再 publish
COPY Demo.Gateway/ Demo.Gateway/
RUN dotnet publish Demo.Gateway/Demo.Gateway.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
# aspnet:8.0 只含執行環境（沒有編譯器），Image 比 sdk 小很多
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

# 從 Stage 1 複製編譯好的成果（原始碼不會進入這個 Image）
COPY --from=build /app/publish .

# 容器內監聽的 Port（只是文件用途，不會自動對外開放）
EXPOSE 5000
EXPOSE 5001

# 容器啟動時執行的指令
ENTRYPOINT ["dotnet", "Demo.Gateway.dll"]