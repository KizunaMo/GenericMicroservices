# Phase 3-B 補充：RabbitMQ → SignalR 橋接筆記

## 概念說明

### 問題：RabbitMQ 事件如何送到 Unity？

Phase 3-B 實作了「後端服務之間的非同步通訊」：

```
DataService → ItemCreated → RabbitMQ → Demo.Worker（後端消費）
```

但 Unity 是**前端客戶端**，無法直接訂閱 RabbitMQ（AMQP 協議，適合後端服務互連）。
Unity 只能透過 **SignalR（WebSocket）** 與後端維持即時連線。

### 解法：讓 RealTime 服務同時訂閱 RabbitMQ

```
DataService → Publish(ItemCreated) → RabbitMQ
                                         │
                    ┌────────────────────┴─────────────────────┐
                    ▼                                           ▼
             Demo.Worker                               Demo.RealTime（新）
          （後端處理：記錄日誌等）                  訂閱 RabbitMQ 事件
                                                    IHubContext<ItemHub>
                                                               │
                                                               ▼
                                                        SignalR push
                                                               │
                                                               ▼
                                                       Unity Client
```

### 為什麼選方案 B（RealTime 直接訂閱 RabbitMQ）而非方案 A（Worker 呼叫 RealTime HTTP）？

| | 方案 A（Worker → HTTP → RealTime） | 方案 B（各自訂閱 RabbitMQ）✅ |
|---|---|---|
| 耦合度 | Worker 必須知道 RealTime 的位址 | 兩個服務完全不知道對方存在 |
| 擴展性 | 加新服務要改 Worker 的程式碼 | 加新服務只要多訂閱 RabbitMQ |
| 故障影響 | RealTime 掛掉 → Worker 也失敗 | 互不影響，各自獨立消費 |
| 符合微服務原則 | ✗ | ✅ |

這就是 **Pub/Sub 模式**的核心：發布者完全不知道有幾個訂閱者，訂閱者各自獨立。

### IHubContext<T> 是什麼？

SignalR Hub 本身代表一個連線上下文（每個客戶端連進來就是一個 Hub 實例）。
但 Consumer 不是 Hub，它是一個獨立的類別，要怎麼推送訊息給 SignalR 客戶端？

答案是 **`IHubContext<T>`**：

```
Hub（連線上下文）         IHubContext<T>（Hub 的服務代理）
  ↓                          ↓
Hub 內部用 Clients.All   → 任何注入點都能用，功能相同
只能在 Hub 方法裡用         在 Consumer、Controller、背景服務都能用
```

`IHubContext<T>` 由 ASP.NET Core DI 提供，只要 `AddSignalR()` 有註冊，任何地方都可以注入。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.RealTime/Hubs/ItemHub.cs` | 新增 | Unity 連接的 SignalR Hub，Server → Client 單向推送 |
| `Demo.RealTime/Consumers/ItemCreatedConsumer.cs` | 新增 | 訂閱 RabbitMQ，收到事件後推送 SignalR |
| `Demo.RealTime/Program.cs` | 修改 | 註冊 MassTransit、ItemHub 路由 |
| `Demo.RealTime/appsettings.json` | 修改 | 加入 RabbitMq:Host 設定 |
| `Demo.RealTime/Dockerfile` | 修改 | 加入 Demo.Contracts 的 COPY 步驟 |
| `Demo.RealTime/Demo.RealTime.csproj` | 修改（自動）| 加入 MassTransit.RabbitMQ 和 Demo.Contracts 相依 |
| `docker-compose.yml` | 修改 | realtime-service 加入 RabbitMq__Host 環境變數和 depends_on |

---

## 實作步驟

### 步驟 1：安裝 MassTransit 套件

**目的**：讓 Demo.RealTime 有能力連接 RabbitMQ 訂閱事件

```bash
cd Demo.RealTime
dotnet add package MassTransit.RabbitMQ --version 8.3.6
dotnet add reference ../Demo.Contracts/Demo.Contracts.csproj
```

**為什麼是 8.3.6**：MassTransit v9+ 需要付費授權，v8.x 是免費開源版本。

---

### 步驟 2：建立 ItemHub

**目的**：提供 Unity 客戶端連接的 SignalR 端點，專門接收 Item 相關事件

**新增檔案**：`Demo.RealTime/Hubs/ItemHub.cs`

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Demo.RealTime.Hubs;

// Unity 客戶端連接到這個 Hub，訂閱後端主動推送的事件
// 這個 Hub 是單向的：Server → Client（沒有 Client → Server 的方法）
// 推送由 ItemCreatedConsumer 透過 IHubContext<ItemHub> 觸發
public class ItemHub : Hub
{
}
```

