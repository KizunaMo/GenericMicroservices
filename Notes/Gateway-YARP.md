# Gateway：YARP 反向代理

## 誰讀 appsettings.json？

```
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
```

這行做了兩件事：

```
builder.Configuration.GetSection("ReverseProxy")
    │
    │  ASP.NET Core 內建的 Configuration 系統
    │  自動讀取 appsettings.json，取出 "ReverseProxy" 區塊
    ▼
.LoadFromConfig(...)
    │
    │  YARP 套件提供的方法
    │  接收設定，解析 Routes 和 Clusters
    │  載入到 YARP 的路由引擎
    ▼
Request 進來時，YARP 比對路由、執行轉發
```

ASP.NET Core 的 Configuration 系統會**自動**讀取 `appsettings.json`，
不需要手動開檔。正式環境可用環境變數覆蓋設定值而不改檔案。

---

## Program.cs 和 appsettings.json 的分工

```
Program.cs                          appsettings.json
──────────────────────────────      ──────────────────────────────
「能力的設定」                        「規則的設定」
・告訴程式要啟用哪些功能              ・告訴 YARP 路由怎麼轉發
・JWT 驗證要怎麼驗                   ・哪些路由需要 Token
・CORS 允許哪些來源                  ・轉發到哪個服務的哪個 IP
```

Program.cs 是「機制」，appsettings.json 是「設定」。
**新增或修改路由規則只需要改 appsettings.json，不需要動程式碼。**

---

## Program.cs 逐行解說

```csharp
// ① 註冊 JWT 驗證能力
//    告訴程式：收到 Bearer Token 時，用這把 Key 驗證
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { ... });

// ② 註冊授權能力
//    讓「需要登入」的路由能運作
builder.Services.AddAuthorization();

// ③ 載入 YARP，從 appsettings.json 的 ReverseProxy 區塊讀取路由設定
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ④ 設定 CORS 允許的來源
builder.Services.AddCors(options => { ... });
```

```csharp
// ⑤ Middleware Pipeline（順序很重要）
app.UseCors(corsPolicy);       // 先處理跨域
app.UseAuthentication();       // 解析 Token，確認身份
app.UseAuthorization();        // 根據身份決定能否存取

// ⑥ 啟用 YARP 反向代理
//    .RequireCors(corsPolicy) → 讓每個代理路由都套用 CORS
app.MapReverseProxy().RequireCors(corsPolicy);
```

---

## Middleware Pipeline 順序說明

```
Request 進來
    │
    ▼
UseCors           → 檢查 Origin，回應 OPTIONS preflight
    │
    ▼
UseAuthentication → 讀取 Authorization: Bearer <token>
                    解析 JWT，設定 HttpContext.User
    │
    ▼
UseAuthorization  → 檢查這個 endpoint 需要什麼授權
                    和 HttpContext.User 的身份對照
    │
    ▼
MapReverseProxy   → 根據路徑找到對應的 Route
                    轉發到目標服務
```

**順序不能調換**：Authentication 必須在 Authorization 之前，
否則 UseAuthorization 看不到使用者身份，全部視為匿名。

---

## appsettings.json 結構解說

```json
"ReverseProxy": {
  "Routes": {          ← 路由規則：請求進來時怎麼比對
    "route-name": {
      "ClusterId": "...",           ← 對應到哪個 Cluster（服務群組）
      "AuthorizationPolicy": "...", ← 是否需要驗證（選填）
      "Match": {
        "Path": "/api/{**remainder}", ← 路徑匹配規則
        "Methods": ["GET", "POST"]    ← HTTP 方法限制（選填）
      }
    }
  },
  "Clusters": {        ← 服務群組：實際轉發到哪個 URL
    "cluster-name": {
      "Destinations": {
        "destination1": {
          "Address": "http://localhost:5128"  ← 目標服務的位址
        }
      }
    }
  }
}
```

**Route → Cluster 的關係：**
```
Request: GET /api/items
    │
    │ 比對 Routes，找到 "data-service-route"
    │   Path 符合 /api/{**remainder}
    │   ClusterId = "data-service-cluster"
    │
    ▼
查 Clusters，找到 "data-service-cluster"
    │   Address = http://localhost:5128
    │
    ▼
轉發到 http://localhost:5128/api/items
```

---

## 路徑匹配語法

| 語法               | 說明                                    | 範例匹配                     |
|--------------------|-----------------------------------------|------------------------------|
| `/api/items`       | 完全符合                                | `/api/items` 只這一個         |
| `/api/{id}`        | 單段參數（不含 `/`）                    | `/api/1`、`/api/abc`         |
| `/api/{**remainder}` | 萬用（含後面所有路徑）                | `/api/`、`/api/items/1/edit` |

本專案用 `{**remainder}` 讓所有子路徑都能轉發：
```
/auth/{**remainder} → 匹配 /auth/login、/auth/users、/auth/users/1/password
/api/{**remainder}  → 匹配 /api/items、/api/items/1
```

---

## AuthorizationPolicy 說明

```json
"AuthorizationPolicy": "Default"
```

`"Default"` 是 ASP.NET Core 的預設授權策略，意思是「必須是已登入的使用者」。

| 設定值                      | 效果                                     |
|-----------------------------|------------------------------------------|
| 不設定（省略）              | 允許匿名存取                             |
| `"Default"`                 | 必須帶有效的 JWT Token                   |
| 自訂策略（如 `"AdminOnly"`）| 可設定需要特定 Role 或 Claim            |

---

## 本專案路由總覽

```
/auth/{**remainder}  → AuthService :5100  （不驗 Token，AuthService 自己管）
/api/{**remainder}   → DataService :5128  （需要 Token，Gateway 驗）
/hub/{**remainder}   → RealTime    :5200  （需要 Token，Gateway 驗）
```

