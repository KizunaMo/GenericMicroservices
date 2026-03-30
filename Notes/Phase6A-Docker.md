# Phase 6-A：Docker 容器化

## 為什麼需要 Docker？

```
沒有 Docker 的問題：
「在我電腦上能跑」≠「在正式伺服器上能跑」
  - 本機裝了 .NET 8、PostgreSQL、RabbitMQ
  - 正式伺服器可能什麼都沒有，或版本不同
  - 環境差異造成不可預期的錯誤

有 Docker：
  Dockerfile → build → Image（程式 + 執行環境的快照）
  Image → 在任何有 Docker 的機器上 run → 行為完全一致
```

---

## 核心概念

| 名詞 | 說明 |
|------|------|
| **Image** | 程式的「快照」，包含程式碼、執行環境、設定。唯讀，不能修改 |
| **Container** | Image 跑起來的「實例」，可以有多個 Container 用同一個 Image |
| **Dockerfile** | 建立 Image 的指令腳本，描述「怎麼打包這個程式」|
| **Build context** | `docker build` 時 Docker 能看到的目錄範圍 |
| **Port mapping** | `-p 本機Port:容器Port`，讓本機能連進容器 |

---

## Multi-Stage Build（多階段建置）

.NET 標準做法，分兩個階段：

```dockerfile
# Stage 1：build（用 SDK Image，有編譯器）
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
# 編譯程式碼...

# Stage 2：runtime（用精簡 Runtime Image，沒有編譯器）
FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY --from=build /app/publish .   # 只複製編譯結果
```

**為什麼要分兩個階段**：
- SDK Image（~900MB）含編譯器，只在 build 時需要
- Runtime Image（~200MB）只含執行環境
- 最終 Image 只有 Runtime stage，大幅縮小體積

---

## .NET 8 在 Docker 的預設 Port

**重要**：`launchSettings.json` 只在開發環境生效，Docker 裡是 Production 環境。

```
開發環境（dotnet run）→ 讀 launchSettings.json → 用你設定的 Port（例如 5000）
Docker Container     → 不讀 launchSettings    → .NET 8 預設 Port：8080
```

所以 Port Mapping 要對應到 **8080**，不是 5000：
```bash
docker run -p 本機Port:8080 image-name   # ✅ 正確
docker run -p 本機Port:5000 image-name   # ❌ 容器內 5000 沒人在聽
```

要改變容器內的 Port，用環境變數：
```bash
docker run -e ASPNETCORE_URLS=http://+:5000 -p 5010:5000 image-name
```

---

## Dockerfile 語法逐行說明

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
```
- `FROM`：這個 Image 的基底是什麼（從哪個環境開始建）
- `mcr.microsoft.com/dotnet/sdk:8.0`：Microsoft 官方 .NET 8 SDK Image，含編譯器
- `AS build`：幫這個 Stage 取名，後面可以用 `--from=build` 引用

```dockerfile
WORKDIR /src
```
- 設定工作目錄，之後所有指令都在 `/src` 裡執行
- 目錄不存在會自動建立（等同 `mkdir -p /src && cd /src`）

```dockerfile
COPY Demo.Gateway/Demo.Gateway.csproj Demo.Gateway/
RUN dotnet restore Demo.Gateway/Demo.Gateway.csproj
```
- `COPY 來源 目的地`：把 build context 裡的檔案複製進 Image
- **先只複製 .csproj**：利用 Docker Layer 快取，只要套件沒變就不重跑 restore（套件下載很慢）
- `RUN`：在 Image 裡執行指令，結果會被保存成一個 Layer

```dockerfile
COPY Demo.Gateway/ Demo.Gateway/
RUN dotnet publish ... -c Release -o /app/publish --no-restore
```
- 再複製所有原始碼（.cs 等）
- `dotnet publish`：編譯並輸出可執行的成果到 `/app/publish`
- `-c Release`：正式版本（效能最佳化，去掉 debug 資訊）
- `--no-restore`：套件已 restore 過，不重複

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
```
- **開始新的 Stage**，拋棄前面的 SDK，改用精簡的 Runtime Image
- `aspnet:8.0`：只含執行環境（沒有編譯器），約 200MB（SDK 約 900MB）
- 最終 Image 只包含這個 Stage 的內容

```dockerfile
WORKDIR /app
COPY --from=build /app/publish .
```
- `COPY --from=build`：從前面的 `build` Stage 複製檔案到這裡
- 只複製編譯結果，原始碼不會進最終 Image（安全 + 體積小）

```dockerfile
EXPOSE 8080
```
- 聲明容器預計使用的 Port（純文件用途，不會自動對外開放）
- 實際開放要靠 `docker run -p` 或 Docker Compose 的 `ports:`