**為什麼是空的 Hub**：這個 Hub 只用於 Server → Client 推送，Unity 不需要呼叫任何 Hub 方法。推送邏輯在 Consumer 裡，透過 `IHubContext<ItemHub>` 完成。

---

### 步驟 3：建立 ItemCreatedConsumer

**目的**：橋接 RabbitMQ 和 SignalR——收到事件後，推送給所有連線的 Unity 客戶端

**新增檔案**：`Demo.RealTime/Consumers/ItemCreatedConsumer.cs`

```csharp
using Demo.Contracts;
using Demo.RealTime.Hubs;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace Demo.RealTime.Consumers;

public class ItemCreatedConsumer : IConsumer<ItemCreated>
{
    // IHubContext<T>：在 Hub 外部操作 SignalR 的介面
    // 不同於 Hub 內部的 Clients，這個介面讓任何注入點都能推送訊息
    private readonly IHubContext<ItemHub> _hubContext;
    private readonly ILogger<ItemCreatedConsumer> _logger;

    public ItemCreatedConsumer(IHubContext<ItemHub> hubContext, ILogger<ItemCreatedConsumer> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ItemCreated> context)
    {
        var item = context.Message;

        _logger.LogInformation(
            "[RealTime] 收到 ItemCreated 事件 → Id: {Id}, Name: {Name}，推送 SignalR",
            item.Id, item.Name);

        // 推送給所有連線到 /hub/items 的 Unity 客戶端
        // "OnItemCreated"：Unity 端要監聽的事件名稱（On 前綴是慣例）
        await _hubContext.Clients.All.SendAsync("OnItemCreated", new
        {
            item.Id,
            item.Name,
            item.CreatedAt
        });
    }
}
```

**重點**：`"OnItemCreated"` 是 Unity 端監聽的事件名稱，兩邊必須一致（大小寫敏感）。

---

### 步驟 4：修改 Program.cs

**目的**：註冊 MassTransit（讓 Consumer 開始監聽）和 ItemHub 路由

**修改檔案**：`Demo.RealTime/Program.cs`

```csharp
using Demo.RealTime.Consumers;
using Demo.RealTime.Hubs;
using MassTransit;
// ... 其他 using 不變

var builder = WebApplication.CreateBuilder(args);

// ... Serilog、SignalR、OpenTelemetry 設定不變

// 新增：讓 RealTime 服務成為 RabbitMQ 的訂閱者
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ItemCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(context);  // 自動建立 Queue
    });
});

var app = builder.Build();

// ... UseStaticFiles、UseHttpMetrics 不變

app.MapHub<ChatHub>("/hub/chat");
app.MapHub<ItemHub>("/hub/items");   // 新增：Unity 連這裡訂閱 Item 事件
app.MapHealthChecks("/health");
app.UseMetricServer();

app.Run();
```

---

### 步驟 5：更新 appsettings.json

**目的**：讓開發環境有 RabbitMQ Host 設定（預設 localhost）

**修改檔案**：`Demo.RealTime/appsettings.json`

```json
{
  "RabbitMq": {
    "Host": "localhost"
  },
  "Serilog": { ... },
  "Otlp": { ... }
}
```

---

### 步驟 6：更新 Dockerfile

