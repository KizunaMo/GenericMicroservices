# 網路通訊協議完整筆記

## 核心概念：Socket 是什麼？

**Socket 不是協議，是作業系統提供的網路連線 API 介面。**

所有網路通訊（HTTP、WebSocket、gRPC、MQTT）的底層，都是透過 Socket API 操作 TCP/IP。
差別只在「封裝的層次」。

```
你的程式碼
    ↓ 呼叫
Socket API（OS 提供的介面）
    ↓
TCP / UDP（傳輸層協議）
    ↓
IP（網路層，負責定址與路由）
    ↓
網路線 / WiFi / 4G
```

---

## 傳輸層：TCP vs UDP

所有應用層協議都建在這兩個之上，先搞懂它們的差異。

### TCP（Transmission Control Protocol）

**可靠傳輸**，有以下保證：
- 資料一定送達（若網路中斷會重試）
- 資料順序正確（封包 1 一定在封包 2 之前）
- 不會重複（去除重複封包）

代價是有額外的握手、確認、重傳機制，速度比 UDP 慢。

```
Client                    Server
  |------ SYN ------------>|    三次握手建立連線
  |<----- SYN-ACK ---------|
  |------ ACK ------------>|
  |                        |
  |------ Data 1 --------->|
  |<----- ACK 1 -----------|    每個封包都要確認
  |------ Data 2 --------->|
  |<----- ACK 2 -----------|
```

**使用場景**：HTTP、WebSocket、gRPC、MQTT、一般 API 全部都用 TCP。

---

### UDP（User Datagram Protocol）

**快速但不可靠**：
- 不保證送達（封包可能遺失）
- 不保證順序（可能亂序）
- 不去除重複

代價是沒有握手、確認機制，延遲極低。

**使用場景**：
- 即時遊戲（丟一個位置封包沒到沒關係，下一個馬上來）
- 影音串流（一格畫面掉了比卡頓好）
- DNS 查詢（簡單問答，不需要可靠連線）

---

## 應用層協議全景

```
高層抽象（框架幫你處理格式）
┌─────────────────────────────────────────────────┐
│  SignalR  │  REST/HTTP  │  gRPC  │  MQTT        │
└─────────────────────────────────────────────────┘
         ↓           ↓         ↓        ↓
┌─────────────────────────────────────────────────┐
│  WebSocket  │  HTTP/1.1  │  HTTP/2  │  TCP raw  │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│              TCP Socket / UDP Socket            │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│                    IP 網路層                     │
└─────────────────────────────────────────────────┘
```

---

## 各協議詳細說明

---

### 1. HTTP / REST（已實作）

**連線方式**：短連線。每次 Request 建立連線，Response 後關閉。

**資料格式**：文字（JSON / XML）

**運作流程**：
```
Client                          Server
  |--- GET /api/items --------->|
  |<-- 200 OK + JSON -----------|   連線結束

  |--- POST /api/items -------->|
  |<-- 201 Created + JSON ------|   連線結束
```

**優點**：
- 通用、有 Swagger 文件
- 任何語言、任何平台都能呼叫
- 無狀態，容易水平擴展

**缺點**：
- 每次都要重新建立連線（有 overhead）
- Server 無法主動推訊息給 Client
- 大量資料時 Header 重複傳輸浪費頻寬

**適合場景**：CRUD API、一般 Web 後端、對外公開 API

---

### 2. WebSocket

**連線方式**：長連線。一次握手後持續保持，雙方都可以隨時發送訊息。

**資料格式**：二進位 or 文字（自訂）

**運作流程**：
```
Client                          Server
  |--- HTTP Upgrade ----------->|   握手（只做一次）
  |<-- 101 Switching Protocols -|
  |                             |
  |--- 訊息 A ----------------->|   之後雙向任意時間發送
  |<-- 訊息 B ------------------|
  |<-- 訊息 C ------------------|   Server 主動推送
  |--- 訊息 D ----------------->|
  |                             |
  （連線一直保持直到某一方關閉）
```

**底層**：TCP Socket + HTTP Upgrade 握手

**優點**：
- 真正的雙向即時通訊
- 連線建立後無額外 Header overhead
- Server 可以主動推訊息

**缺點**：
- 需要維護連線狀態（Server 要記住哪些 Client 連著）
- 水平擴展較複雜（多個 Server 時需要 Redis 同步連線狀態）

