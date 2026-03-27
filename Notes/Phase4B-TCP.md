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
