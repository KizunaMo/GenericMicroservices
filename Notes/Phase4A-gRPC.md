# Phase 4-A：gRPC

## 什麼是 gRPC？

- Google 開發的高效能 RPC（Remote Procedure Call）框架
- 使用 **Protocol Buffers（protobuf）** 作為序列化格式（二進位，非 JSON）
- 適合**服務與服務之間**的通訊，不適合直接給瀏覽器用
- 速度比 REST + JSON 快 3-10 倍，payload 更小

### 和 REST 的比較

| 項目       | REST + JSON      | gRPC + Protobuf      |
|------------|------------------|----------------------|
| 格式       | 文字（人眼可讀）  | 二進位（人眼不可讀）  |
| 速度       | 較慢              | 快 3-10 倍            |
| 合約定義   | 無強制            | `.proto` 強制定義     |
| 瀏覽器支援 | 完整              | 受限                  |
| 適合場景   | 對外 API          | 服務間通訊            |

---

## Protocol Buffers（protobuf）

- 序列化 / 反序列化是**自動的**，不需要手動處理二進位資料
- 你只需要寫 `.proto` 定義檔，工具幫你生成 C# 程式碼

---

## 自動生成的程式碼在哪裡？

`dotnet build` 執行後，Grpc.Tools 會把生成的 C# 程式碼放在：

```
obj/Debug/net8.0/Protos/
  ├── Items.cs       ← 所有 message 對應的 C# class（ItemResponse、GetItemRequest 等）
  └── ItemsGrpc.cs   ← ItemService.ItemServiceBase、ItemService.ItemServiceClient
```

你**不需要動這些檔案**，它們每次 build 都會重新生成。

### items.proto → 生成對應關係

```
items.proto
  service ItemService        → 生成外層 class ItemService
    rpc GetItem(...)         → 生成 ItemServiceBase.GetItem()   （你 override）
    rpc GetAllItems(...)     → 生成 ItemServiceBase.GetAllItems()（你 override）
  message ItemResponse       → 生成 Items.cs 裡的 ItemResponse class
  message GetItemRequest     → 生成 Items.cs 裡的 GetItemRequest class
  message GetAllItemsRequest → 生成 Items.cs 裡的 GetAllItemsRequest class
  message ItemListResponse   → 生成 Items.cs 裡的 ItemListResponse class
```

**.proto 是模具，生成的程式碼是從模具印出的骨架，你只負責填入實作邏輯。**

---

## `.csproj` 是什麼？在哪裡？

`.csproj` 是每個 .NET 專案的**設定檔**，記錄：
- 目標框架（`net8.0`）
- 安裝的 NuGet 套件（`PackageReference`）
- 需要處理的特殊檔案（例如 `.proto`）

位置：每個專案資料夾的根目錄，和 `Program.cs` 同層。

```
Demo.GrpcService/
  ├── Demo.GrpcService.csproj   ← 這裡
  ├── Program.cs
  ├── Protos/
  │   └── items.proto
  └── Services/
      └── ItemGrpcService.cs
```

在 Rider 中可以在 Solution Explorer 直接雙擊開啟。

---

## `.proto` 檔案語法

### 基本結構（每個 .proto 檔開頭都要寫）

```proto
syntax = "proto3";                            // 使用 proto3 版本（必填）
option csharp_namespace = "Demo.GrpcService"; // 生成的 C# 程式碼放在哪個 namespace
package items;                                // proto 自己的命名空間，防止跨檔案名稱衝突
```

---

### message — 定義資料結構

相當於 C# 的 `class`，每個欄位格式為：`類型 名稱 = 欄位編號;`

```proto
message ItemResponse {
  int32  id          = 1;
  string name        = 2;
  string description = 3;
}
```

> **欄位編號**（`= 1`, `= 2`...）是給二進位編碼用的識別碼，**不是預設值、不是順序索引**。
> 一旦定義後不可更改，否則會破壞資料相容性。

#### 常用欄位類型

