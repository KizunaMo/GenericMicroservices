# Phase 4-B：TCP Socket Raw

## TCP 在協議層的位置

```
應用層    HTTP、gRPC、WebSocket、自訂協議
    │
傳輸層    TCP、UDP         ← TCP 在這一層
    │
網路層    IP
    │
實體層    網路線、Wi-Fi
```

HTTP、gRPC、WebSocket **都是建立在 TCP 之上的**。
它們只是幫你定義好了資料格式。
TCP Socket Raw 則是直接使用 TCP，**自己定義資料格式**。

---

## TCP 的四個核心特性

| 特性       | 說明                                           |
|------------|------------------------------------------------|
| 連線導向   | 通訊前先建立連線（三次握手）                   |
| 可靠傳輸   | 保證資料到達、不遺失、不亂序                   |
| 串流       | 資料是連續的位元組流，沒有天然「邊界」         |
| 雙向       | Server 和 Client 都可以主動送資料              |

---

## 最重要的概念：黏包問題（Sticky Packet）

TCP 是**串流**，不是一包一包的訊息。

```
你以為送出的：   [訊息 A][訊息 B]

實際收到的可能： [訊息 A + 訊息 B 的前半段]
                [訊息 B 的後半段]
```

這就是**黏包問題**：接收方無法自動判斷一筆資料從哪裡結束。

### 解法：自訂封包格式（Framing）

必須自己定義「一筆資料的邊界」，常見兩種做法：

**方法一：固定長度 Header（推薦）**
```
[ 4 bytes: 資料長度 ][ N bytes: 資料內容 ]

範例：
  送出 "Hello"（5 bytes）
  → [0x00 0x00 0x00 0x05][H e l l o]

接收方流程：
  1. 先讀 4 bytes，得知資料長度 = 5
  2. 再讀 5 bytes，得到完整資料
```

**方法二：特殊分隔符**
```
資料內容 + \n

範例：Hello\nWorld\n

缺點：資料內容本身不能含有分隔符
```

---

## Server / Client 通訊模型

```
Server                              Client
  │                                   │
  │  TcpListener.Start()              │
  │  （監聽 Port，等待連線）           │
  │                                   │
  │ ←──────── 建立連線 ───────────────│  TcpClient.Connect()
  │                                   │
  │ ←──────── 送資料 ─────────────────│  stream.Write(...)
  │  stream.Read(...)                 │
  │  （處理資料）                      │
  │ ──────────回應 ───────────────────→│  stream.Read(...)
  │                                   │
  │  （保持連線，持續通訊）            │
```

- **Server**：監聽指定 Port，等待 Client 連進來，每個連線開一個 Task 處理
- **Client**：主動連到 Server 的 IP + Port
- 連線建立後，雙方都可以隨時送資料（不像 HTTP 要等 Request）

---

## 和其他通訊方式的比較

| 項目         | HTTP / gRPC    | WebSocket      | TCP Socket Raw   |
|--------------|----------------|----------------|------------------|
| 協議格式     | 固定（框架定義）| 固定（框架定義）| 自己定義          |
| 連線模式     | 短連線          | 長連線          | 長連線            |
| 框架支援     | 完整            | 完整            | 幾乎沒有          |
| 黏包問題     | 框架處理        | 框架處理        | 自己處理          |
| 適合場景     | API 呼叫        | 即時推播        | 硬體設備、遊戲    |

---

## 適合使用 TCP Socket Raw 的場景

- 對接硬體設備（IoT 感測器、工廠機器、PLC 控制器）
- 自訂通訊協議（遊戲 Server、即時控制系統）
- 效能極度敏感的場景（避免 HTTP 的額外 overhead）
- 對接使用私有協議的第三方設備

> Unity 開發中常見：遊戲 Client 用 TCP Socket 連到 Game Server，
> 或對接外部硬體設備（例如 VR 控制器、體感設備）。

---

## .NET 中的 TCP 相關 Class

