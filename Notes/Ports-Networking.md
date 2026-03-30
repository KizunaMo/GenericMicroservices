# Port 與網路連線完整筆記

---

## 一、Port 是什麼？

一台電腦有一個 IP 位址，但可以同時跑很多程式（Server）。
Port 是用來區分「這個連線要給哪個程式」的號碼。

```
你的電腦（192.168.1.10）
├── Port 5000  → Gateway
├── Port 5100  → AuthService
├── Port 5128  → DataService（REST）
├── Port 5129  → DataService（gRPC）
└── Port 5432  → PostgreSQL
```

一個 Port 同時只能被一個程式佔用。兩個程式搶同一個 Port → 啟動失敗。

---

## 二、Bind（綁定）：監聽哪個介面？

程式啟動時要宣告：「我要監聽哪個 IP 的哪個 Port？」這個動作叫做 **bind**。

有三種選擇：

| 綁定方式 | 實際位址 | 誰可以連進來 |
|----------|----------|-------------|
| `localhost` / `127.0.0.1` | 只有自己這台機器 | 同一台機器上的程式 |
| `0.0.0.0` | 所有介面 | 同一台機器 + 同一網路的其他機器 |
| 指定 IP（例如 `192.168.1.10`） | 特定網路卡 | 透過那個 IP 連進來的 |

### 對應 .NET 的寫法

```csharp
// 只綁 127.0.0.1 → 只有本機可以連
options.ListenLocalhost(5128);

// 綁 0.0.0.0 → 所有人可以連（包含其他容器、其他機器）
options.ListenAnyIP(5128);
```

---

## 三、本次遇到的問題：ListenLocalhost 在 Docker 裡失效

### 情境

```
[ Gateway 容器 ]  →  http://data-service:5128  →  [ DataService 容器 ]
```

DataService 用了 `ListenLocalhost(5128)`，等於只監聽自己容器內的 `127.0.0.1:5128`。

Gateway 容器是另一個獨立的容器，它連到 `data-service:5128` 時，走的是 Docker 內部網路，**不是** DataService 容器的 `127.0.0.1`。所以連不到 → 502。

### 修法

```csharp
// 改成這樣，綁定所有介面，Docker 容器間才能連
options.ListenAnyIP(5128);
```

### 規則

> **在 Docker 裡，服務如果要被其他容器連到，一定要用 `ListenAnyIP` 或 `0.0.0.0`，不能用 `ListenLocalhost`。**

---

## 四、Port 寫法詳解：冒號前後是什麼意思？

```
ports:
  - "5010:8080"
     ^^^^  ^^^^
     左邊  右邊
     本機  容器內
```

**左邊（冒號前）= 你的 Mac 開的門**
你在 Mac 上打開瀏覽器或 Postman，連的就是這個 Port。
這個數字可以自己決定，只要沒被佔用就行。

**右邊（冒號後）= 容器內程式監聽的 Port**
容器裡面的程式（例如 Gateway）實際在監聽哪個 Port。
這個數字要跟程式的設定一致，不能亂填。

### 用「轉接頭」來理解

```
你（Postman）
    ↓
  5010（Mac 的門）
    ↓  Docker 把流量轉過去
  8080（容器內的門，程式在這裡聽）
    ↓
  Gateway 程式
```

你連 `localhost:5010`，Docker 幫你把流量轉到容器內的 `8080`。
對你來說看到 5010，對容器裡的程式來說看到 8080，中間 Docker 透明轉接。

### 範例對照

| 寫法 | 意思 |
|------|------|
| `"5010:8080"` | Mac 的 5010 → 容器內的 8080 |
| `"5432:5432"` | Mac 的 5432 → 容器內的 5432（兩邊一樣） |
| `"5433:5432"` | Mac 的 5433 → 容器內的 5432（左邊改掉，右邊不變）|
| `"15672:15672"` | Mac 的 15672 → 容器內的 15672 |

### 場景：本機 PostgreSQL 和 Docker PostgreSQL 衝突

