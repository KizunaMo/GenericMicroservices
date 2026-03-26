# Phase 3-B：RabbitMQ + MassTransit 學習筆記

## 學習目標
理解非同步訊息通訊模式，讓服務之間透過 Message Broker 解耦，
不需要直接互相呼叫。

---

## 一、為什麼需要 Message Broker？

### 沒有 Message Broker 的問題

```
DataService 新增 Item 後，直接呼叫下游服務：

DataService → MailService.寄信()
            → CacheService.更新快取()
            → AuditService.記錄稽核()
```

問題：
- **耦合**：DataService 必須知道所有下游服務的位址與介面
- **等待**：寄信很慢（可能 3 秒），使用者要等完才拿到回應
- **脆弱**：MailService 掛掉 → DataService 也失敗
- **難擴展**：新增一個下游服務，就要改 DataService 的程式碼

### 有 Message Broker 的解法

```
DataService → 發布「ItemCreated 事件」→ RabbitMQ
                                            ↓
                              MailService 訂閱 → 收到後寄信
                              AuditService 訂閱 → 收到後記錄
```

DataService 只負責「告知有事發生」，立刻回傳結果給使用者。
後續的處理由訂閱者在背景非同步完成。

---

## 二、RabbitMQ 是什麼？

**RabbitMQ** 是一個開源的 Message Broker（訊息代理），
扮演訊息的「中間人 / 郵局」角色。

### 安裝（macOS Homebrew）

```bash
brew install rabbitmq
brew services start rabbitmq   # 啟動（開機自動啟動）
brew services stop rabbitmq    # 停止
brew services restart rabbitmq # 重啟
```

---

## 三、RabbitMQ 的 Port 與約定

**這些 Port 是業界約定俗成的預設值**，就像 HTTP 是 80、PostgreSQL 是 5432。

| Port | 用途 | 說明 |
|---|---|---|
| **5672** | AMQP 協議 | .NET / Java / Python 服務連這裡發送和接收訊息 |
| **15672** | Management UI | 網頁監控介面，瀏覽器開啟 http://localhost:15672 |
| **1883** | MQTT | 需要另外啟用 Plugin，IoT 裝置用這個 |
| **5671** | AMQPS | AMQP + TLS 加密版本（正式環境用） |
| **15671** | Management UI + TLS | 管理介面加密版本 |

### Management UI 預設帳號
```
URL：http://localhost:15672
帳號：guest
密碼：guest
（正式環境必須修改，guest 帳號預設只能本機登入）
```

---

## 四、RabbitMQ 核心概念

```
Producer（發送方）
    ↓ 發布訊息
Exchange（交換機）← 決定訊息要送到哪個 Queue
    ↓ 根據規則路由
Queue（佇列）← 訊息排隊等待
    ↓ 推送
Consumer（接收方）
```

### Exchange 類型

| 類型 | 行為 | 適合場景 |
|---|---|---|
| **Direct** | 精確比對 routing key | 點對點，特定 Queue |
| **Fanout** | 廣播給所有綁定的 Queue | 所有訂閱者都要收到 |
| **Topic** | 萬用字元比對 | 分類訊息（log.error, log.info）|
| **Headers** | 根據 Header 比對 | 複雜條件路由 |

### 訊息的生命週期

```
1. Producer 發布訊息到 Exchange
2. Exchange 根據規則把訊息路由到對應的 Queue
3. 訊息在 Queue 裡等待（可持久化，重啟後不消失）
4. Consumer 連線後，RabbitMQ 把訊息推送給 Consumer
5. Consumer 處理完後，發送 ACK（確認）
6. RabbitMQ 收到 ACK 後，從 Queue 刪除訊息
```

### ACK（確認機制）

```
Consumer 沒有發 ACK 就掛掉 → RabbitMQ 把訊息重新放回 Queue
                            → 其他 Consumer 或重啟後繼續處理
```
這確保訊息**不會因為 Consumer 崩潰而遺失**。

---

## 五、RabbitMQ 支援的協議

RabbitMQ 是一個多協議的 Message Broker：

| 協議 | Port | 用途 |
|---|---|---|
| AMQP 0-9-1 | 5672 | 預設協議，.NET/Java 主要使用 |
| MQTT | 1883 | IoT 裝置（需啟用 Plugin） |
| STOMP | 61613 | Web / WebSocket（需啟用 Plugin） |

### 與 MQTT 的關係（iBMS 重要）

```
IoT 設備（電表、感測器）
    ↓ MQTT（輕量，省電，適合嵌入式）
RabbitMQ MQTT Plugin（統一入口）
    ↓ 內部轉換為 AMQP
.NET 後端服務（DataService、AlertService）
```

**不需要兩套 Broker**，RabbitMQ 同時支援兩種協議，
IoT 設備用 MQTT 進來，後端用 AMQP 處理，中間自動橋接。

---

## 六、MassTransit 是什麼？

**MassTransit** 是 .NET 的 Message Bus 抽象層套件。

### 與 RabbitMQ 的關係

```
你的 C# 程式碼
    ↓ 使用
MassTransit（高層抽象）← 類比：SignalR 之於 WebSocket
    ↓ 底層連接
RabbitMQ（基礎設施）← 類比：WebSocket Server
    ↓
實際的訊息傳輸
```

### 為什麼不直接用 RabbitMQ.Client？

| | RabbitMQ.Client（底層） | MassTransit（高層） |
|---|---|---|
| 使用難度 | 要自己管理 Connection、Channel、Exchange | 幾行程式碼 |
| 錯誤處理 | 自己實作重試、ACK | 內建 |
| 換 Broker | 要重寫程式碼 | 只改設定（可換 Kafka、Azure SB）|
| 序列化 | 自己處理 | 內建 JSON 序列化 |