| Class           | 用途                                   |
|-----------------|----------------------------------------|
| `TcpListener`   | Server 端，監聽 Port、接受連線         |
| `TcpClient`     | Client 端，主動發起連線                |
| `NetworkStream` | 連線建立後，用來讀寫資料的串流         |
| `BinaryReader`  | 從 Stream 讀取特定長度的 bytes         |
| `BinaryWriter`  | 向 Stream 寫入 bytes                   |

---

## 封包格式設計

### 基礎版（只有長度）

```
[ 4 bytes: 資料長度 ][ N bytes: 資料 ]
```

適合簡單場景，只有一種訊息類型。

### 擴充版（Category + SubType）

```
[ 4 bytes: Category ][ 4 bytes: SubType ][ 4 bytes: 資料長度 ][ N bytes: 資料 ]
```

**Category**：大分類（Item、System...）
**SubType**：細分動作（Query、Create、Heartbeat...）
**資料**：依 SubType 不同，內容完全自由

---

## 多層分類設計（Category + SubType）

### 為什麼需要兩層？

訊息量大的時候，單一 MessageType 會變得難以管理：

```
// 單層（混亂）
MessageType = 1 → ItemQuery
MessageType = 2 → ItemCreate
MessageType = 3 → SystemHeartbeat
MessageType = 4 → SystemError
...（越來越多）

// 兩層（清楚）
Category = Item,   SubType = Query
Category = Item,   SubType = Create
Category = System, SubType = Heartbeat
Category = System, SubType = Error
```

### C# 實作：各自獨立的 enum

```csharp
public enum MessageCategory { Item = 1, System = 2 }
public enum ItemSubType     { Query = 1, Create = 2 }
public enum SystemSubType   { Heartbeat = 1, Error = 2 }
```

每個 Category 有自己的 SubType enum，新增分類不影響其他分類。

---

## Handler 設計（Strategy Pattern）

每個 Category + SubType 組合對應一個 Handler class：

```
IMessageHandler（interface）
  ├── ItemQueryHandler      → Category=Item,   SubType=Query
  ├── ItemCreateHandler     → Category=Item,   SubType=Create
  └── SystemHeartbeatHandler → Category=System, SubType=Heartbeat
```

Server 用 Dictionary 分派：

```csharp
private readonly Dictionary<(MessageCategory, int), IMessageHandler> _handlers = new()
{
    { (MessageCategory.Item,   (int)ItemSubType.Query),       new ItemQueryHandler()       },
    { (MessageCategory.Item,   (int)ItemSubType.Create),      new ItemCreateHandler()      },
    { (MessageCategory.System, (int)SystemSubType.Heartbeat), new SystemHeartbeatHandler() },
};

// 分派
if (_handlers.TryGetValue((category, subType), out var handler))
    var response = await handler.HandleAsync(data);
```

新增訊息類型只需要：
1. 在對應的 SubType enum 加一個值
2. 新增一個 Handler class
3. 在 Dictionary 加一行

---

## 專案結構

```
Demo.TcpService/
  Protocol/
    MessageCategory.cs         ← enum：大分類
    ItemSubType.cs             ← enum：Item 的細分動作
    SystemSubType.cs           ← enum：System 的細分動作
    IMessageHandler.cs         ← interface：Handler 合約
    Packet.cs                  ← 封包資料結構
    Handlers/
      ItemQueryHandler.cs
      ItemCreateHandler.cs
      SystemHeartbeatHandler.cs
  TcpServer.cs                 ← 解析封包 → Dictionary 分派 → 呼叫 Handler
  Program.cs                   ← 啟動 Server，CancellationToken 優雅關閉

Demo.TcpTestConsole/
  TcpClientHelper.cs           ← 封裝連線、Send、Receive（程式碼參考用）
  Program.cs                   ← 測試各種 Category + SubType
```

---

## ReadExactAsync：解決黏包的關鍵

TCP 不保證一次 `ReadAsync` 能讀到你要的長度。
必須用迴圈持續讀，直到湊滿指定長度：

```csharp
private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
{
    var totalRead = 0;
    while (totalRead < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, ct);
        if (read == 0) return 0;  // 連線已關閉
        totalRead += read;
    }
    return totalRead;
}
```

每次讀 Header 和 Data 都要用這個方法，不能直接 `ReadAsync`。

---

