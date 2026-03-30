# Phase 6-B：Docker Compose 多服務整合

## 概念說明

### 為什麼需要 Docker Compose？

Phase 6-A 用 `docker run` 啟動單一 Container，但實際上有 9 個服務：

```
手動方式（不實際）：
  Terminal 1: docker run gateway...
  Terminal 2: docker run auth-service...
  Terminal 3: docker run data-service...
  ...（9 個 Terminal）
  問題：服務之間用 localhost 根本連不到彼此
```

Docker Compose 解決的核心問題：
1. **一個指令啟動所有服務**：`docker compose up`
2. **服務之間可以用名稱互連**：gateway 可以連到 `auth-service:8080`
3. **統一管理設定**：環境變數、Port、Volume 全部集中在一個 yml 檔案

---

## 核心概念

| 名詞 | 說明 |
|------|------|
| **docker-compose.yml** | 描述「有哪些服務、怎麼跑、怎麼連」的設定檔 |
| **Service** | docker-compose.yml 裡的每一個服務（等於一個 Container）|
| **Docker Network** | Compose 自動建立的虛擬網路，所有服務都在這個網路裡 |
| **Named Volume** | 有名字的持久化儲存空間，Container 重啟後資料不消失 |
| **depends_on** | 指定服務啟動順序 |

---

## docker-compose.yml 語法逐段說明

以本專案的 docker-compose.yml 為例：

### 最外層結構

```yaml
services:       # 所有服務定義在這裡
  gateway:      # 服務名稱（同時也是 DNS 名稱，其他服務可以用這個名稱連線）
    ...
  auth-service:
    ...

volumes:        # 宣告 Named Volume
  postgres-data:
```

---

### `build:` — 從 Dockerfile 建立 Image

```yaml
gateway:
  build:
    context: .                          # build context 的根目錄（. = Solution 根目錄）
    dockerfile: Demo.Gateway/Dockerfile # 指定哪個 Dockerfile
```

- `context: .`：Docker 能「看到」的目錄範圍，這裡是整個 Solution
- `dockerfile:`：相對於 context 的 Dockerfile 路徑
- 每次 `docker compose up --build` 都會重新 build

對比用現成 Image（postgres、rabbitmq 不需要 build）：

```yaml
postgres:
  image: postgres:16    # 直接用 Docker Hub 上的官方 Image，不用自己 build
```

---

### `ports:` — 對外開放 Port

```yaml
gateway:
  ports:
    - "5010:8080"   # "本機Port:容器內Port"
```

- 格式：`"本機Port:容器Port"`
- **只有需要從本機（Mac）直接連的服務才需要設定**
- 容器間互連不需要 `ports:`，透過 Docker 內部網路直接用服務名稱連

### 為什麼容器內是 8080？

`launchSettings.json` 只在本機開發（Development）環境生效，Docker 是 Production 環境，完全不讀它。
.NET 8 容器的內建預設 Port 是 **8080**（.NET 7 以前是 80）。

```
本機 dotnet run  → 讀 launchSettings.json → 用你設定的 Port（例如 5000、5001）
Docker Container → 不讀 launchSettings   → 用 .NET 8 預設：8080
```

所以容器內那側永遠填 **8080**（除非服務用 `ConfigureKestrel` 自訂了 Port，例如 DataService 是 5128）。

本專案對外開放的 Port：

| 服務 | ports 設定 | 原因 |
|------|-----------|------|
| gateway | `5010:8080` | 唯一對外入口，Postman / 瀏覽器連這裡 |
| tcp-service | `5400:5400` | TCP Raw 需要直接連，不走 Gateway |
| rabbitmq | `15672:15672` | RabbitMQ 管理介面（開發時用瀏覽器看） |

其他服務（auth-service、data-service 等）**沒有 `ports:`**，只能透過 Gateway 或其他容器連到。

---

### `environment:` — 環境變數

```yaml
gateway:
  environment:
    - Jwt__SecretKey=dev-secret-key-must-be-at-least-32-chars!!
    - Jwt__Issuer=demo-app
    - Jwt__Audience=demo-app
```

- 格式：`- KEY=VALUE`（清單格式）
- `__`（雙底線）對應 appsettings.json 的巢狀層級：
  ```
  Jwt__SecretKey  →  { "Jwt": { "SecretKey": "..." } }
  ConnectionStrings__DefaultConnection  →  { "ConnectionStrings": { "DefaultConnection": "..." } }
  ```
- 優先順序高於 appsettings.json（會覆蓋）
- **敏感值（密碼、SecretKey）正式環境要改用 .env 檔案或 Secret 管理服務**

---

### `depends_on:` — 啟動順序