**目的**：RealTime 現在相依 Demo.Contracts（包含 ItemCreated 定義），Dockerfile 必須複製這個專案才能 build

**修改檔案**：`Demo.RealTime/Dockerfile`

```dockerfile
# 先複製 .csproj（包含 Demo.Contracts，restore 需要知道相依）
COPY Demo.RealTime/Demo.RealTime.csproj Demo.RealTime/
COPY Demo.Contracts/Demo.Contracts.csproj Demo.Contracts/
RUN dotnet restore Demo.RealTime/Demo.RealTime.csproj

# 複製完整原始碼再 publish
COPY Demo.RealTime/ Demo.RealTime/
COPY Demo.Contracts/ Demo.Contracts/
RUN dotnet publish Demo.RealTime/Demo.RealTime.csproj -c Release -o /app/publish --no-restore
```

**為什麼兩段都要 COPY Demo.Contracts**：第一段是讓 restore 知道相依的 csproj，第二段才有實際的 .cs 原始碼可以編譯。

---

### 步驟 7：更新 docker-compose.yml

**目的**：realtime-service 在 Docker 環境需要連 rabbitmq 容器，並且等 rabbitmq 啟動後才啟動

**修改**：`docker-compose.yml`

```yaml
realtime-service:
  environment:
    - Otlp__Endpoint=http://jaeger:4317
    - RabbitMq__Host=rabbitmq        # 新增：Docker 內部用服務名稱
  depends_on:
    - rabbitmq                        # 新增：等 rabbitmq 啟動
```

---

## Unity 端實作參考（C#）

```csharp
using Microsoft.AspNetCore.SignalR.Client;

// 建立連線到 /hub/items
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hub/items")   // 透過 Gateway
    .WithAutomaticReconnect()
    .Build();

// 監聽 OnItemCreated 事件（名稱必須和後端 SendAsync 的第一個參數一致）
connection.On<ItemCreatedDto>("OnItemCreated", (item) =>
{
    Debug.Log($"收到新 Item：{item.Id} - {item.Name}");
    // 更新 UI、刷新列表等
});

await connection.StartAsync();

// 對應的資料結構
public class ItemCreatedDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Gateway 路由**：`/hub/items` 在 Gateway 的 YARP 設定中對應到 `realtime-service:8080/hub/items`，Unity 只需要連 Gateway，不需要知道後端位址。

---

## 驗證方式

### 開發環境（本機）

1. 確認 RabbitMQ 在執行：`brew services list | grep rabbitmq`
2. 啟動 Demo.RealTime：`cd Demo.RealTime && dotnet run`
3. 觀察啟動日誌，應該看到 MassTransit 連接 RabbitMQ 的訊息
4. 開啟 `http://localhost:15672`（RabbitMQ 管理介面），在 Queues 頁面應該看到一個新的 Queue（名稱包含 `ItemCreatedConsumer`）
5. 用 SignalR 客戶端（或瀏覽器 console）連接 `ws://localhost:5200/hub/items`
6. 用 Postman 發送 `POST http://localhost:5128/api/items`
7. 預期：SignalR 客戶端收到 `OnItemCreated` 事件

### Docker 環境

1. `docker-compose up --build realtime-service`
2. 觀察日誌：應該看到 `[RealTime] 收到 ItemCreated 事件 → ...`

---

## 專有名詞解釋

| 名詞 | 說明 |
|------|------|
| **IHubContext<T>** | 在 Hub 外部（Controller、背景服務等）操作 SignalR 的服務介面，由 `AddSignalR()` 自動注入 |
| **Pub/Sub（發布/訂閱）** | 一個發布者對多個訂閱者的訊息模式，發布者和訂閱者完全解耦，彼此不知道對方存在 |
| **Fan-out** | RabbitMQ 的廣播模式，一個訊息被複製推送給所有訂閱者（Worker 和 RealTime 各收一份）|
| **`SendAsync` 第一個參數** | SignalR 的事件名稱，客戶端用 `connection.On("事件名稱", ...)` 監聽，兩邊必須完全一致 |
