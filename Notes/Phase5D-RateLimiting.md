# Phase 5-D：Rate Limiting

## 為什麼需要 Rate Limiting？

沒有流量限制的 API 面臨兩種威脅：

```
暴力攻擊（Brute Force）
    攻擊者用程式每秒送數百次登入請求，嘗試猜出密碼
    → 沒有限制的話，只要時間夠長就能成功

DDoS（Distributed Denial of Service）
    大量請求同時打進來，讓服務資源耗盡無法回應正常請求
    → 服務掛掉，所有使用者都無法使用
```

Rate Limiting 的作用：

```
每個 IP 在一段時間內只能送 N 次請求
超過上限 → 立即回傳 HTTP 429 Too Many Requests
合法使用者不受影響（正常使用根本不會觸發上限）
```

---

## 本專案的策略

| Policy 名稱 | 套用路由 | 限制規則 | 防護對象 |
|-------------|----------|----------|----------|
| `"login"` | `POST /auth/login` | 每 IP 每 60 秒最多 5 次 | 暴力攻擊 |
| `"global"` | 所有其他路由 | 每 IP 每 1 秒最多 20 次 | DDoS |

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.Gateway/Program.cs` | 修改 | 註冊 Rate Limiting 服務、加入 Middleware |
| `Demo.Gateway/appsettings.json` | 修改 | YARP 路由設定加上 `RateLimiterPolicy` |

Rate Limiting 加在 Gateway 的原因：所有流量的唯一入口，在這裡擋一次，後面所有服務都受到保護，不需要各服務重複設定。

---

## 實作步驟

### 步驟 1：在 Program.cs 加入 using

**目的**：引入 Rate Limiting 相關的命名空間

**修改檔案**：`Demo.Gateway/Program.cs`（最上方）

```csharp
using System.Threading.RateLimiting;           // FixedWindowRateLimiterOptions、QueueProcessingOrder
using Microsoft.AspNetCore.RateLimiting;       // AddPolicy 擴充方法
```

**為什麼**：.NET 8 內建 Rate Limiting，這兩個 namespace 是標準函式庫，不需要額外安裝 NuGet 套件。

---

### 步驟 2：註冊 Rate Limiting 服務

**目的**：定義兩個 Policy，指定每個 Policy 的限制規則

**修改檔案**：`Demo.Gateway/Program.cs`，放在 `builder.Services.AddAuthorization()` 後面

```csharp
builder.Services.AddRateLimiter(options =>
{
    // 超過限制時回傳的 HTTP 狀態碼
    // 429 = Too Many Requests，這是 Rate Limiting 的標準回應碼
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Policy 一：登入端點專用，防止暴力攻擊
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // partitionKey：以 IP 為單位計數，不同 IP 各自獨立，互不影響
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 5,                        // 同一個時間窗格內最多允許 5 次
                Window               = TimeSpan.FromSeconds(60), // 時間窗格 = 60 秒
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0                         // 不排隊，超過立刻拒絕
            }));

    // Policy 二：所有路由的一般保護，防止 DDoS
    options.AddPolicy("global", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 20,                      // 每秒最多 20 次
                Window               = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            }));
});
```

**重要欄位說明**：

| 欄位 | 說明 |
|------|------|
| `partitionKey` | 計數的單位，用 IP 表示「每個 IP 各自計算次數」 |
| `PermitLimit` | 時間窗格內最多允許幾次請求 |
| `Window` | 時間窗格長度（到期後計數歸零，重新開始計算）|
| `QueueLimit = 0` | 超過上限直接拒絕，不讓請求排隊等待 |

**Fixed Window 是什麼**：
```
固定時間窗格（Fixed Window）算法：
    00:00:00 ~ 00:01:00  → 這個窗格最多 5 次
    第 6 次在這個窗格內 → 429
    00:01:00（下個窗格開始）→ 計數歸零，再允許 5 次