```yaml
data-service:
  depends_on:
    - postgres    # postgres 啟動後，才啟動 data-service
    - rabbitmq
```

**重要限制**：`depends_on` 只等 Container **啟動**，不等服務 **ready**。

```
postgres Container 啟動 → data-service 立刻啟動
                          但 PostgreSQL 可能還在初始化！
                          data-service 連 DB 可能失敗
```

解法（進階，Phase 6-D 健康檢查）：

```yaml
depends_on:
  postgres:
    condition: service_healthy   # 等 postgres 真正 ready 才啟動
```

目前的處理：.NET 的 EF Core Migrate() 連線失敗會 retry，通常幾秒內自動成功。

---

### `volumes:` — 資料持久化

```yaml
postgres:
  volumes:
    - postgres-data:/var/lib/postgresql/data   # 把容器內的 DB 資料目錄掛到 Named Volume

volumes:
  postgres-data:   # 宣告這個 Named Volume（不宣告無法使用）
```

**為什麼需要 Volume？**

Container 是「無狀態」的：Container 刪掉，裡面的資料就消失。
Volume 是「宿主機上的儲存空間」，Container 掛上去用，刪掉 Container 資料還在。

```
有 Volume：
  docker compose down → Container 刪掉
  docker compose up   → 新 Container 掛同一個 Volume → 資料還在 ✅

沒有 Volume：
  docker compose down → Container 刪掉，DB 資料消失 ❌
  docker compose up   → 空的 DB，要重新建資料
```

**刪掉 Volume 的指令**（才會真正清掉資料）：

```bash
docker compose down -v   # 停止並刪除所有 Container + Volume
```

---

## Docker 內部網路（最重要的概念）

Docker Compose 啟動時自動建立一個虛擬網路，所有服務都在裡面：

```
[ Docker 內部網路：genericmicroservices_default ]
│
├── gateway         → 可以連到 auth-service:8080
├── auth-service    → 可以連到 postgres:5432
├── data-service    → 可以連到 postgres:5432、rabbitmq:5672
├── worker          → 可以連到 rabbitmq:5672
├── postgres
└── rabbitmq
```

**服務名稱 = DNS 名稱**：在同一個 Compose 裡，`auth-service` 這個名字可以直接解析到對應容器的 IP。

```
本機（Mac）→ 只能連到有 ports: 設定的服務
Docker 內部 → 任何服務都能用名稱連到任何服務
```

---

## appsettings.Production.json — 解決開發 vs Docker 環境差異

### 問題

開發環境在本機跑，所有服務都是 `localhost`：

```json
// appsettings.json（開發用）
"auth-service-cluster": {
  "Address": "http://localhost:5100"
}
```

Docker 裡服務在不同容器，`localhost` 連不到其他容器，要用服務名稱：

```json
// appsettings.Production.json（Docker 用）
"auth-service-cluster": {
  "Address": "http://auth-service:8080"
}
```

### 為什麼 Production.json 能自動生效？

.NET 的 Configuration 系統載入順序：

```
appsettings.json                          ← 基底
appsettings.{環境}.json                   ← 覆蓋基底（同 Key 覆蓋，沒有的 Key 保留）
環境變數                                   ← 覆蓋上面所有
```

Docker 容器的 `ASPNETCORE_ENVIRONMENT` 預設是 `Production`，所以自動載入 `appsettings.Production.json`，其中的 Clusters Address 覆蓋掉 appsettings.json 裡的 localhost。

**實際覆蓋結果**：

```
appsettings.json 的 Routes（路由規則）  ← 保留（Production.json 沒寫 Routes）
appsettings.json 的 Clusters（localhost）← 被 Production.json 的（服務名稱）覆蓋
```

### 本專案的 appsettings.Production.json

```json
{
  "ReverseProxy": {
    "Clusters": {
      "auth-service-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://auth-service:8080"
          }
        }
      },
      "data-service-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://data-service:5128"   ← 注意：不是 8080，DataService 監聽 5128
          }
        }
      },
      "realtime-service-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://realtime-service:8080"
          }
        }
      }
    }
  }
}
```

**為什麼 data-service 是 5128 不是 8080？**

DataService 的 Program.cs 用了 `ConfigureKestrel` 自訂了兩個 Port：

```csharp
options.ListenAnyIP(5128, o => o.Protocols = HttpProtocols.Http1);  // REST
options.ListenAnyIP(5129, o => o.Protocols = HttpProtocols.Http2);  // gRPC
```