### 安裝（在需要的專案執行）

```bash
# ⚠️ 重要：MassTransit v9+ 需要付費授權，使用 v8.x（免費）
dotnet add package MassTransit.RabbitMQ --version 8.3.6
```

### 版本選擇

| 版本 | 授權 | 說明 |
|---|---|---|
| v8.x（8.3.6）| 免費開源 | 學習與商業使用均可 |
| v9.x+ | 付費 | 需要 License Key |

---

## 七、Domain Event（領域事件）

### 這就是 Domain Event

`ItemCreated` 是一個 **Domain Event**，來自 DDD（Domain-Driven Design）的概念：

> **Domain Event = 「某件事情在系統中發生了」的事實記錄**

```
ItemCreated    = Item 被建立了
OrderPlaced    = 訂單被下了
UserRegistered = 使用者註冊了
PaymentFailed  = 付款失敗了
```

### Domain Event 的特性

| 特性 | 說明 | 我們的實作 |
|---|---|---|
| 過去式命名 | 已發生的事實 | `ItemCreated`（不是 `CreateItem`）|
| 不可變 | 事實不能被修改 | `record` + `init` |
| 只帶資料 | 不帶行為 | 純資料結構，無方法 |
| 發布後不管 | 發布者不等待結果 | `await publishEndpoint.Publish(...)` |

### 和 Command 的差異

```
Command（命令）= 請求做某件事，可能被拒絕
  CreateItemCommand → 「請建立 Item」（可能失敗）

Event（事件）= 某件事已經發生，不可撤銷
  ItemCreated → 「Item 已建立」（事實）
```

---

## 八、Demo.Contracts 的架構意義

### 為什麼要獨立一個 Contracts 專案？

**單一事實來源（Single Source of Truth）**：

```
❌ 各自定義（容易不一致）
DataService/Events/ItemCreated.cs   ← 一份
Demo.Worker/Events/ItemCreated.cs   ← 另一份
→ 加欄位時漏改一個 → 兩邊型別不一致 → 執行期錯誤

✅ 共用 Demo.Contracts（永遠一致）
Demo.Contracts/ItemCreated.cs       ← 唯一一份
→ 改這裡 → 兩個專案編譯時都會發現問題
```

### 同 Solution vs 不同 Repository

| 情況 | Reference 方式 | 適用時機 |
|---|---|---|
| 同一個 Solution | `<ProjectReference>` | 開發階段、學習 |
| 不同 Repo，公司內部 | 私有 NuGet Package | 正式微服務 |
| 開源 | 公開 NuGet Package | 開源套件 |

### 未來正式化的流程

```bash
# 1. 打包 Demo.Contracts
dotnet pack Demo.Contracts --output ./nupkgs

# 2. 上傳到私有 NuGet Server（公司內部）
dotnet nuget push ./nupkgs/Demo.Contracts.1.0.0.nupkg \
  --source https://nuget.mycompany.com

# 3. 其他服務安裝（不需要在同一個 Repo）
dotnet add package Demo.Contracts
```

### 對應你的 AMO_Dev

```
Unity（AMO_Dev）                  .NET（Demo.Contracts）
com.kizunamo.amo_dev         →    Demo.Contracts
Unity Package（UPM）          →    NuGet Package
package.json + Git URL       →    .nupkg + NuGet Server
其他專案 import               →    dotnet add package
```

概念完全相同，工具不同而已。

---

## 九、我們的實作架構

```
POST /api/items
    ↓
DataService
    ├── 儲存 Item 到 PostgreSQL
    ├── 發布 ItemCreated 事件 → RabbitMQ（非同步，不等待）
    └── 立即回傳 201 Created

RabbitMQ（localhost:5672）
    ↓ 推送訊息
Demo.Worker
    └── 收到 ItemCreated → 處理（記錄 log / 之後可寄信、推通知）
```

### 事件定義（共用 Contract）

```csharp
// 共用的事件資料結構
public record ItemCreated
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
```

### DataService 發布

```csharp
// 注入 IPublishEndpoint（MassTransit）
await publishEndpoint.Publish(new ItemCreated { Id = item.Id, Name = item.Name });
```

### Demo.Worker 訂閱

```csharp
public class ItemCreatedConsumer : IConsumer<ItemCreated>
{
    public async Task Consume(ConsumeContext<ItemCreated> context)
    {
        var item = context.Message;
        Console.WriteLine($"收到新 Item：{item.Id} - {item.Name}");
        // 之後可以：寄信、推 SignalR 通知、更新快取...
    }
}
```

---

## 八、常見 Port 約定一覽（後端工程師必知）

| 服務 | 預設 Port | 說明 |
|---|---|---|
| HTTP | 80 | 標準網頁 |
| HTTPS | 443 | 加密網頁 |
| PostgreSQL | 5432 | 關聯式資料庫 |
| MySQL | 3306 | 關聯式資料庫 |
| MongoDB | 27017 | 文件資料庫 |
| Redis | 6379 | 快取 / Session |
| RabbitMQ AMQP | 5672 | Message Broker |
| RabbitMQ UI | 15672 | 管理介面 |
| RabbitMQ MQTT | 1883 | IoT 訊息 |
| Kafka | 9092 | 高吞吐量訊息串流 |
| gRPC | 慣例 443/50051 | 高效能 RPC |
| SMTP（寄信）| 25 / 587 | 郵件傳輸 |
| SSH | 22 | 遠端登入 |

**記憶技巧**：不需要全背，知道「PostgreSQL 是 5432」、「RabbitMQ AMQP 是 5672」就夠，其他查文件。