| proto 類型 | C# 類型 |
|---|---|
| `int32` | `int` |
| `int64` | `long` |
| `string` | `string` |
| `bool` | `bool` |
| `double` | `double` |
| `bytes` | `ByteString` |

#### repeated — 陣列

```proto
message ItemListResponse {
  repeated ItemResponse items = 1;  // 相當於 List<ItemResponse>
}
```

生成的 C# 類型是 `RepeatedField<ItemResponse>`，填入資料用 `AddRange()`。

#### 空 message

proto3 的每個方法**一定要有輸入和輸出**，沒有參數也必須定義空 message（不能用 `void`）：

```proto
message GetAllItemsRequest {
  // 空的，但不能省略
}
```

---

### service — 定義可呼叫的方法

相當於 C# 的 `interface`，格式：

```
rpc 方法名稱 (輸入 message) returns (輸出 message);
```

```proto
service ItemService {
  rpc GetItem     (GetItemRequest)     returns (ItemResponse);
  rpc GetAllItems (GetAllItemsRequest) returns (ItemListResponse);
}
```

---

### 完整範例 `items.proto`

```proto
syntax = "proto3";

option csharp_namespace = "Demo.GrpcService";

package items;

service ItemService {
  rpc GetItem     (GetItemRequest)     returns (ItemResponse);
  rpc GetAllItems (GetAllItemsRequest) returns (ItemListResponse);
}

message GetItemRequest {
  int32 id = 1;
}

message GetAllItemsRequest {
}

message ItemResponse {
  int32  id          = 1;
  string name        = 2;
  string description = 3;
}

message ItemListResponse {
  repeated ItemResponse items = 1;
}
```

---

## `.csproj` 設定：Server vs Client 的差異

### 為什麼兩邊都要設定？

`.proto` 只是一份「合約描述文件」，本身不是程式碼。
必須在 `.csproj` 中登記，`dotnet build` 時工具才會根據它**自動生成 C# 程式碼**。

Server 和 Client 生成的東西不同，所以用 `GrpcServices` 屬性區分：

| 設定值 | 生成內容 | 給誰用 |
|---|---|---|
| `GrpcServices="Server"` | `ItemService.ItemServiceBase`（抽象 base class） | Server 繼承並實作 |
| `GrpcServices="Client"` | `ItemService.ItemServiceClient`（可直接呼叫的 client） | Client new 出來用 |

---

### Server 的 `.csproj` 設定

```xml
<ItemGroup>
  <Protobuf Include="Protos\items.proto" GrpcServices="Server" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Grpc.AspNetCore" Version="2.57.0" />
</ItemGroup>
```

套件說明：
- `Grpc.AspNetCore`：整合包，包含 Server 需要的所有東西

---

### Client 的 `.csproj` 設定

```xml
<ItemGroup>
  <Protobuf Include="Protos\items.proto" GrpcServices="Client" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Grpc.Net.Client" Version="2.76.0" />
  <PackageReference Include="Google.Protobuf" Version="3.34.1" />
  <PackageReference Include="Grpc.Tools" Version="2.78.0">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
```

套件說明：
- `Grpc.Net.Client`：建立連線、呼叫遠端方法
- `Google.Protobuf`：序列化 / 反序列化
- `Grpc.Tools`：從 `.proto` 生成 C# 程式碼的工具（只在 build 時用，不打包進執行檔）

---

## 兩個專案都要放同一份 `.proto`？

**是的，內容完全一樣，但各自持有一份。**

```
Demo.GrpcService/Protos/items.proto   ← GrpcServices="Server"
Demo.GrpcClient/Protos/items.proto    ← GrpcServices="Client"（內容相同）
```

原因：在真實微服務架構中，Server 和 Client 通常是**不同 repo、不同團隊**。
各自持有一份是業界慣例，合約由 Server 方維護，有更新時通知 Client 方同步。

---

## `dotnet build` 時發生了什麼

