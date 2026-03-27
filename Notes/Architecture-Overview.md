# GenericMicroservices 架構設計文件

這份文件說明本專案的整體架構、每個服務的職責、設計決策的理由，
以及未來如何維護與擴充。

---

## 整體架構圖

```
                    ┌─────────────────────────────┐
                    │        外部 Client           │
                    │  瀏覽器 / Postman / Unity    │
                    └──────────────┬──────────────┘
                                   │ HTTP (REST / WebSocket)
                                   ▼
          ┌────────────────────────────────────────────────┐
          │                 Demo.Gateway  :5000            │
          │  ・唯一對外 HTTP 入口                            │
          │  ・JWT 驗證（/api/**、/hub/** 需要 Token）       │
          │  ・CORS 集中處理（解決瀏覽器跨域問題）              │
          │  ・YARP 反向代理（根據路徑轉發到對應服務）           │
          └──────────────────┬─────────────────────────────┘
                             │
       ┌─────────────────────┼────────────────┐
       │ /auth/**            │ /api/**         │ /hub/**
       ▼                     ▼                 ▼
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────┐
│ Demo.AuthService │  │ Demo.DataService │  │    Demo.RealTime      │
│ :5100            │  │ :5128 (REST)     │  │  :5200 (WebSocket)    │
│ ・登入 → 簽發JWT  │  │ :5129 (gRPC)     │  │  ・SignalR Hub        │
│ ・bcrypt 密碼驗證 │  │ ・Items CRUD API │  │  ・即時雙向推播        │
│ ・EF Core        │  │ ・EF Core        │  └──────────────────────┘
│ ・auth_db        │  │ ・demo_db        │
└──────────────────┘  └────────▲─────────┘
                               │ gRPC 服務間直連（不走 Gateway）
                               │
                    ┌──────────┴──────────┐
                    │   Demo.GrpcService  │
                    │   :5300 (HTTP/2)    │
                    │   ・高效能服務間通訊  │
                    │   ・EF Core         │
                    │   ・grpc_db         │
                    └─────────────────────┘


┌──────────────────────────────────────────┐
│             Demo.TcpService  :5400       │
│  ・完全獨立，不經過 Gateway               │
│  ・TCP Socket Raw，自訂封包協議           │
│  ・Category + SubType 兩層訊息分類        │
│  ・Watchdog 心跳超時偵測                  │
│  對象：硬體設備 / 遊戲 Client（TCP 直連） │
└──────────────────────────────────────────┘
```

---

## 各服務職責

| 專案                 | Port            | 協議             | 職責                                     |
|----------------------|-----------------|------------------|------------------------------------------|
| Demo.Gateway         | 5000            | HTTP/1.1         | 對外入口，JWT 驗證（不簽發）、CORS、路由轉發 |
| Demo.AuthService     | 5100            | HTTP/1.1         | 使用者管理，登入驗證，簽發 JWT Token      |
| Demo.DataService     | 5128 / 5129     | HTTP/1.1 / HTTP/2| Items CRUD，連接 demo_db                 |
| Demo.RealTime        | 5200            | WebSocket        | SignalR Hub，即時雙向推播                |
| Demo.GrpcService     | 5300            | HTTP/2           | gRPC 服務，連接 grpc_db                  |
| Demo.TcpService      | 5400            | TCP Raw          | 自訂協議，硬體設備／遊戲 Client 場景      |

測試用專案（非服務，不部署）：

| 專案                  | 職責                                           |
|-----------------------|------------------------------------------------|
| Demo.GrpcTestConsole  | gRPC Client 測試，程式碼參考用                 |
| Demo.TcpTestConsole   | TCP Client 測試，程式碼參考用                  |
| Demo.Contracts        | RabbitMQ 共用訊息合約（事件型別定義）          |
| Demo.Worker           | RabbitMQ Consumer，背景訂閱服務                |

---

## 設計決策與理由

### 為什麼用 Gateway 當唯一入口？

沒有 Gateway 的情況：
```
Client → DataService  :5128  （要知道這個 Port）
Client → RealTime     :5200  （要知道這個 Port）
Client → GrpcService  :5300  （要知道這個 Port）

問題：
  ・每個服務都要各自處理 CORS
  ・每個服務都要各自驗證 JWT
  ・Port 改了，Client 要跟著改
```