---

## 如何新增一條路由

以新增 OrderService 為例：

**appsettings.json 新增：**
```json
"order-service-route": {
  "ClusterId": "order-service-cluster",
  "AuthorizationPolicy": "Default",
  "Match": {
    "Path": "/api/orders/{**remainder}"
  }
}
```

```json
"order-service-cluster": {
  "Destinations": {
    "destination1": {
      "Address": "http://localhost:5130"
    }
  }
}
```

**Program.cs 不需要改任何程式碼。**

---

## 負載平衡（擴充參考）

Cluster 可以設定多個 Destination，YARP 自動分流：

```json
"data-service-cluster": {
  "LoadBalancingPolicy": "RoundRobin",
  "Destinations": {
    "destination1": { "Address": "http://localhost:5128" },
    "destination2": { "Address": "http://localhost:5130" },
    "destination3": { "Address": "http://localhost:5131" }
  }
}
```

三台 DataService 輪流接收請求，這就是水平擴展的基礎。

---

## 完整作業流程圖

### 階段一：程式啟動時（讀設定、建立能力）

```
dotnet run
    │
    ▼
Program.cs 開始執行
    │
    ├─① builder.Configuration
    │       └── 自動讀取 appsettings.json
    │               ├── Jwt.SecretKey / Issuer / Audience
    │               ├── ReverseProxy.Routes
    │               └── ReverseProxy.Clusters
    │
    ├─② builder.Services.AddAuthentication().AddJwtBearer(...)
    │       └── 讀取 Jwt.SecretKey
    │           建立「JWT 驗證器」，知道要用哪把 Key 驗 Token
    │
    ├─③ builder.Services.AddReverseProxy()
    │       .LoadFromConfig(GetSection("ReverseProxy"))
    │           └── YARP 讀取 Routes 設定
    │                   ├── "auth-service-route" → path=/auth/**  → cluster=auth-service-cluster
    │                   ├── "data-service-route" → path=/api/**   → cluster=data-service-cluster（需Token）
    │                   └── "realtime-service-route" → path=/hub/** → cluster=realtime-service-cluster（需Token）
    │               YARP 讀取 Clusters 設定
    │                   ├── auth-service-cluster  → http://localhost:5100
    │                   ├── data-service-cluster  → http://localhost:5128
    │                   └── realtime-service-cluster → http://localhost:5200
    │               建立路由表，存在記憶體
    │
    ├─④ builder.Services.AddCors(...)
    │       └── 建立 CORS 規則，記住允許的來源
    │
    └─⑤ app = builder.Build()
            app.UseCors()         → 把 CORS 功能掛進 Pipeline
            app.UseAuthentication() → 把 JWT 驗證掛進 Pipeline
            app.UseAuthorization()  → 把授權檢查掛進 Pipeline
            app.MapReverseProxy()   → 把 YARP 路由掛進 Pipeline

伺服器啟動，開始監聽 Port 5000
```

---

### 階段二：Request 進來時（每一個請求都走這條路）

以 `GET /api/items`（帶 Token）為例：

```
Client 發送請求
GET http://localhost:5000/api/items
Authorization: Bearer eyJhbGci...
    │
    ▼
Pipeline 開始處理
    │
    ├─① UseCors
    │       檢查 Origin header
    │       是瀏覽器請求才需要過這關
    │       Postman / 服務間呼叫不受 CORS 限制
    │
    ├─② UseAuthentication（JWT 驗證）
    │       讀取 Authorization: Bearer eyJhbGci...
    │       用 Jwt.SecretKey 驗證簽名
    │       驗證 Issuer、Audience、過期時間
    │           ├── 驗證成功 → 解析 Claims，設定 HttpContext.User
    │           │              （User.Identity.Name = "admin", Role = "admin"）
    │           └── 沒有 Token → HttpContext.User = 匿名
    │
    ├─③ UseAuthorization
    │       （此步驟只標記，實際授權在 YARP 路由匹配後）
    │
    └─④ MapReverseProxy（YARP）
            比對 Routes 路由表
                /api/items 符合 "data-service-route"（path=/api/**）
                    │
                    ├── 有 AuthorizationPolicy: "Default"
                    │       檢查 HttpContext.User 是否已驗證
                    │           ├── 已驗證 → 繼續
                    │           └── 匿名   → 回 401/403，不轉發
                    │
                    └── 驗證通過
                            查 Clusters → data-service-cluster
                            目標 → http://localhost:5128
                            轉發請求到 http://localhost:5128/api/items
                                │
                                ▼
                          DataService 處理，回傳結果
                                │
                                ▼
                          YARP 把結果回傳給 Client
```

---

### 階段三：不帶 Token 的請求

以 `GET /api/items`（不帶 Token）為例：

```
GET http://localhost:5000/api/items
（沒有 Authorization header）
    │
    ├─② UseAuthentication
    │       沒有 Token → HttpContext.User = 匿名
    │
    └─④ MapReverseProxy
            比對到 "data-service-route"
            有 AuthorizationPolicy: "Default"
            HttpContext.User 是匿名 → 拒絕
            回傳 401 / 403，不轉發到 DataService
```

---

### 階段四：不需要 Token 的路由

以 `POST /auth/login` 為例：

```
POST http://localhost:5000/auth/login
（沒有 Authorization header）
    │
    ├─② UseAuthentication
    │       沒有 Token → HttpContext.User = 匿名
    │
    └─④ MapReverseProxy
            比對到 "auth-service-route"
            沒有 AuthorizationPolicy → 允許匿名
            查 Clusters → auth-service-cluster
            轉發到 http://localhost:5100/auth/login
                │
                ▼
          AuthService 驗帳密、簽發 Token，回傳給 Client
```