```dockerfile
ENTRYPOINT ["dotnet", "Demo.Gateway.dll"]
```
- Container 啟動時執行的指令
- 等同在 terminal 執行 `dotnet Demo.Gateway.dll`
- 用陣列格式（不用 shell 執行，更直接、更快）

---

## Docker Layer 快取原理

```
Dockerfile 每個 RUN / COPY 都是一個 Layer（層）
Layer 有快取，只有「這層或之前的層」改變時，才重新執行

範例：
  Layer 1: COPY .csproj          ← 套件沒變 → 用快取
  Layer 2: RUN dotnet restore    ← 套件沒變 → 用快取（最慢的這步跳過）
  Layer 3: COPY 所有原始碼       ← .cs 改了 → 重新執行
  Layer 4: RUN dotnet publish    ← 重新執行

結論：.csproj 和原始碼分開 COPY，才能讓 restore 被快取
```

---

## .dockerignore

放在 Solution 根目錄，排除不需要進 build context 的目錄：

```
**/bin/
**/obj/
**/.git/
**/.vs/
**/.idea/
```

**為什麼必須排除 bin/ 和 obj/**：
Dockerfile 先 `dotnet restore`（產生 obj/），再 `COPY 整個目錄`。
如果不排除，本機的 obj/ 會覆蓋容器裡 restore 產生的 obj/，導致 `--no-restore` 找不到套件，build 失敗。

---

## 環境變數 vs User Secrets

| 環境 | Secrets 來源 |
|------|-------------|
| 本機開發（dotnet run）| User Secrets（`~/.microsoft/usersecrets/`）|
| Docker Container | 環境變數（`-e` 參數）|
| Docker Compose | `environment:` 區塊或 `.env` 檔案 |

User Secrets 存在本機家目錄，Container 裡沒有，所以要改用環境變數注入。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.Gateway/Dockerfile` | 新增 | 描述如何建立 Gateway 的 Image |
| `.dockerignore` | 新增 | 排除 bin/、obj/ 等不需要進 Image 的目錄 |

---

## 實作步驟

### 步驟 1：建立 .dockerignore

**位置**：Solution 根目錄（和 .gitignore 同一層）

```
**/bin/
**/obj/
**/.git/
**/.vs/
**/.idea/
```

---

### 步驟 2：建立 Dockerfile

**位置**：各服務的專案目錄下（例如 `Demo.Gateway/Dockerfile`）

```dockerfile
# ── Stage 1：Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# 先只複製 .csproj，讓 Docker 快取 restore 結果
# 只有套件變動時才重新 restore（加速後續 build）
COPY Demo.Gateway/Demo.Gateway.csproj Demo.Gateway/
RUN dotnet restore Demo.Gateway/Demo.Gateway.csproj

# 複製所有原始碼，再 publish
COPY Demo.Gateway/ Demo.Gateway/
RUN dotnet publish Demo.Gateway/Demo.Gateway.csproj -c Release -o /app/publish --no-restore

# ── Stage 2：Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Demo.Gateway.dll"]
```

**注意**：`EXPOSE` 只是文件用途，不會實際開放 Port。

---

## CLI 指令完整參考

### Build Image

```bash
# 在 Solution 根目錄執行
# -t：Image 名稱（tag），格式 名稱:版本，省略版本預設 latest
# -f：指定哪個 Dockerfile
# . ：build context（Docker 能看到的範圍，這裡是整個 Solution）
docker build -t demo-gateway -f Demo.Gateway/Dockerfile .
```

### Run Container

```bash
# --rm：Container 停止後自動刪除（測試用）
# -p 本機Port:容器Port：Port 映射
# -e 'Key=Value'：注入環境變數（用單引號避免 shell 展開特殊字元）
docker run --rm -p 5010:8080 \
  -e 'Jwt__SecretKey=dev-secret-key-must-be-at-least-32-chars!!' \
  demo-gateway
```

**環境變數格式**：`__`（雙底線）對應 appsettings.json 的層級分隔：
```
Jwt__SecretKey         → { "Jwt": { "SecretKey": "..." } }
ConnectionStrings__AuthDb → { "ConnectionStrings": { "AuthDb": "..." } }
```

### 查詢指令

```bash
# 列出所有 Image（含大小、建立時間）
docker images

# 只列出包含 "demo" 的 Image
docker images | grep demo

# 列出正在跑的 Container
docker ps

# 列出所有 Container（包含已停止的）
docker ps -a

# 查看 Container 即時 Log
docker logs <container-id 或 name>

# 持續追蹤 Log（類似 tail -f）
docker logs -f <container-id 或 name>
```

### 停止與刪除

```bash
# 停止 Container（優雅關閉）
docker stop <container-id 或 name>

# 強制停止（等同 kill）
docker kill <container-id 或 name>

# 刪除已停止的 Container
docker rm <container-id 或 name>