## 心跳包（Heartbeat）與超時偵測

### 為什麼需要心跳包？

TCP 長連線在沒有資料傳輸時，**不會自動偵測對方是否斷線**：

```
Client 因網路問題斷線
        │
        │  Server 完全不知道
        │  繼續「以為」連線還活著
        │  資源一直被佔用，無法釋放
        ↓
記憶體洩漏 / 連線數耗盡
```

心跳包（Heartbeat）是解法：**定期互送封包，確認連線還活著**。

```
Client ──── Category=System, SubType=Heartbeat ────→ Server
Client ←──── [System.Heartbeat] Pong ─────────────── Server

若 Server 超過 90 秒沒收到任何封包
    → 主動關閉連線，釋放資源
```

### 實作：Watchdog Task

每個 Client 連線各自有一個 Watchdog Task，不影響其他連線：

```csharp
// 每個 Client 有自己的 CancellationTokenSource
// Server 停止 或 Client 超時，都會觸發取消
using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
var lastActivity = DateTime.UtcNow;

// Watchdog：每 10 秒檢查一次，超過 90 秒無活動則斷線
_ = Task.Run(async () =>
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(WatchdogInterval, ct);  // 每 10 秒醒來一次

        if (DateTime.UtcNow - lastActivity > HeartbeatTimeout)  // 超過 90 秒？
        {
            Console.WriteLine("Client timeout, closing connection.");
            clientCts.Cancel();  // 觸發斷線
        }
    }
});

// 每次收到任何封包都重置計時
lastActivity = DateTime.UtcNow;
```

### 超時參數設計原則

```
HeartbeatTimeout  = 90 秒   ← Server 等待的最長時間
WatchdogInterval  = 10 秒   ← 檢查頻率
Client 送心跳頻率 = 30 秒   ← Client 每 30 秒送一次，確保在 90 秒內至少送 2 次
```

> 任何封包（不只是 Heartbeat）都算活躍，重置計時。
> Heartbeat 只是確保「沒有業務資料時」連線不會被誤判超時。

---

## TCP 為什麼不經過 Gateway？

```
外部 Client（瀏覽器）
        │ HTTP
        ↓
    Gateway（5000）    ← YARP 只能代理 HTTP 流量
        │
        ↓
    DataService、RealTime...

Demo.TcpService（5400）← 完全不是 HTTP，Gateway 無法代理
```

**YARP 只能處理 HTTP 流量**，TCP Socket Raw 完全繞過 Gateway。

這不是設計缺陷，而是**場景決定的**：

| 場景                    | 通訊方式       | 走 Gateway？ |
|-------------------------|----------------|-------------|
| 瀏覽器呼叫 API           | REST           | 是           |
| 服務間高效呼叫           | gRPC           | 否           |
| 即時推播給瀏覽器         | SignalR        | 是（可選）   |
| 硬體設備 / 遊戲 Client   | TCP Raw        | 否           |

### TCP 服務自己負責認證

不走 Gateway 代表沒有集中的 JWT 驗證保護，
TCP 服務需要**在協議層自己實作認證**：

```
連線建立
    │
    │  Client 第一包必須是 Auth Token
    ↓
Server 驗證 Token
    ├─ 合法 → 繼續通訊
    └─ 非法 → 立即關閉連線
```

可以定義一個專門的認證訊息：
```csharp
// Category = System, SubType = Auth
{ (MessageCategory.System, (int)SystemSubType.Auth), new AuthHandler() }
```

---

## 協議擴充：加入版本號

未來若需要同時支援新舊版本的 Client，可在最前面加 Version：

```
[ 1 byte: Version ][ 4 bytes: Category ][ 4 bytes: SubType ][ 4 bytes: 長度 ][ N bytes: 資料 ]
```

Server 根據 Version 選擇不同的解析邏輯，新舊 Client 都能連線。

---

## 本專案服務 Port

| 服務              | Port | 說明           |
|-------------------|------|----------------|
| Demo.TcpService   | 5400 | TCP Socket Raw |

---

## 實作步驟（從零開始重現）

