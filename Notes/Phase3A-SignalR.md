# Phase 3-A：即時通訊（SignalR）

## 概念說明

### 為什麼需要 SignalR？

HTTP 是**短連線、單向**的：Client 問，Server 才能答。
如果 Server 想主動推訊息給 Client（例如即時聊天、遊戲狀態同步、通知），HTTP 做不到。

**WebSocket** 解決了這個問題：建立之後保持長連線，雙向都能主動傳訊息。

**SignalR** 是微軟封裝在 WebSocket 上的框架，多提供了：
- 自動 fallback（WebSocket → SSE → Long Polling，視瀏覽器支援）
- Hub 抽象層（直接呼叫 Server 方法，像 RPC 一樣）
- 廣播 / 群組管理（`Clients.All`、`Clients.Group`）
- 自動重連機制（`.withAutomaticReconnect()`）

### WebSocket 和 SignalR 的關係

```
 ┌─────────────────────────────────┐
 │           SignalR               │  ← 你用的是這層
 │   (Hub 協議 / JSON / 群組管理)    │
 ├─────────────────────────────────┤
 │           WebSocket             │  ← SignalR 預設的傳輸層
 │   (長連線 / 雙向 / 低延遲)         │
 ├─────────────────────────────────┤
 │             TCP                 │
 └─────────────────────────────────┘
```

SignalR 底層就是 WebSocket，不是兩個平行的東西。你選 SignalR，就等於選了 WebSocket 加上更多工具。

### 什麼時候才用純 WebSocket？

| 情境 | 選哪個 |
|------|--------|
| 前端對接自己的 .NET 後端 | SignalR |
| 多人遊戲、聊天室、即時通知 | SignalR |
| 對接第三方服務（只支援 WebSocket，不支援 SignalR 協議） | 純 WebSocket |
| 對接硬體裝置（嵌入式 WebSocket，無法載入 SignalR 套件） | 純 WebSocket |
| 需要自訂二進位協議（類似 TCP Raw） | 純 WebSocket |
| 連線到非 .NET 伺服器（Python/Go，沒有 SignalR 對等實作） | 純 WebSocket |

---

## 架構圖

```
 Browser                Gateway (5000)          Demo.RealTime (5200)
    │                        │                         │
    │  WS /hub/chat          │                         │
    │──────────────────────► │  WS /hub/chat           │
    │                        │ ──────────────────────► │
    │                        │                         │  ChatHub
    │                        │                         │  OnConnectedAsync()
    │◄─────────────────────────────────────────────────│  廣播 "XXX joined"
    │                        │                         │
    │  invoke SendMessage    │                         │
    │──────────────────────► │ ──────────────────────► │
    │                        │                         │  SendMessage()
    │◄─────────────────────────────────────────────────│  廣播給所有人
```

