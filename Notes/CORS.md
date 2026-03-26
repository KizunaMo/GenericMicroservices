# CORS 完整筆記

## 什麼是 CORS？

**CORS（Cross-Origin Resource Sharing，跨來源資源共享）** 是瀏覽器的安全機制。

### 為什麼需要它？

瀏覽器有一個預設規則叫做 **Same-Origin Policy（同源政策）**：
> 網頁只能向「同一個來源」的 Server 發請求，不能隨意存取其他來源的資源。

「同一個來源」的定義：**Protocol + Domain + Port 三者都相同**

```
http://localhost:5200/index.html  →  來源是 http://localhost:5200

這個頁面發請求到：
http://localhost:5200/api/data    →  同源 ✅（允許）
http://localhost:5000/api/data    →  跨源 ❌（port 不同，封鎖）
https://localhost:5200/api/data   →  跨源 ❌（protocol 不同，封鎖）
http://example.com/api/data       →  跨源 ❌（domain 不同，封鎖）
```

### 為什麼瀏覽器要這樣做？

防止惡意網站偷取你的資料：

```
你登入了 bank.com（有你的 cookie）
你開啟了 evil.com
evil.com 的 JS 偷偷向 bank.com 發請求
如果沒有 Same-Origin Policy → evil.com 能拿到你的銀行資料！
```

---

## CORS 只影響瀏覽器

這是最重要的概念：

```
瀏覽器（Chrome / Safari）  → 有 Same-Origin Policy → 需要 CORS 設定
Unity（.NET Runtime）       → 沒有 → 直接通
Postman / curl              → 沒有 → 直接通
Server 對 Server            → 沒有 → 直接通
Docker 容器間               → 沒有 → 直接通
```

**CORS 是瀏覽器的限制，不是網路的限制。**

---

## CORS 的運作流程

### 簡單請求（GET / POST）

```
瀏覽器                              Server
  |                                   |
  |--- GET /api/items ---------------->|
  |    Origin: http://localhost:5200   |   自動帶上來源
  |                                   |
  |<-- 200 OK -------------------------|
  |    Access-Control-Allow-Origin:    |   Server 回應允許哪些來源
  |    http://localhost:5200           |
  |                                   |
瀏覽器檢查 → 來源在允許清單 → 放行 ✅
```

### 預檢請求（Preflight）

複雜請求（PUT、DELETE、自訂 Header）瀏覽器會先發一個 OPTIONS 請求詢問：

```
瀏覽器                              Server
  |--- OPTIONS /api/items ------------>|   先問：我可以這樣發嗎？
  |    Origin: http://localhost:5200   |
  |    Access-Control-Request-Method: DELETE
  |                                   |
  |<-- 204 No Content -----------------|
  |    Access-Control-Allow-Origin: * |   Server 回答：可以
  |    Access-Control-Allow-Methods: DELETE
  |                                   |
  |--- DELETE /api/items/1 ----------->|   才真正發請求
  |<-- 204 No Content -----------------|
```

---

## 關鍵 HTTP Header

| Header | 方向 | 說明 |
|---|---|---|
| `Origin` | Request（瀏覽器自動帶） | 請求來自哪個來源 |
| `Access-Control-Allow-Origin` | Response（Server 設定） | 允許哪些來源 |
| `Access-Control-Allow-Methods` | Response | 允許哪些 HTTP 方法 |
| `Access-Control-Allow-Headers` | Response | 允許哪些 Header |
| `Access-Control-Allow-Credentials` | Response | 是否允許帶 cookie / 認證 |

---

## .NET 的 CORS 設定方式

### 基本設定

```csharp
// Program.cs

// 1. 定義 CORS 政策
builder.Services.AddCors(options =>
{
    options.AddPolicy("MyPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:3000")  // 只允許這個來源
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 2. 套用 CORS Middleware（要在 routing 之前）
app.UseCors("MyPolicy");
```

### AllowAnyOrigin vs WithOrigins

```csharp
// ⚠️ 開發用，正式環境不能這樣
policy.AllowAnyOrigin()   // 允許所有來源（*）

// ✅ 正式環境，明確指定
policy.WithOrigins(
    "https://myapp.com",
    "https://admin.myapp.com"
)
```

### AllowCredentials 的限制

```csharp
// SignalR / Cookie 認證需要 AllowCredentials
policy.AllowCredentials()

// ⚠️ 注意：AllowCredentials 和 AllowAnyOrigin 不能並用
// 因為允許所有來源 + 允許帶認證 = 安全漏洞
policy.AllowAnyOrigin().AllowCredentials()  // ❌ 會報錯
policy.WithOrigins("https://myapp.com").AllowCredentials()  // ✅ 正確
```

---

## 微服務架構的正確 CORS 位置

### ❌ 錯誤做法：每個服務各自設定

```
瀏覽器 → DataService（設 CORS）
瀏覽器 → RealTimeService（設 CORS）
瀏覽器 → OrderService（設 CORS）
```

問題：
- 重複設定，新增服務就要再設定一次
- 前端網址變更時，要改所有服務
- 後端服務對外暴露，增加攻擊面

### ✅ 正確做法：CORS 集中在 Gateway

```
瀏覽器
    ↓（跨來源請求）
Gateway（統一處理 CORS）← 只有這裡設定
    ↓（同源，內網）
DataService（不需要 CORS）
RealTimeService（不需要 CORS）
OrderService（不需要 CORS）
```

優點：
- 一個地方管理所有 CORS 規則
- 後端服務不對外，不需要設 CORS
- 前端網址變更只改 Gateway

---

## 我們的實作

### Demo.Gateway/Program.cs

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5200",   // 開發時 HTML 頁面的位址
                "http://localhost:3000"    // 之後 React/Vue 前端
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();        // SignalR 必須
    });
});

app.UseCors();          // 套用 CORS（在 MapReverseProxy 之前）
app.MapReverseProxy();
```

### Demo.RealTime/Program.cs

```csharp
// 後端服務不需要設 CORS，因為瀏覽器只會連 Gateway
// Gateway 統一處理
```

---

## 常見錯誤

### 錯誤 1：CORS policy 錯誤

```
Access to fetch at 'http://localhost:5000' from origin
'http://localhost:5200' has been blocked by CORS policy
```
原因：Server 沒有允許這個來源，或根本沒設 CORS

### 錯誤 2：Preflight 失敗

```
Response to preflight request doesn't pass access control check
```
原因：Server 沒有正確回應 OPTIONS 請求

### 錯誤 3：AllowCredentials 衝突

```
The 'Access-Control-Allow-Origin' header contains the wildcard '*'
which cannot be used when credentials flag is true
```
原因：同時用了 `AllowAnyOrigin()` 和 `AllowCredentials()`

解法：改用 `WithOrigins("具體網址")`