**適合場景**：聊天室、即時通知、多人協作、Dashboard 即時數據

---

### 3. SignalR（WebSocket 的封裝）

**本質**：Microsoft 對 WebSocket 的高層封裝。

**額外提供**：
- 自動 fallback（WebSocket → SSE → Long Polling）
- 群組廣播（broadcast to group）
- 重連機制
- 強型別 Hub 方法呼叫

```csharp
// Server 端（Hub）
public class ChatHub : Hub
{
    // Client 呼叫 Server 的方法
    public async Task SendMessage(string message)
    {
        // Server 廣播給所有連線的 Client
        await Clients.All.SendAsync("ReceiveMessage", message);
    }
}

// Client 端
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost/hub/chat")
    .Build();

connection.On<string>("ReceiveMessage", msg => Console.WriteLine(msg));
await connection.StartAsync();
await connection.InvokeAsync("SendMessage", "Hello!");
```

**適合場景**：所有需要 WebSocket 的場景，在 .NET 生態優先用 SignalR

---

### 4. SSE（Server-Sent Events）

**連線方式**：單向長連線（只有 Server → Client）

**運作流程**：
```
Client                          Server
  |--- GET /events ------------>|
  |<-- data: 訊息 A ------------|
  |<-- data: 訊息 B ------------|   Server 持續推送
  |<-- data: 訊息 C ------------|
  （Client 不能透過這條連線發訊息回去）
```

**優點**：比 WebSocket 輕量，走標準 HTTP，防火牆不會擋

**缺點**：只有單向推送，Client 要發訊息得另外開 REST 連線

**適合場景**：新聞 feed、系統通知、進度條更新（不需要 Client 回傳的場景）

---

### 5. gRPC

**連線方式**：HTTP/2 長連線，支援多路復用（一條連線同時跑多個請求）

**資料格式**：Protocol Buffers（二進位，比 JSON 小 3-10 倍）

**特性**：強型別，先定義 `.proto` 檔，自動生成 Client/Server 程式碼

```protobuf
// item.proto（介面定義）
service ItemService {
  rpc GetItem (GetItemRequest) returns (Item);
  rpc ListItems (Empty) returns (stream Item);  // Server streaming
}

message Item {
  int32 id = 1;
  string name = 2;
}
```

**與 REST 比較**：

| | REST | gRPC |
|---|---|---|
| 資料格式 | JSON（文字） | Protobuf（二進位） |
| 速度 | 一般 | 快 3-10 倍 |
| 型別安全 | 無（靠文件約定） | 強型別（編譯期檢查） |
| 可讀性 | 高（JSON 直接看） | 低（需要工具解析） |
| 瀏覽器支援 | 完整 | 有限（需要 grpc-web） |

**適合場景**：
- 後端服務與服務之間的高頻呼叫
- 需要強型別介面確保兩端不出錯
- 資料量大、效能敏感的服務間通訊

**不適合**：對外公開的 API（瀏覽器支援差），用 REST 就好

---

### 6. Message Broker（RabbitMQ / Kafka）

**連線方式**：非同步，不直接連線，透過中間人（Broker）

**運作流程**：
```
ServiceA                  Broker（RabbitMQ）         ServiceB
   |--- 發布事件 "訂單建立" -->|                          |
   |（完成，不等待）           |--- 推送給訂閱者 ---------->|
                              |                      （非同步處理）
```

**與其他協議最大的差異**：
- ServiceA 和 ServiceB **完全不知道對方的存在**
- ServiceB 當機時，訊息在 Broker 裡排隊等待，恢復後繼續處理
- 可以有多個 ServiceB 同時處理同一個 Queue（並行處理）

**兩種主要模式**：

```
Queue（工作佇列）               Topic / Exchange（發布訂閱）
ServiceA → [Queue] → ServiceB   ServiceA → [Exchange] → ServiceB1
                                                       → ServiceB2
                                                       → ServiceB3
一個訊息只給一個消費者            一個訊息廣播給所有訂閱者
```

**RabbitMQ vs Kafka**：

| | RabbitMQ | Kafka |
|---|---|---|
| 定位 | 訊息佇列 | 事件串流平台 |
| 訊息保留 | 消費後刪除 | 永久保留（可重播） |
| 吞吐量 | 中等 | 極高（百萬/秒） |
| 適合 | 任務分發、RPC | 事件溯源、大數據、log |
| 學習曲線 | 低 | 高 |