```
items.proto
    ↓ Grpc.Tools 讀取
自動生成（放在 obj/ 資料夾，你不需要動它）：
  - ItemService.ItemServiceBase   ← 只在 GrpcServices="Server" 時生成
  - ItemService.ItemServiceClient ← 只在 GrpcServices="Client" 時生成
  - ItemResponse、GetItemRequest 等 message 對應的 C# class
```

> **命名衝突注意**：`service ItemService` 會生成名為 `ItemService` 的 class。
> 你自己的實作類別**不能也叫 `ItemService`**，應改名為 `ItemGrpcService`。

---

## Server 實作

```csharp
// 繼承自動生成的 ItemService.ItemServiceBase
public class ItemGrpcService : ItemService.ItemServiceBase
{
    public override Task<ItemResponse> GetItem(GetItemRequest request, ServerCallContext context)
    {
        var item = _items.FirstOrDefault(x => x.Id == request.Id);

        if (item is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Item {request.Id} not found"));

        return Task.FromResult(item);
    }

    public override Task<ItemListResponse> GetAllItems(GetAllItemsRequest request, ServerCallContext context)
    {
        var response = new ItemListResponse();
        response.Items.AddRange(_items);  // repeated 欄位用 AddRange
        return Task.FromResult(response);
    }
}
```

### gRPC 錯誤處理

不回傳 HTTP 404，而是拋出 `RpcException`：

```csharp
throw new RpcException(new Status(StatusCode.NotFound, "Item 99 not found"));
```

常用 StatusCode：

| StatusCode | 對應概念 |
|---|---|
| `NotFound` | 404 |
| `InvalidArgument` | 400 |
| `Unauthenticated` | 401 |
| `PermissionDenied` | 403 |
| `Internal` | 500 |

---

## Program.cs 註冊（Server）

```csharp
builder.Services.AddGrpc();                    // 加入 gRPC 服務
app.MapGrpcService<ItemGrpcService>();          // 掛載實作類別
```

---

## Client 呼叫方式

```csharp
// 建立連線（channel 可重複使用）
using var channel = GrpcChannel.ForAddress("http://localhost:5300");

// 建立 client（從 .proto 自動生成的）
var client = new ItemService.ItemServiceClient(channel);

// 呼叫方式和呼叫本地方法幾乎一樣
var allItems = await client.GetAllItemsAsync(new GetAllItemsRequest());
var single   = await client.GetItemAsync(new GetItemRequest { Id = 2 });

// 捕捉錯誤
try { ... }
catch (Grpc.Core.RpcException ex)
{
    Console.WriteLine($"{ex.StatusCode} - {ex.Status.Detail}");
}
```

---

## 測試 gRPC 的方式

| 方式 | 說明 |
|---|---|
| **Postman** | GUI 工具，匯入 `.proto` 後可手動測試，推薦 |
| **Demo.GrpcClient** | Console App，模擬其他服務呼叫，可當程式碼參考 |
| **grpcurl** | 命令列工具（`brew install grpcurl`），類似 curl |

### Postman 測試步驟
1. New → gRPC
2. Server URL：`localhost:5300`
3. 匯入 `items.proto`
4. 選擇方法 → 填參數 → Invoke

---

## 整體架構總覽

```
items.proto（合約，兩邊各持有一份）
    │
    ├─ GrpcServices="Server" → 生成 ItemService.ItemServiceBase
    │       ↓ 你繼承並實作
    │   ItemGrpcService.cs
    │       ↓ 註冊
    │   Program.cs → app.MapGrpcService<ItemGrpcService>()
    │
    └─ GrpcServices="Client" → 生成 ItemService.ItemServiceClient
            ↓ 直接 new 出來用
        var client = new ItemService.ItemServiceClient(channel);
        await client.GetItemAsync(...)
```

真實微服務場景中，Client 端的程式碼會在**另一個服務的內部**：

```
OrderService（下訂單）
    ↓ gRPC 呼叫（像呼叫本地方法）
InventoryService（查庫存）
```

---

## 為什麼 Demo.GrpcService 沒有經過 Gateway？