手動 `ConfigureKestrel` 會覆蓋 .NET 8 的預設 8080，所以 DataService 不在 8080 上監聽。
其他服務（auth-service、realtime-service）沒有自訂 Kestrel，走預設 8080。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `docker-compose.yml` | 新增 | 定義所有服務、網路、Volume |
| `Demo.Gateway/appsettings.Production.json` | 新增 | Docker 環境的路由位址（服務名稱取代 localhost）|
| `Demo.DataService/Program.cs` | 修改 | `ListenLocalhost` → `ListenAnyIP`，讓其他容器可以連入 |
| `Demo.Worker/Program.cs` | 修改 | RabbitMQ Host 從 Configuration 讀取（不寫死 localhost）|

---

## 實作步驟

### 步驟 1：修改 DataService 的 Kestrel 設定

**目的**：讓 DataService 可以被其他容器連入

**修改檔案**：`Demo.DataService/Program.cs`

```csharp
// 修改前：只綁 127.0.0.1，其他容器連不到
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5128, o => o.Protocols = HttpProtocols.Http1);
    options.ListenLocalhost(5129, o => o.Protocols = HttpProtocols.Http2);
});

// 修改後：綁 0.0.0.0，所有容器都可以連到
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5128, o => o.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(5129, o => o.Protocols = HttpProtocols.Http2);
});
```

**為什麼**：`ListenLocalhost` 只綁定 `127.0.0.1`，這是容器自己的 loopback，其他容器透過 Docker 網路連過來時走的是不同的網路介面，連不到 `127.0.0.1`。`ListenAnyIP` 綁定 `0.0.0.0`（所有介面），Docker 網路的請求也能進來。

---

### 步驟 2：建立 appsettings.Production.json

**目的**：Gateway 在 Docker 裡用服務名稱連到其他服務

**新增檔案**：`Demo.Gateway/appsettings.Production.json`

```json
{
  "ReverseProxy": {
    "Clusters": {
      "auth-service-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://auth-service:8080"
          }
        }
      },
      "data-service-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://data-service:5128"
          }
        }
      },
      "realtime-service-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://realtime-service:8080"
          }
        }
      }
    }
  }
}
```

---

### 步驟 3：建立 docker-compose.yml

**目的**：一個指令啟動所有服務

**新增檔案**：Solution 根目錄 `docker-compose.yml`

```yaml
services:

  # ── Gateway（唯一對外入口）─────────────────────────────────
  gateway:
    build:
      context: .
      dockerfile: Demo.Gateway/Dockerfile
    ports:
      - "5010:8080"      # 本機 5010 → 容器內 8080（5000 被 macOS AirPlay 佔用）
    environment:
      - Jwt__SecretKey=dev-secret-key-must-be-at-least-32-chars!!
      - Jwt__Issuer=demo-app
      - Jwt__Audience=demo-app
    depends_on:
      - auth-service
      - data-service
      - realtime-service

  # ── AuthService ───────────────────────────────────────────
  auth-service:
    build:
      context: .
      dockerfile: Demo.AuthService/Dockerfile
    environment:
      - Jwt__SecretKey=dev-secret-key-must-be-at-least-32-chars!!
      - Jwt__Issuer=demo-app
      - Jwt__Audience=demo-app
      - ConnectionStrings__AuthDb=Host=postgres;Database=auth_db;Username=postgres;Password=postgres
    depends_on:
      - postgres

  # ── DataService ───────────────────────────────────────────
  data-service:
    build:
      context: .
      dockerfile: Demo.DataService/Dockerfile
    environment:
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=demo_db;Username=postgres;Password=postgres
      - RabbitMq__Host=rabbitmq    # Docker 裡 RabbitMQ 用服務名稱，不是 localhost
    depends_on:
      - postgres
      - rabbitmq

  # ── RealTime（SignalR）────────────────────────────────────
  realtime-service:
    build:
      context: .
      dockerfile: Demo.RealTime/Dockerfile

  # ── GrpcService ───────────────────────────────────────────
  grpc-service:
    build:
      context: .
      dockerfile: Demo.GrpcService/Dockerfile
    environment:
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=grpc_db;Username=postgres;Password=postgres
    depends_on:
      - postgres

  # ── Worker（RabbitMQ Consumer）────────────────────────────
  worker:
    build:
      context: .
      dockerfile: Demo.Worker/Dockerfile
    depends_on:
      - rabbitmq

  # ── TcpService ────────────────────────────────────────────
  tcp-service:
    build:
      context: .
      dockerfile: Demo.TcpService/Dockerfile
    ports:
      - "5400:5400"      # TCP Raw，需要對外開放，不走 Gateway

  # ── PostgreSQL ────────────────────────────────────────────
  postgres:
    image: postgres:16
    environment:
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
    volumes:
      - postgres-data:/var/lib/postgresql/data   # 資料持久化

  # ── RabbitMQ ─────────────────────────────────────────────
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "15672:15672"    # 管理介面（瀏覽器 http://localhost:15672，帳密 guest/guest）

volumes:
  postgres-data:   # 宣告 Named Volume，必須在這裡宣告才能使用
```