# 停止並刪除（一行）
docker rm -f <container-id 或 name>

# 刪除 Image
docker rmi <image-name>

# 刪除所有沒在使用的 Image（清理空間）
docker image prune
```

### 進入 Container 除錯

```bash
# 進入正在跑的 Container，開啟 shell（除錯用）
docker exec -it <container-id 或 name> /bin/bash

# 如果沒有 bash，試 sh
docker exec -it <container-id 或 name> /bin/sh

# 在 Container 裡執行單一指令
docker exec <container-id 或 name> ls /app
```

### 查看 Container 詳細資訊

```bash
# 查看 Container 的完整設定（Port、環境變數、Volume 等）
docker inspect <container-id 或 name>

# 查看 Container 資源使用（CPU、記憶體）
docker stats

# 查看 Image 的 Build 歷史（每個 Layer 的大小）
docker history <image-name>
```

---

## Terminal 與 Docker 的關係

每個 Terminal 視窗是獨立的 Shell，互不影響：

```
Terminal 1（被佔用）          Terminal 2（自由）
────────────────────          ────────────────────
$ docker run demo-gateway     $ docker build ...
  印 log...                   $ curl http://...
  印 log...
  （Ctrl+C 才能停止）
```

**`docker run` 會佔用 Terminal**：
- Container 的所有 log 輸出在這裡
- 無法再輸入其他指令
- 按 `Ctrl+C` → 停止 Container，拿回控制權

所以需要**多個 Terminal 視窗**同時操作：
- Terminal 1：跑 Gateway Container（被佔用）
- Terminal 2：跑 AuthService Container（被佔用）
- Terminal 3：執行 docker build、curl 測試

**macOS 開新 Terminal**：`Cmd+N`（新視窗）或 `Cmd+T`（新分頁）

---

### 背景執行（不佔用 Terminal）

加 `-d`（detached）：

```bash
docker run -d -p 5010:8080 demo-gateway
# Terminal 立刻拿回控制權，Container 在背景跑

docker logs <container-name>   # 看 log
docker stop <container-name>   # 停止
```

**學習階段建議不用 `-d`**，看到即時 log 才能理解服務在做什麼。

---

## 常見錯誤與排除

### 1. `address already in use`

```
Error: ports are not available: exposing port TCP 0.0.0.0:5000
```

**原因**：本機已有程式佔用該 Port。

**排查**：
```bash
sudo lsof -i :5000    # 查是誰在用
```

**解法**：
- 停掉本機服務，或
- 換一個 Host Port：`-p 5010:8080`（macOS 5000 可能被 AirPlay Receiver 佔用）

---

### 2. `key length is zero`

```
System.ArgumentException: IDX10703: Cannot create a SymmetricSecurityKey, key length is zero.
```

**原因**：Container 裡沒有 User Secrets，appsettings.json 的 SecretKey 是空白。

**解法**：用 `-e` 注入環境變數：
```bash
docker run -e 'Jwt__SecretKey=your-secret-key-here' ...
```

---

### 3. `NETSDK1064: Package not found` / `--no-restore` 失敗

```
error NETSDK1064: Package XYZ was not found
```

**原因**：本機的 `obj/` 目錄被 COPY 進容器，覆蓋了 `dotnet restore` 產生的 `obj/`。

**解法**：確認 `.dockerignore` 有加入 `**/obj/` 和 `**/bin/`。

---

### 4. Container 跑起來但連不到（000 或 Connection refused）

**排查步驟**：
```bash
# 1. 確認 Container 真的在跑
docker ps

# 2. 查看 Container Log，確認監聽的 Port
docker logs <container-name>
# 找這行：Now listening on: http://[::]:xxxx

# 3. Port Mapping 要對應容器內實際監聽的 Port
# .NET 8 預設容器 Port 是 8080，不是 5000
docker run -p 本機Port:8080 ...
```

---

### 5. `!!` 在 zsh 被展開

```bash
docker run -e "Jwt__SecretKey=key-with-!!" ...   # ❌ !! 被展開成上個指令
docker run -e 'Jwt__SecretKey=key-with-!!' ...   # ✅ 單引號內不展開
```

**規則**：含有特殊字元（`!`、`$`、`` ` ``）的值，用**單引號**包住。

---

## IDE（Rider）使用建議

| 操作 | 建議工具 |
|------|----------|
| Build Image | CLI（`docker build`）|
| Run 單一 Container | CLI（`docker run`）|
| 查看 Container 狀態 | Docker Desktop GUI |
| 查看 Container Log | Docker Desktop GUI 或 `docker logs` |
| Docker Compose 多服務 | CLI（`docker compose up`）或 Rider（Phase 6-B）|

Rider 對單一 Dockerfile 的 Run 支援不穩定，目前 Phase 先以 CLI 為主。