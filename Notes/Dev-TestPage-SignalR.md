# 開發測試頁面：SignalR Hub 驗證筆記

## 這份筆記記錄什麼？

在補實 RabbitMQ → SignalR 橋接後，用 `wwwroot/index.html` 做瀏覽器驗證時
遇到的三個問題，以及背後的原因和修法。這些問題不是 bug，而是網路與認證概念的盲點。

---

## 問題零：HTTP 404，頁面完全空白（Docker 環境）

### 症狀

`http://localhost:5200` 回傳 HTTP 404，curl 也沒有任何輸出。
但在 Rider 直接跑（`dotnet run`）時沒有這個問題。

### 原因：缺少 `UseDefaultFiles()`

`UseStaticFiles()` 只負責提供靜態檔案，**不會自動把 `/` 對應到 `index.html`**。
需要 `UseDefaultFiles()` 才會讓 `/` → `wwwroot/index.html`。

Rider 直接跑沒遇到是因為 Kestrel 的預設行為略有不同，但 Docker 環境會嚴格按照 Middleware 設定走。

### 修法

```csharp
// Demo.RealTime/Program.cs
app.UseDefaultFiles();  // / → index.html，必須在 UseStaticFiles 之前
app.UseStaticFiles();
```

**順序很重要**：`UseDefaultFiles()` 必須在 `UseStaticFiles()` 前面，
它的作用是把 `/` 改寫成 `/index.html`，再交給 `UseStaticFiles()` 去找檔案。

---

## 問題一：頁面顯示空白（連線失敗，TypeErrorLoad failed）

### 症狀

開啟 `http://localhost:5200`，兩個 Hub 都顯示「連線失敗」，沒有任何事件出現。

### 原因：JS 在瀏覽器執行，`localhost` 是你的 Mac

```
瀏覽器（Mac）開啟 http://localhost:5200
  └─ 下載 index.html（來自 realtime-service 容器）

JS 在瀏覽器（Mac）裡執行：
  └─ 連 http://localhost:5000/hub/chat
                ↑
         這是 Mac 的 localhost
         不是容器內部的網路
```

**HTML 檔案雖然從容器下載，但 JS 執行在你的 Mac 上。**
`localhost` 永遠指向執行 JS 的那台機器（你的 Mac），不是容器。

### Docker dev 的 Gateway port

```yaml
# docker-compose.override.yml
gateway:
  ports:
    - "5010:8080"   ← Mac 的 5010 對應容器的 8080
```

本機 `dotnet run` 時 Gateway 在 `:5000`，Docker dev 環境暴露在 `:5010`。
兩個情境的 port 不同，JS 裡的連線位址要對應調整。

### 修法

```js
// Docker dev → 用 5010
const GATEWAY = "http://localhost:5010";

// 本機 dotnet run → 改為 5000
// const GATEWAY = "http://localhost:5000";
```

---

## 問題二：連線失敗，Status code '401'（Unauthorized）

### 症狀

Port 改對後，錯誤從「Load failed」變成「Unauthorized: Status code '401'」。

### 原因：Gateway 的 `/hub/**` 路由要求 JWT

```json
// Demo.Gateway/appsettings.json
"realtime-service-route": {
    "AuthorizationPolicy": "Default",   ← 需要有效的 JWT
    "Match": { "Path": "/hub/{**remainder}" }
}
```

這是**正確的設計**，不是 bug。Unity 實際連接 SignalR 時也一樣需要先登入取得 JWT，
才能帶著 Token 建立 Hub 連線。

### 解法：加入登入流程，取得 JWT 後再建立 Hub 連線

SignalR JS Client 提供 `accessTokenFactory`，每次連線前自動呼叫取得 Token：

```js
// 先登入
const res = await fetch(`${GATEWAY}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
});
const data = await res.json();
const jwtToken = data.accessToken;

// 建立 Hub 連線，帶入 JWT
const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${GATEWAY}/hub/items`, {
        accessTokenFactory: () => jwtToken   // ← 每次連線前呼叫
    })
    .withAutomaticReconnect()
    .build();
```

### Unity 的對應寫法

```csharp
// Unity（C#）等效寫法
var connection = new HubConnectionBuilder()
    .WithUrl(url, opts =>
    {
        opts.AccessTokenProvider = () => Task.FromResult(jwtToken);
    })
    .Build();
```

概念完全一樣：把取得 Token 的函式傳給 SignalR，它在建立連線時自動帶入
`Authorization: Bearer <token>` Header。

---

## 問題三：登入成功但 TypeError（undefined is not an object）

### 症狀

```
登入失敗：TypeError: undefined is not an object (evaluating 'data.data.accessToken')
```

### 原因：AuthService 的登入回傳格式不是 ApiResponse 包裝

DataService 的 API 回傳統一格式：`ApiResponse<T>` → JS 要用 `data.data.xxx`

但 **AuthService 的 `/auth/login` 直接回傳 plain object**：

```csharp
// Demo.AuthService/Program.cs
return Results.Ok(new
{
    accessToken  = GenerateAccessToken(user),
    refreshToken = refreshToken.Token,
});
```

回傳的 JSON：
```json
{
    "accessToken": "eyJ...",
    "refreshToken": "abc..."
}
```

不是：
```json
{
    "data": { "accessToken": "..." }   ← 這個假設是錯的
}
```

### 修法

```js
// ❌ 錯誤
jwtToken = data.data.accessToken;

// ✅ 正確
jwtToken = data.accessToken;
```

### 為什麼 AuthService 不包 ApiResponse？

AuthService 是安全性服務，登入回應的結構是業界慣例格式，
直接回傳 `{ accessToken, refreshToken }` 讓客戶端容易解析。
統一格式（ApiResponse）主要用於業務 API（DataService），不強制套用到 AuthService。

---

## Rider 可以看到 Docker 的 DB？

是的，Rider 的 Database 工具看到的**就是同一個** Docker postgres 容器的資料。

```
docker-compose.override.yml（開發用，自動載入）：
  postgres:
    ports:
      - "5432:5432"   ← 容器的 5432 暴露到 Mac 的 5432
```

```
你的 Mac
  ├── Rider Database Tool → localhost:5432 ──┐
  └── Docker 容器                            │ 同一個 DB
        └── postgres container:5432 ←────────┘
```

Docker 容器本身是隔離的，但 `ports` 設定讓你從 Mac 直接連進去。
這是開發環境刻意設計的：讓你可以用 Rider、psql、TablePlus 等工具直接檢查資料。
正式環境不會暴露這個 port。

---

## 最終完整流程（驗證成功的路徑）

```
1. docker-compose up --build realtime-service

2. 開啟 http://localhost:5200
   └─ 看到 Step 1 登入區塊

3. 輸入帳號密碼 → 登入
   └─ 呼叫 localhost:5010/auth/login
   └─ 取得 { accessToken, refreshToken }
   └─ 自動建立 ChatHub + ItemHub 連線（帶 JWT）

4. 兩個 Hub 都顯示綠色「已連線」

5. Postman：POST http://localhost:5128/api/items（Header: Authorization: Bearer <token>）

6. Item Hub 框即時出現藍色訊息：
   [02:48:58] OnItemCreated → Id: 2, Name: "測試橋接"

7. RealTime console 也看到：
   [RealTime] 收到 ItemCreated 事件 → Id: 2, Name: 測試橋接，推送 SignalR
```
