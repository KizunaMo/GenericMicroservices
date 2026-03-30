# Phase 6-D：健康檢查（Health Checks）

## 概念說明

### 問題：depends_on 不夠用

```yaml
auth-service:
  depends_on:
    - postgres
```

這樣寫只能等 postgres **Container 啟動**，不能等 PostgreSQL **真正 ready**。
PostgreSQL 啟動後還需要幾秒鐘初始化，這段時間 auth-service 嘗試連 DB 就會失敗。

```
postgres Container 啟動（0.1 秒）
  → auth-service 立刻啟動
  → auth-service 嘗試連 DB → 失敗（PostgreSQL 還在初始化）
  → 幸運的話 EF Core retry 能恢復，不幸就要手動 restart
```

### 解法：Health Check + condition: service_healthy

```
postgres Container 啟動
  → Docker 每 5 秒執行 pg_isready 檢查
  → pg_isready 成功 → postgres 標記為 healthy
  → auth-service 才開始啟動（真正安全）
```

---

## 兩個層面的健康檢查

### 1. .NET 服務的 /health 端點

讓 Docker（或其他監控工具）可以用 HTTP 探測「這個服務是否正常運作」。

```csharp
// 註冊服務
builder.Services.AddHealthChecks();

// 暴露端點
app.MapHealthChecks("/health");
```

呼叫 `GET /health`，回應：
- `200 Healthy`：服務正常
- `503 Unhealthy`：服務異常（例如 DB 連不到）

### 2. docker-compose.yml 的 healthcheck

讓 Docker 知道一個 Container 是否真正 ready，讓其他服務可以等它：

```yaml
postgres:
  healthcheck:
    test: ["CMD-SHELL", "pg_isready -U postgres"]
    interval: 5s    # 每 5 秒檢查一次
    timeout: 5s     # 超過 5 秒沒回應算失敗
    retries: 5      # 連續失敗 5 次才標記為 unhealthy
```

`pg_isready` 是 PostgreSQL 內建的工具，專門用來確認 DB 是否接受連線。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.AuthService/Program.cs` | 修改 | 加入 AddHealthChecks + MapHealthChecks |
| `Demo.DataService/Program.cs` | 修改 | 加入 AddHealthChecks + MapHealthChecks |
| `Demo.Gateway/Program.cs` | 修改 | 加入 AddHealthChecks + MapHealthChecks |
| `Demo.RealTime/Program.cs` | 修改 | 加入 AddHealthChecks + MapHealthChecks |
| `Demo.GrpcService/Program.cs` | 修改 | 加入 AddHealthChecks + MapHealthChecks |
| `docker-compose.yml` | 修改 | postgres 加 healthcheck，depends_on 改用 condition |

---

## 實作步驟

### 步驟 1：各 .NET 服務加入 Health Check（以 AuthService 為例）

```csharp
// builder 階段：註冊服務
builder.Services.AddHealthChecks();

// app 階段：暴露端點
app.MapHealthChecks("/health");
```

所有服務都一樣，只有兩行。不需要額外安裝套件，.NET 8 內建。

---

### 步驟 2：postgres 加入 healthcheck

```yaml
postgres:
  image: postgres:16
  environment:
    - POSTGRES_USER=postgres
    - POSTGRES_PASSWORD=postgres
  volumes:
    - postgres-data:/var/lib/postgresql/data
  healthcheck:
    test: ["CMD-SHELL", "pg_isready -U postgres"]
    interval: 5s
    timeout: 5s
    retries: 5
```

`pg_isready -U postgres`：以 postgres 帳號確認 DB 是否接受連線。

---

### 步驟 3：depends_on 改用 condition

```yaml
# 修改前：只等 Container 啟動
depends_on:
  - postgres

# 修改後：等 postgres healthy 才啟動
depends_on:
  postgres:
    condition: service_healthy
```

三種 condition：

| condition | 意思 |
|-----------|------|
| `service_started` | Container 啟動就好（預設，等同舊寫法）|
| `service_healthy` | 要等 healthcheck 通過才算 ready |
| `service_completed_successfully` | 等 Container 執行完畢退出（適合 migration job）|

---

## 專有名詞

| 名詞 | 說明 |
|------|------|
| `pg_isready` | PostgreSQL 內建工具，回傳 0 表示 DB 接受連線，回傳非 0 表示還沒 ready |
| `CMD-SHELL` | 用 shell 執行指令（相對於 CMD 直接執行二進位）|
| `service_healthy` | depends_on 的條件，表示要等目標服務的 healthcheck 通過 |

---

## 驗證方式

### 驗證 /health 端點（本機）

```bash
# 啟動 AuthService
dotnet run --project Demo.AuthService

# 測試健康檢查
curl http://localhost:5100/health
# 預期回應：Healthy
```

### 驗證 Docker Compose 啟動順序

```bash
docker compose up --build
```

觀察 log，應該看到：
```
postgres-1  | database system is ready to accept connections
# postgres healthcheck 通過後，auth-service 才啟動
auth-service-1 | Now listening on: http://[::]:8080
```

而不是 auth-service 在 postgres ready 前就嘗試連線。