這是**刻意的設計**，不是遺漏。

### Gateway 是給誰的？

Gateway 的職責是處理**外部流量**：

```
瀏覽器 / 手機 App
        ↓ HTTP / REST
    Demo.Gateway（唯一對外入口）
        ↓ 路由轉發
    DataService、RealTime...
```

Gateway 負責的事：CORS、路由、JWT 驗證（未來）。
這些都是針對**不受信任的外部請求**才需要的保護。

### gRPC 是給誰的？

gRPC 是**後端服務與後端服務之間**的通訊，呼叫方是另一個後端服務，不是瀏覽器：

```
ServiceA（後端）
    ↓ gRPC（內部網路）
ServiceB（後端）
```

呼叫方是可信任的內部服務，不需要 CORS，也不需要 Gateway 的保護層。
**繞過 Gateway 是正確的架構決策。**

### 真實微服務架構中的全貌

```
瀏覽器
  │
  │ REST（對外）
  ↓
Gateway
  │
  │ REST（內部轉發）
  ↓
OrderService ──── gRPC（內部）──→ InventoryService
             └─── gRPC（內部）──→ PricingService
```

- 瀏覽器只認識 Gateway，完全不知道 gRPC 服務的存在
- gRPC 走內部網路，速度快、不需要 CORS
- REST、gRPC、SignalR、RabbitMQ 在同一個系統裡**同時存在**，各負責不同場景

### 各通訊方式的適用場景

| 通訊方式  | 方向          | 適合場景                     |
|-----------|---------------|------------------------------|
| REST      | 外部 → 內部   | 瀏覽器 / App 呼叫 API        |
| gRPC      | 內部 ↔ 內部   | 服務間高效呼叫               |
| SignalR   | 內部 → 外部   | Server 主動推播給瀏覽器      |
| RabbitMQ  | 內部 ↔ 內部   | 非同步解耦，不需即時回應     |

---

## 微服務與資料庫的正確關係

### 核心原則：每個服務擁有自己的 DB

微服務架構中，**每個服務應該有獨立的資料庫**，不允許跨越服務邊界直接操作別人的 DB。

```
DataService → demo_db（自己的）
GrpcService → grpc_db（自己的）
```

### 為什麼不能共用 DB？

| 問題 | 說明 |
|---|---|
| 資料衝突 | 兩個服務同時寫入同一筆資料，後寫的覆蓋前寫的 |
| Schema 衝突 | 兩個服務各自跑 Migration，可能互相破壞資料表結構 |
| 耦合 | 共用 DB 代表兩個服務無法獨立部署，違反微服務精神 |

### 需要對方資料怎麼辦？

**不直接連對方的 DB，而是透過 API 請求對方服務處理。**

```
情境一：GrpcService 有自己的資料
    GrpcService → grpc_db（自己管理）

情境二：GrpcService 需要 DataService 的 Item 資料
    GrpcService → HTTP 請求 → DataService → demo_db
    （DataService 才是 demo_db 的唯一擁有者）
```

### 判斷原則

> 「這筆資料是誰的責任？」
> - 是自己的資料 → 自己的 DB
> - 是別人的資料 → 請求那個服務處理，不要直接碰它的 DB

### 本專案的做法

`Demo.GrpcService` 使用獨立的 `grpc_db`，資料表命名為 `GrpcItems`，
明確和 DataService 的 `Items` 資料表區別，代表這是兩個完全不同的資料集合。

---

## 本專案服務 Port

| 服務 | Port | 協議 | 用途 |
|---|---|---|---|
| DataService | 5128 | HTTP/1.1 | REST API + Swagger |
| DataService | 5129 | HTTP/2 | gRPC（供內部服務呼叫）|
| Demo.GrpcService | 5300 | HTTP/2 | gRPC |
| Demo.Gateway | 5000 | HTTP/1.1 | 對外入口 |

> HTTP/2 設定細節見 `Notes/gRPC-HTTP2-Setup.md`