Gateway 做路由轉發（`/hub/**` → RealTime），CORS 也集中在 Gateway 處理。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.RealTime/Demo.RealTime.csproj` | 新增 | 專案定義，安裝 SignalR |
| `Demo.RealTime/Program.cs` | 新增 | 服務入口，註冊 SignalR、Serilog、OTel、Prometheus |
| `Demo.RealTime/Hubs/ChatHub.cs` | 新增 | SignalR Hub，處理連線、離線、廣播 |
| `Demo.RealTime/wwwroot/index.html` | 新增 | 前端測試頁，使用 SignalR JS Client |
| `Demo.RealTime/appsettings.json` | 新增 | Serilog 設定、OTel Endpoint |
| `Demo.Gateway/appsettings.json` | 修改 | 新增 `/hub/**` → RealTime 路由 |

---

## 實作步驟

### 步驟 1：建立 Demo.RealTime 專案

**目的**：建立一個獨立的 WebAPI 服務，專門處理 SignalR 連線。

```bash
# 在 Solution 根目錄執行
dotnet new web -n Demo.RealTime
dotnet sln add Demo.RealTime/Demo.RealTime.csproj
```

SignalR 從 .NET 3.0 起已內建在 `Microsoft.AspNetCore`，**不需要額外安裝套件**。

---

### 步驟 2：建立 ChatHub

**目的**：定義 Server 可以被 Client 呼叫的方法，以及 Server 主動推送的事件。

**新增檔案**：`Demo.RealTime/Hubs/ChatHub.cs`

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Demo.RealTime.Hubs;

public class ChatHub : Hub
{
    // Client 呼叫這個方法 → Server 廣播給所有人
    // connection.invoke("SendMessage", user, message) 對應這裡
    public async Task SendMessage(string user, string message)
    {
        // Clients.All：廣播給目前所有連線的 Client
        // "ReceiveMessage"：Client 端 connection.on("ReceiveMessage", ...) 對應的事件名稱
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }

    // Client 建立連線時自動觸發（不需要 Client 主動呼叫）
    public override async Task OnConnectedAsync()
    {
        // Context.ConnectionId：SignalR 為每個連線自動分配的唯一 ID
        await Clients.All.SendAsync("ReceiveMessage", "System", $"{Context.ConnectionId} joined");
        await base.OnConnectedAsync();  // 一定要呼叫 base，否則連線流程不完整
    }

    // Client 斷線時自動觸發（正常關閉或網路中斷都會觸發）
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("ReceiveMessage", "System", $"{Context.ConnectionId} left");
        await base.OnDisconnectedAsync(exception);
    }
}
```

**Hub 方法命名規則**：
- Client 用 `invoke("SendMessage")` → Server 要有 `public Task SendMessage(...)`
- Server 用 `SendAsync("ReceiveMessage")` → Client 用 `on("ReceiveMessage", ...)`
- 名稱是字串對應，大小寫不敏感，但習慣上保持一致

---

### 步驟 3：設定 Program.cs

**目的**：註冊 SignalR 服務，並將 ChatHub 掛載到路由。

**修改檔案**：`Demo.RealTime/Program.cs`

```csharp
using Demo.RealTime.Hubs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 結構化日誌（Phase 7-A）
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

// 告訴 DI 容器：這個服務需要 SignalR
builder.Services.AddSignalR();

// 健康檢查端點（Phase 6-D）
builder.Services.AddHealthChecks();

// 分散式追蹤（Phase 7-B）
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.RealTime"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 追蹤 HTTP + WebSocket upgrade 請求
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));

var app = builder.Build();

// 讓 wwwroot/ 目錄下的靜態檔案可以被直接存取（index.html 測試頁）
app.UseStaticFiles();

// 追蹤每個 HTTP 請求的 method、status、duration（Phase 7-C）
app.UseHttpMetrics();

// CORS 由 Gateway 統一處理，這裡不需要設定

// 將 ChatHub 掛載到 /hub/chat 路徑
// 瀏覽器連線到 ws://localhost:5000/hub/chat（經由 Gateway 轉發）
app.MapHub<ChatHub>("/hub/chat");

app.MapHealthChecks("/health");

// 暴露 /metrics，讓 Prometheus 來抓（Phase 7-C）
app.UseMetricServer();

app.Run();
```

---

### 步驟 4：設定 appsettings.json

**目的**：配置 Serilog 日誌格式與 OTel 追蹤端點。

**修改檔案**：`Demo.RealTime/appsettings.json`

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext" ]
  },
  "Otlp": {
    "Endpoint": "http://localhost:4317"
  },
  "AllowedHosts": "*"
}
```

---

### 步驟 5：建立前端測試頁（wwwroot/index.html）

**目的**：用 SignalR JS Client 在瀏覽器中測試即時通訊，不需要 Postman。

**新增檔案**：`Demo.RealTime/wwwroot/index.html`

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>SignalR Chat Demo</title>
</head>
<body>
    <h2>SignalR Chat Demo</h2>
    <div id="status">連線中...</div>
    <div id="messages" style="border:1px solid #ccc; height:300px; overflow-y:auto; padding:8px;"></div>

    <input id="user" type="text" value="User1" style="width:100px" />
    <input id="message" type="text" placeholder="輸入訊息..." style="width:300px" />
    <button onclick="sendMessage()">送出</button>

    <!-- 從 CDN 載入 SignalR JS Client（版本需與後端 .NET 版本對應） -->
    <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
    <script>
        // 建立 HubConnection，指向 Gateway（不直接連 RealTime 服務）
        // Gateway 的 /hub/** 路由會轉發到 RealTime:5200
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("http://localhost:5000/hub/chat")
            .withAutomaticReconnect()   // 斷線後自動重連（預設 0、2、10、30 秒間隔）
            .build();

        // 註冊事件：Server 呼叫 SendAsync("ReceiveMessage") 時觸發
        // 參數順序必須和 Server 端 SendAsync 的參數一致
        connection.on("ReceiveMessage", (user, message) => {
            const div = document.getElementById("messages");
            div.innerHTML += `<div><b>${user}</b>: ${message}</div>`;
            div.scrollTop = div.scrollHeight;
        });

        // 建立 WebSocket 連線
        connection.start()
            .then(() => {
                document.getElementById("status").textContent =
                    `已連線 (ID: ${connection.connectionId})`;
            })
            .catch(err => console.error("連線失敗：", err));

        function sendMessage() {
            const user = document.getElementById("user").value;
            const message = document.getElementById("message").value;
            if (!message) return;

            // invoke：呼叫 Server Hub 的方法（非同步，回傳 Promise）
            connection.invoke("SendMessage", user, message)
                .catch(err => console.error(err));

            document.getElementById("message").value = "";
        }

        document.getElementById("message").addEventListener("keypress", e => {
            if (e.key === "Enter") sendMessage();
        });
    </script>
</body>
</html>
```

---

### 步驟 6：Gateway 新增 /hub/** 路由

**目的**：所有 WebSocket 連線經由 Gateway 統一入口轉發，前端只需要知道 Gateway 的位址。

**修改檔案**：`Demo.Gateway/appsettings.json`

在 `ReverseProxy` → `Routes` 新增：

```json
"realtime-route": {
  "ClusterId": "realtime-cluster",
  "Match": { "Path": "/hub/{**catch-all}" },
  "AuthorizationPolicy": "default"
},
```

在 `ReverseProxy` → `Clusters` 新增：

```json
"realtime-cluster": {
  "Destinations": {
    "realtime/destination1": {
      "Address": "http://localhost:5200/"
    }
  }
}
```

**為什麼要把 WebSocket 路由加上 AuthorizationPolicy**：
SignalR 建立連線時是 HTTP Upgrade 請求，Gateway 可以在這個階段驗 JWT Token，
連線建立後的 WebSocket 訊息就不需要重複驗證了。

---

## 專有名詞解釋

| 名詞 | 說明 |
|------|------|
| **WebSocket** | HTTP 升級後的長連線協議（`ws://` / `wss://`），雙向、低延遲，不像 HTTP 每次都要帶 Header |
| **SignalR** | 微軟封裝 WebSocket 的框架，提供 Hub、廣播、自動重連等功能 |
| **Hub** | SignalR 的核心概念，類似一個「頻道管理員」，Client 連進來後可呼叫 Hub 的方法，Hub 也可以主動推訊息給 Client |
| **ConnectionId** | SignalR 為每個連線自動分配的唯一識別碼（GUID 格式） |
| **Clients.All** | 廣播給所有目前連線的 Client |
| **Clients.Caller** | 只回給呼叫這個方法的那個 Client |
| **Clients.Others** | 廣播給除了呼叫者以外的所有 Client |
| **Clients.Group(name)** | 廣播給特定群組的 Client（需先 `Groups.AddToGroupAsync`） |
| **invoke** | JS Client 呼叫 Server Hub 方法（有回傳值，回傳 Promise） |
| **send** | JS Client 呼叫 Server Hub 方法（不等回傳，fire and forget） |
| **on** | JS Client 註冊事件監聽，等 Server 主動推送 |
| **SSE (Server-Sent Events)** | WebSocket 的 fallback 之一，只有 Server → Client 單向推送 |
| **Long Polling** | 最後的 fallback，Client 輪詢，WebSocket 不支援時才用 |
| **HTTP Upgrade** | 從 HTTP 升級為 WebSocket 的握手過程，成功後就切換協議 |

---

## Clients.XXX 比較

```
連線中的 Client：A、B、C（A 是呼叫者）

Clients.All            → 傳給 A、B、C
Clients.Caller         → 傳給 A
Clients.Others         → 傳給 B、C
Clients.Client("id")   → 傳給指定 ConnectionId 的 Client
Clients.Group("room1") → 傳給 room1 群組的成員
```

---

## 驗證方式

### 本機開發驗證

1. 啟動 `Demo.RealTime`（port 5200）和 `Demo.Gateway`（port 5000）
2. 開啟兩個瀏覽器分頁，都前往 `http://localhost:5200`（直接存取測試頁）
3. 兩個分頁各輸入不同名稱，互相傳訊息
4. 預期：兩個分頁都即時看到對方的訊息

> 測試頁直接連 5200 可以跳過 Gateway（不需要 JWT）。
> 測試完整路徑時改連 `http://localhost:5000/hub/chat`，需要在連線前帶 Token。

### 驗證 WebSocket 升級

在瀏覽器 DevTools → Network → 篩選 `WS`：
- 狀態碼應為 **101 Switching Protocols**（不是 200）
- Protocol 欄顯示 `websocket`
- Messages 分頁可以看到雙向的訊息流

### 驗證廣播

- 分頁 A 連線 → 分頁 B 看到「A's ConnectionId joined」
- 分頁 A 送訊息 → 分頁 B 即時看到
- 關閉分頁 A → 分頁 B 看到「A's ConnectionId left」