```
本機 PostgreSQL（Homebrew）→ 佔用 Mac 的 5432
Docker PostgreSQL          → 預設也想用 Mac 的 5432 → 衝突！
```

解法：把 Docker postgres 的左邊（Mac 的門）改成 5433：

```yaml
postgres:
  ports:
    - "5433:5432"   # Mac 的 5433 → 容器內的 5432
```

容器內的 PostgreSQL 程式完全不知道外面發生了什麼，它依然監聽 5432。
只有要從 Mac 直接連 Docker 的 postgres 時，才需要用 5433。

**重要**：容器間互連不受影響。
data-service 容器連 postgres 時，走 Docker 內部網路，直接用 `postgres:5432`，
不需要經過 Mac，所以 `5433:5432` 的設定對它完全沒影響。

```
Mac（你）→ localhost:5433 → Docker 轉接 → postgres 容器:5432  ✅
data-service 容器 → postgres:5432（Docker 內部網路，不走 Mac）✅
```

---

## 五、Docker 的 Port 有兩種概念

### 4-1. 容器內部 Port（Container Port）

程式在容器裡監聽的 Port。這個 Port 只存在於 Docker 內部網路。

例如：DataService 監聽 5128，Gateway 監聽 8080。

### 4-2. 對外 Port（Host Port / Published Port）

用 `ports:` 宣告後，本機（Mac）才能連進去。

```yaml
gateway:
  ports:
    - "5010:8080"   # 本機 5010 → 容器內 8080
```

**沒有 `ports:` 的服務，本機連不到，但 Docker 內部其他容器可以連。**

### 範例：本專案的 Port 全覽

```
你的 Mac（本機）               Docker 對外 Port     容器內 Port
─────────────────────────────────────────────────────────────────
Postman/瀏覽器 → localhost:5010  ──[5010:8080]──►  gateway:8080
瀏覽器         → localhost:15672 ──[15672:15672]► rabbitmq:15672
TCP Client     → localhost:5400  ──[5400:5400]──►  tcp-service:5400
Prometheus UI  → localhost:9090  ──[9090:9090]──►  prometheus:9090
Grafana UI     → localhost:3000  ──[3000:3000]──►  grafana:3000
Jaeger UI      → localhost:16686 ──[16686:16686]► jaeger:16686

─────────────────────────────────────────────────────────────────
Docker 內部網路（容器間互連，本機無法直接連）

gateway:8080
  ├──► auth-service:8080       （POST /auth/**）
  ├──► data-service:5128       （GET/POST /api/**，REST）
  └──► realtime-service:8080   （WebSocket /hub/**）

grpc-service:8080
  └──► data-service:5129       （gRPC，HTTP/2）

data-service:5128
  ├──► postgres:5432            （EF Core，demo_db）
  └──► rabbitmq:5672            （MassTransit，發布事件）

auth-service:8080
  └──► postgres:5432            （EF Core，auth_db）

grpc-service:8080
  └──► postgres:5432            （EF Core，grpc_db）

worker（無對外 port）
  └──► rabbitmq:5672            （MassTransit，消費事件）

prometheus:9090
  ├──► gateway:8080/metrics     （每 15 秒抓取）
  ├──► auth-service:8080/metrics
  ├──► data-service:5128/metrics
  ├──► realtime-service:8080/metrics
  └──► grpc-service:8080/metrics

grafana:3000
  └──► prometheus:9090          （PromQL 查詢）

所有 .NET 服務（各自）
  └──► jaeger:4317              （OTLP gRPC，Push Trace 資料）
```

---

## 五、Port 被佔用：常見原因與排查

### 錯誤訊息

```
address already in use
port is already allocated
Bind for 0.0.0.0:5000 failed
```

### 排查方法

```bash
# 查誰佔用了某個 Port（例如 5000）
lsof -i :5000

# 查所有 Docker 容器正在用的 Port
docker ps

# 查所有正在監聽的 Port（含非 Docker）
netstat -an | grep LISTEN
```

### 常見佔用原因