有 Gateway 之後：
```
Client → Gateway :5000（只需要知道這一個）
  Gateway 負責：路由轉發、JWT 驗證、CORS
  內部服務：假設進來的請求已通過驗證，不重複處理
```

### 為什麼各服務各自獨立 DB？

微服務核心原則：**每個服務擁有自己的 DB，不跨越服務邊界直接存取**。

```
錯誤做法（DB 共用）：
  DataService ──┐
                ├──► 同一個 DB
  GrpcService ──┘
  問題：schema 改動影響所有服務，服務間產生隱性耦合

正確做法（DB 隔離）：
  DataService  ──► demo_db
  GrpcService  ──► grpc_db
  OrderService ──► order_db  （未來）
  服務間需要資料 → 透過 API 或 gRPC 溝通，不直接查對方的 DB
```

### 為什麼 gRPC 和 TCP 不走 Gateway？

```
gRPC：服務間通訊，不面向外部 Client，直連效率更高
TCP Raw：YARP 只能代理 HTTP 流量，無法處理 TCP
```

| 情境                      | 通訊方式  | 走 Gateway？ | 理由                           |
|---------------------------|-----------|-------------|--------------------------------|
| 瀏覽器 / Postman 呼叫 API | REST      | 是           | 需要統一驗證和 CORS 處理        |
| 即時推播給瀏覽器            | SignalR   | 是（可選）  | HTTP 升級協議，Gateway 可代理   |
| 服務間呼叫                  | gRPC      | 否           | 內部直連，不需對外暴露           |
| 硬體設備 / 遊戲 Client      | TCP Raw   | 否           | YARP 無法代理 TCP 流量          |

### 通訊協議選擇原則

```
對外（面向外部 Client）
  ├── 一般 API 呼叫          → REST / HTTP（透過 Gateway）
  └── 即時雙向推播            → SignalR / WebSocket

服務間（內部溝通）
  ├── 同步呼叫（需要等回應）  → gRPC（強型別、高效能）
  └── 非同步（不需等待）      → RabbitMQ（解耦、容錯）

特殊場景
  └── 硬體設備 / 自訂協議    → TCP Socket Raw
```

---

## 如何新增一個服務

以新增 OrderService 為例，步驟如下：

```
1. 建立新專案
   dotnet new webapi -n Demo.OrderService

2. 建立獨立 DB
   PostgreSQL 建立 order_db

3. 實作 CRUD API
   照 Demo.DataService 的結構：
   ・Core/Repositories/（IRepository<T> + Repository<T>）
   ・Features/Orders/（Order.cs + OrderRepository.cs）
   ・Data/AppDbContext.cs

4. 在 Gateway 加路由（appsettings.json）
   /api/orders/** → http://localhost:5130

5. 加入 Solution
   dotnet sln add Demo.OrderService
```

不影響其他任何服務。

---

## 資料庫一覽

| DB 名稱   | 擁有者            | 資料表          |
|-----------|-------------------|-----------------|
| demo_db   | Demo.DataService  | Items           |
| grpc_db   | Demo.GrpcService  | GrpcItems       |

---

## 學習路徑對應

| Phase   | 內容                          | 對應專案                            | 狀態 |
|---------|-------------------------------|-------------------------------------|------|
| Phase 1 | REST API + EF Core + PostgreSQL | Demo.DataService                   | ✅   |
| Phase 2 | API Gateway + YARP             | Demo.Gateway                        | ✅   |
| Phase 3-A | SignalR 即時通訊             | Demo.RealTime                       | ✅   |
| Phase 3-B | RabbitMQ 非同步訊息          | Demo.Contracts + Demo.Worker        | ✅   |
| Phase 4-A | gRPC 服務間通訊              | Demo.GrpcService                    | ✅   |
| Phase 4-B | TCP Socket Raw               | Demo.TcpService                     | ✅   |
| Phase 5-A | JWT 身份驗證                 | Demo.Gateway                        | 🔄   |
| Phase 5-B | HTTPS / TLS                  | 所有服務                            | ⬜   |
| Phase 5-C | Secrets 管理                 | 所有服務                            | ⬜   |
| Phase 5-D | Rate Limiting                | Demo.Gateway                        | ⬜   |
| Phase 6   | Docker 容器化 + Compose      | 所有服務                            | ⬜   |
| Phase 7   | 可觀測性（Log/Trace/Metrics）| 所有服務                            | ⬜   |