### 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.TcpService/Protocol/MessageCategory.cs` | 新增 | 大分類 enum（Item、System...）|
| `Demo.TcpService/Protocol/ItemSubType.cs` | 新增 | Item 類別的子類型 enum（Query、Create）|
| `Demo.TcpService/Protocol/SystemSubType.cs` | 新增 | System 類別的子類型 enum（Heartbeat）|
| `Demo.TcpService/Protocol/Packet.cs` | 新增 | 封包資料結構（解析後的結果）|
| `Demo.TcpService/Protocol/IMessageHandler.cs` | 新增 | Handler 的共同介面（Strategy Pattern）|
| `Demo.TcpService/Protocol/Handlers/ItemQueryHandler.cs` | 新增 | 處理 Item 查詢的 Handler |
| `Demo.TcpService/Protocol/Handlers/ItemCreateHandler.cs` | 新增 | 處理 Item 新增的 Handler |
| `Demo.TcpService/Protocol/Handlers/SystemHeartbeatHandler.cs` | 新增 | 處理心跳的 Handler |
| `Demo.TcpService/TcpServer.cs` | 新增 | TCP 伺服器核心：接收連線、解析封包、分派 Handler |
| `Demo.TcpService/Program.cs` | 新增 | 啟動 TcpServer，處理 Ctrl+C 優雅關閉 |

---

### 步驟 1：建立專案

```bash
dotnet new console -n Demo.TcpService
dotnet sln add Demo.TcpService/Demo.TcpService.csproj
```

TCP Socket 在 .NET 標準函式庫（`System.Net.Sockets`）裡，不需要安裝額外套件。

---

### 步驟 2：定義封包協議（Protocol 層）

**目的**：先把「資料格式」定義清楚，之後讀寫都依照這個格式

**封包格式**（每筆資料）：
```
[ 4 bytes: Category ][ 4 bytes: SubType ][ 4 bytes: Data 長度 ][ N bytes: Data ]
```

**新增檔案**：`Protocol/MessageCategory.cs`

```csharp
namespace Demo.TcpService.Protocol;

public enum MessageCategory
{
    Item   = 1,   // 數字是二進位傳輸時的識別碼，不可隨意修改（改了舊 Client 就不相容）
    System = 2
}
```

**新增檔案**：`Protocol/ItemSubType.cs`

```csharp
namespace Demo.TcpService.Protocol;

public enum ItemSubType
{
    Query  = 1,
    Create = 2
}
```

**新增檔案**：`Protocol/SystemSubType.cs`

```csharp
namespace Demo.TcpService.Protocol;

public enum SystemSubType
{
    Heartbeat = 1  // Client 定期送心跳，讓 Server 知道連線還活著
}
```

**新增檔案**：`Protocol/Packet.cs`

```csharp
namespace Demo.TcpService.Protocol;

// 封包解析後的結果，Handler 收到的就是這個物件
public class Packet
{
    public MessageCategory Category { get; init; }
    public int             SubType  { get; init; }
    public string          Data     { get; init; } = string.Empty;
}
```

---

### 步驟 3：定義 Handler 介面（Strategy Pattern）

**目的**：每種訊息類型有獨立的 Handler，新增類型只需要新增 Handler，不改既有程式碼（OCP 原則）

**新增檔案**：`Protocol/IMessageHandler.cs`

```csharp
namespace Demo.TcpService.Protocol;

public interface IMessageHandler
{
    // 輸入：Client 送來的 Data 字串
    // 輸出：要回傳給 Client 的字串
    Task<string> HandleAsync(string data);
}
```

**新增檔案**：`Protocol/Handlers/SystemHeartbeatHandler.cs`（以此為例）

```csharp
using Demo.TcpService.Protocol;

namespace Demo.TcpService.Protocol.Handlers;

public class SystemHeartbeatHandler : IMessageHandler
{
    public Task<string> HandleAsync(string data)
    {
        // 心跳不需要做任何事，只回應 "pong" 讓 Client 知道 Server 還活著
        return Task.FromResult("pong");
    }
}
```

---

### 步驟 4：建立 TcpServer 核心

**目的**：接受 Client 連線、解析封包、分派到對應 Handler