| Port | 常見佔用者 |
|------|-----------|
| 5000 | macOS AirPlay Receiver / ControlCenter |
| 5432 | 本機 PostgreSQL（Homebrew 安裝的） |
| 80   | 本機 Nginx / Apache |
| 443  | 同上 |
| 3000 | Node.js 開發伺服器 |

### 本次遇到的：macOS AirPlay Receiver 佔用 5000

解法：把 Gateway 的 `ports` 改為 `5010:8080`，避開 5000。

永久解法：系統設定 → 一般 → AirDrop 與接力 → 關閉 AirPlay 接收器。

---

## 六、`appsettings.Production.json` 怎麼解決環境差異

### 問題

開發時服務在本機跑，位址是 `localhost:5128`。
Docker 裡服務在不同容器，位址是 `data-service:5128`。

### 解法：Production 設定覆蓋

```
Demo.Gateway/
├── appsettings.json              ← 預設（開發用）
│     data-service → localhost:5128
└── appsettings.Production.json   ← Production 覆蓋（Docker 用）
      data-service → data-service:5128
```

Docker 容器的環境變數 `ASPNETCORE_ENVIRONMENT=Production`（.NET 8 預設），所以啟動時自動載入 Production 版本，覆蓋掉 localhost 的設定。

### 載入優先順序（高蓋低）

```
環境變數
  > User Secrets（開發機）
    > appsettings.Production.json（或對應環境）
      > appsettings.json
```

---

## 七、gRPC 的兩個 Port：為什麼需要拆開？

DataService 有兩個 Port：

| Port | 協議 | 用途 |
|------|------|------|
| 5128 | HTTP/1.1 | REST API（瀏覽器、Postman、Gateway） |
| 5129 | HTTP/2   | gRPC（只給 GrpcService 呼叫） |

HTTP/1.1 和 HTTP/2 協議本身可以在同一個 Port 上共存（稱為 ALPN 協商），但需要 HTTPS。
開發環境為了簡化（不用處理 TLS），直接拆成兩個 Port 各自獨立運作。

**正式環境**：通常透過 TLS 的 ALPN 讓 HTTP/1.1 和 HTTP/2 共用同一個 Port（443）。

---

## 八、未來可能遇到的 Port 相關問題

### 場景 1：新增服務忘記更新 Production 設定

症狀：Docker 裡 Gateway 連到新服務 502。
排查：檢查 `appsettings.Production.json` 有沒有對應的 Cluster Address。

### 場景 2：服務用 ListenLocalhost，Docker 容器間連不到

症狀：服務在 Docker Desktop 顯示 running，但其他服務連它就 502。
排查：確認 `ConfigureKestrel` 是否用了 `ListenLocalhost`，改為 `ListenAnyIP`。

### 場景 3：忘記開 ports，本機連不到

症狀：在本機 curl / Postman 連不到某個服務，但容器間互連正常。
排查：確認 docker-compose.yml 那個服務有沒有 `ports:` 設定。

### 場景 4：兩個服務搶同一個容器內 Port

症狀：`docker compose up` 時某個服務一直 restart 或 exit。
排查：`docker logs <container名稱>`，看有沒有 `address already in use`。

### 場景 5：本機和 Docker 的 DB 衝突

症狀：Docker 的 postgres 起不來，Port 5432 already in use。
原因：本機 Homebrew 安裝的 PostgreSQL 也在跑。
解法：`brew services stop postgresql@16` 停掉本機 DB，或把 Docker 的 postgres 改成 `5433:5432`。

---

## 九、快速指令參考

```bash
# 查某個 Port 被誰佔用
lsof -i :5000

# 停掉佔用 Port 的程式（把上面查到的 PID 填進去）
kill -9 <PID>

# 查所有正在跑的 Docker 容器和它們的 Port
docker ps

# 查某個容器的 log（排查連線失敗）
docker logs genericmicroservices-data-service-1

# 進入容器內部確認服務在監聽哪個 Port
docker exec -it genericmicroservices-data-service-1 sh
# 在容器內執行：
netstat -tlnp
```