**適合場景**：
- 寄信、推通知（發完不用等）
- 訂單建立後通知庫存服務扣貨
- 多個服務訂閱同一個事件
- 服務間解耦（不希望直接依賴）

---

### 7. MQTT

**連線方式**：TCP 長連線，Publish / Subscribe 模式

**特性**：
- 極度輕量（Header 只有 2 bytes）
- 支援 QoS（Quality of Service）等級 0/1/2
- 專為不穩定網路設計（IoT 裝置常在網路不穩的環境）

```
溫度感測器 → publish("building/floor1/temp", 26.5)
              → MQTT Broker
                → subscribe 的 Dashboard 收到
                → subscribe 的 AlertService 收到
```

**QoS 等級**：
- QoS 0：最多送一次（可能遺失）
- QoS 1：至少送一次（可能重複）
- QoS 2：剛好送一次（最可靠，最慢）

**適合場景**：IoT 裝置、感測器、iBMS 的電表/環控設備上傳資料

---

### 8. TCP Socket Raw

**連線方式**：直接的 TCP 連線，完全自訂協議格式

```csharp
// Server
var server = new TcpListener(IPAddress.Any, 9000);
server.Start();
var client = await server.AcceptTcpClientAsync();
var stream = client.GetStream();
// 自己讀寫 byte[]，自己定義格式

// Client
var client = new TcpClient();
await client.ConnectAsync("localhost", 9000);
var stream = client.GetStream();
await stream.WriteAsync(new byte[] { 0x01, 0x02, 0x03 });
```

**優點**：最大彈性，最低延遲

**缺點**：
- 要自己設計協議（封包格式、長度、校驗）
- 要自己處理斷線重連、黏包/分包問題
- 沒有框架保護，容易出 bug

**適合場景**：
- 對接老舊硬體設備（PLC、儀器）
- 對方只開 TCP port，沒有 HTTP
- 遊戲伺服器需要自訂封包格式

---

## 選擇指南

```
需求分析
│
├── Client 要查/改資料？
│   └── REST / HTTP ✅
│
├── Server 要主動推訊息給 Client？
│   ├── 雙向（Client 也要發回去）→ WebSocket / SignalR ✅
│   └── 單向（只有 Server 推）  → SSE
│
├── 兩個後端服務互相呼叫？
│   ├── 高頻、效能敏感         → gRPC ✅
│   └── 一般頻率              → REST 就夠
│
├── 服務間不需要即時回應？
│   └── Message Broker（RabbitMQ）✅
│
├── IoT 設備上傳資料？
│   └── MQTT
│
└── 對接老舊硬體或自訂協議？
    └── TCP Socket Raw ✅
```

---

## 我們的學習路徑對應

| Phase | 實作內容 | 覆蓋場景 |
|---|---|---|
| Phase 1（完成） | REST + Repository Pattern | CRUD、一般 Web API |
| Phase 3-A | SignalR（WebSocket） | 即時雙向通訊 |
| Phase 3-B | RabbitMQ（Message Broker） | 非同步、服務解耦 |
| Phase 4-A | gRPC | 高效能服務間通訊 |
| Phase 4-B | TCP Socket Raw | 自訂協議、硬體對接 |
| Phase 5 | Docker + Compose | 所有服務容器化部署 |

完成後能應付的情境：
- ✅ 標準 Web API
- ✅ 即時通訊（聊天、Dashboard）
- ✅ 非同步事件（通知、工作分發）
- ✅ 高效能服務間通訊
- ✅ 硬體設備對接（PLC、iBMS 設備）
- ✅ IoT 感測器（MQTT 建在 Message Broker 之上）

---

## iBMS 專案實際應用場景

| 功能 | 協議 |
|---|---|
| 電表 / 感測器上傳資料 | MQTT → RabbitMQ |
| Dashboard 即時顯示數據 | WebSocket / SignalR |
| 管理介面 CRUD | REST |
| 超標告警通知 | RabbitMQ → 通知服務 |
| 各微服務之間呼叫 | gRPC |
| 對接舊有 BMS 設備（TCP 協議）| TCP Socket Raw |