**新增檔案**：`TcpServer.cs`

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using Demo.TcpService.Protocol;
using Demo.TcpService.Protocol.Handlers;

public class TcpServer
{
    private readonly TcpListener _listener;

    // Dictionary 分派器：(Category, SubType) → 對應的 Handler
    // 查找是 O(1)，不用寫大量 if/else 或 switch
    private readonly Dictionary<(MessageCategory, int), IMessageHandler> _handlers = new()
    {
        { (MessageCategory.Item,   (int)ItemSubType.Query),       new ItemQueryHandler()       },
        { (MessageCategory.Item,   (int)ItemSubType.Create),      new ItemCreateHandler()      },
        { (MessageCategory.System, (int)SystemSubType.Heartbeat), new SystemHeartbeatHandler() },
    };

    public TcpServer(int port)
    {
        // IPAddress.Any：接受來自任何 IP 的連線
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener.Start();

        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(ct);
            // _ = ... 表示「不等待這個 Task」，讓主迴圈繼續接受下一個連線
            // 每個 Client 各自在獨立的 Task 裡處理，互不阻塞
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        await using var stream = client.GetStream();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 讀封包（固定順序：Category → SubType → Length → Data）
                var categoryBuffer = new byte[4];
                if (await ReadExactAsync(stream, categoryBuffer, ct) == 0) break;  // 0 = 連線關閉
                var category = (MessageCategory)BitConverter.ToInt32(categoryBuffer, 0);

                var subTypeBuffer = new byte[4];
                await ReadExactAsync(stream, subTypeBuffer, ct);
                var subType = BitConverter.ToInt32(subTypeBuffer, 0);

                var lengthBuffer = new byte[4];
                await ReadExactAsync(stream, lengthBuffer, ct);
                var dataLength = BitConverter.ToInt32(lengthBuffer, 0);

                var dataBuffer = new byte[dataLength];
                if (dataLength > 0)
                    await ReadExactAsync(stream, dataBuffer, ct);
                var data = Encoding.UTF8.GetString(dataBuffer);

                // 分派到對應 Handler
                var key = (category, subType);
                if (_handlers.TryGetValue(key, out var handler))
                {
                    var response = await handler.HandleAsync(data);
                    await SendMessageAsync(stream, response, ct);
                }
                else
                {
                    await SendMessageAsync(stream, $"[Error] Unknown Category={category}, SubType={subType}", ct);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Client 斷線或服務停止，正常情況，不需要特別處理
        }
        finally
        {
            client.Close();
        }
    }

    // ReadExactAsync：解決黏包問題的核心
    // ReadAsync 不保證一次讀完指定長度，必須循環讀到剛好讀完為止
    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, ct);
            if (read == 0) return 0;  // 連線已關閉
            totalRead += read;
        }
        return totalRead;
    }

    private static async Task SendMessageAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        var data        = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(data.Length);
        // 先送長度（4 bytes），再送資料（N bytes）
        // Client 知道要讀多少 bytes，解決黏包問題
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(data, ct);
    }

    public void Stop() => _listener.Stop();
}
```

---

### 步驟 5：Program.cs 啟動服務

**新增/修改檔案**：`Demo.TcpService/Program.cs`

```csharp
using Demo.TcpService;

var server = new TcpServer(5400);

// CancellationTokenSource：讓程式能優雅關閉（Ctrl+C 時通知所有 Task 停止）
using var cts = new CancellationTokenSource();

// Console.CancelKeyPress：捕捉 Ctrl+C，設定取消而非直接強制結束
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;          // 不讓 OS 直接 kill 程式
    cts.Cancel();             // 通知所有 Task 停止
};

await server.StartAsync(cts.Token);
server.Stop();
```

---

### 驗證方式

1. 啟動 Demo.TcpService
2. 啟動 Demo.TcpTestConsole（測試 Client）
3. 觀察雙方 console 的輸出，確認：
   - 連線建立：`[Server] Client connected:`
   - 收發訊息：`[Server] Received → Category: Item, SubType: 1`
   - 未知類型：收到 `[Error] Unknown Category=...`
   - 按 Ctrl+C：`[Server] Client disconnected:` 後服務正常結束（不是強制終止）