```

---

### 步驟 3：在 Middleware 管道加入 UseRateLimiter

**目的**：讓 Rate Limiting 實際生效，攔截請求

**修改檔案**：`Demo.Gateway/Program.cs`，放在 `app.UseCors()` 之後、`app.UseAuthentication()` 之前

```csharp
app.UseCors(corsPolicy);
app.UseRateLimiter();      // ← 加在這裡
app.UseAuthentication();
app.UseAuthorization();
```

**為什麼放在這個位置**：

```
UseCors         → 先處理 CORS，不相關的請求在這裡就能被排除
UseRateLimiter  → 在身份驗證之前限流，節省後面的驗證資源
UseAuthentication / UseAuthorization → 已通過限流的請求才進行身份驗證
```

如果 Rate Limiting 放在後面，攻擊者的大量請求還是會消耗 JWT 驗證的資源，效果打折。

---

### 步驟 4：在 YARP 路由設定 RateLimiterPolicy

**目的**：指定每條路由要套用哪個 Policy

**修改檔案**：`Demo.Gateway/appsettings.json`

原本 `auth-service-route` 匹配所有 `/auth/**`，需要拆成兩條路由，讓 `/auth/login` 用嚴格的 `"login"` Policy：

```json
"Routes": {
  "auth-login-route": {
    "ClusterId": "auth-service-cluster",
    "RateLimiterPolicy": "login",         // 套用 login policy（5次/60秒）
    "Match": {
      "Path": "/auth/login",
      "Methods": [ "POST" ]               // 只匹配 POST，GET 不受此路由影響
    }
  },
  "auth-service-route": {
    "ClusterId": "auth-service-cluster",
    "RateLimiterPolicy": "global",        // 其他 auth 路由用一般保護
    "Match": {
      "Path": "/auth/{**remainder}"
    }
  },
  "data-service-route": {
    "ClusterId": "data-service-cluster",
    "AuthorizationPolicy": "Default",
    "RateLimiterPolicy": "global",        // 同一條路由可以同時有授權 + 限流
    "Match": {
      "Path": "/api/{**remainder}"
    }
  },
  "realtime-service-route": {
    "ClusterId": "realtime-service-cluster",
    "AuthorizationPolicy": "Default",
    "RateLimiterPolicy": "global",
    "Match": {
      "Path": "/hub/{**remainder}"
    }
  }
}
```

**為什麼要拆兩條路由**：

YARP 的路由是「第一個匹配的路由勝出」。如果只有一條 `/auth/**`，所有 auth 請求都會用同一個 Policy，無法給 `/auth/login` 更嚴格的限制。
把 `/auth/login` 拆成獨立路由（且指定 `Methods: POST`），讓它優先匹配，用 `"login"` Policy。其他 `/auth/**` 退到後面的路由，用 `"global"` Policy。

---

## 最終 Program.cs 完整結構

```csharp
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// JWT 驗證
builder.Services.AddAuthentication(...).AddJwtBearer(...);
builder.Services.AddAuthorization();

// Rate Limiting
builder.Services.AddRateLimiter(options => { ... });

// YARP
builder.Services.AddReverseProxy().LoadFromConfig(...);

// CORS
builder.Services.AddCors(...);

var app = builder.Build();

app.UseCors(corsPolicy);
app.UseRateLimiter();       // ← 限流在身份驗證之前
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy().RequireCors(corsPolicy);

app.Run();
```

---

## 驗證方式

用 curl 或 Postman 快速發送超過上限的請求：

```bash
for i in {1..7}; do
  curl -s -o /dev/null -w "%{http_code}\n" \
    -X POST http://localhost:5000/auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"wrong"}'
done
```

預期輸出（前 5 次回 401 帳密錯誤，第 6、7 次觸發限流回 429）：

```
401
401
401
401
401
429
429
```

---

## 專有名詞

| 名詞 | 說明 |
|------|------|
| Rate Limiting | 限制請求頻率的機制，超過上限拒絕服務 |
| Fixed Window | 固定時間窗格算法，窗格結束後計數重置 |
| partitionKey | Rate Limiting 的計數單位，這裡用 IP 區分不同來源 |
| HTTP 429 | Too Many Requests，Rate Limiting 的標準拒絕狀態碼 |
| DDoS | Distributed Denial of Service，透過大量請求讓服務掛掉 |
| Brute Force | 暴力攻擊，不斷嘗試不同密碼直到成功 |
| QueueLimit | 超過上限後允許排隊等待的請求數，0 = 不排隊直接拒絕 |
| Middleware 管道 | ASP.NET Core 處理請求的有序步驟鏈，順序很重要 |