---

## CLI 指令完整參考

### 啟動

```bash
# 啟動所有服務（前景，看到所有 log）
docker compose up

# 啟動並重新 build 所有 Image（程式碼有改動時用這個）
docker compose up --build

# 背景執行（不佔用 Terminal）
docker compose up -d

# 只啟動特定服務（和它的 depends_on）
docker compose up gateway
```

### 停止

```bash
# 停止所有服務（Container 停止，但 Volume 保留）
docker compose down

# 停止並刪除 Volume（DB 資料會消失！）
docker compose down -v

# 只停止特定服務
docker compose stop auth-service
```

### 查看狀態

```bash
# 查看所有服務的狀態
docker compose ps

# 查看特定服務的 log
docker compose logs auth-service

# 持續追蹤 log（類似 tail -f）
docker compose logs -f auth-service

# 查看所有服務的 log
docker compose logs

# 查看最近 50 行 log
docker compose logs --tail=50
```

### 重建與清理

```bash
# 只重新 build，不啟動
docker compose build

# 只重新 build 特定服務
docker compose build data-service

# 刪除所有停止的 Container（不刪 Volume）
docker compose rm

# 查看 Docker Compose 用到的 Image
docker compose images
```

### 進入容器除錯

```bash
# 進入正在跑的 Container
docker compose exec auth-service /bin/sh

# 在容器內執行指令（不進入互動模式）
docker compose exec data-service dotnet --version
```

---

## 驗證方式

### 步驟 1：啟動

```bash
# 在 Solution 根目錄
docker compose up --build
```

等待所有服務都出現類似：
```
auth-service-1  | info: Microsoft.Hosting.Lifetime[14]
auth-service-1  |       Now listening on: http://[::]:8080
```

### 步驟 2：測試登入（Gateway → AuthService）

```
POST http://localhost:5010/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "admin123"
}
```

預期：HTTP 200，回傳 accessToken 和 refreshToken。

### 步驟 3：測試 API（Gateway → DataService）

```
GET http://localhost:5010/api/items
Authorization: Bearer <上一步拿到的 accessToken>
```

預期：HTTP 200，回傳 items 陣列。

---

## 常見問題與排除

### 1. Port already in use（Port 被佔用）

```
Error: Bind for 0.0.0.0:5010 failed: port is already allocated
```

**原因**：之前 `docker run` 啟動的舊 Container 還在跑，佔用了 5010。

**排查**：
```bash
docker ps                # 找出佔用 Port 的舊 Container
docker stop <container>  # 停掉它
```

**根本原因**：`docker run` 和 `docker compose` 是分開管理的，`docker compose down` 不會停掉 `docker run` 的 Container。

---

### 2. 服務連線失敗（502）

**原因可能有三種**：

A. **Port 設定錯誤**：`appsettings.Production.json` 的 Address 寫錯 Port
```json
// 錯誤
"Address": "http://data-service:8080"   // DataService 不在 8080！

// 正確
"Address": "http://data-service:5128"   // DataService 監聽 5128
```

B. **ListenLocalhost 問題**：服務用 `ListenLocalhost`，其他容器連不到
```csharp
// 錯誤
options.ListenLocalhost(5128, ...);

// 正確
options.ListenAnyIP(5128, ...);
```

C. **忘記建立 Production 設定**：Gateway 還在用 `localhost:5100`，當然連不到其他容器

---

### 3. DB 連線失敗（startup 時）

```
Error: Connection refused / Host not found
```

**原因**：`depends_on` 只等 postgres Container 啟動，不等 PostgreSQL 完全 ready。

**解法（暫時）**：等 10~30 秒後 `docker compose restart auth-service`，通常 EF Core 的 retry 機制能自己恢復。

**正確解法（Phase 6-D）**：加入 Health Check，`depends_on` 改用 `condition: service_healthy`。

---

### 4. Image 名稱不同

`docker compose up` 建的 Image 名稱是：`{專案資料夾名稱}-{服務名稱}`
```
genericmicroservices-gateway
genericmicroservices-auth-service
```

Phase 6-A 手動 `docker build -t demo-gateway` 建的是另一套，兩者互不影響。
後續統一用 Docker Compose，Phase 6-A 的 demo-* Image 可以清掉：

```bash
docker rmi demo-gateway demo-authservice demo-dataservice ...
```
