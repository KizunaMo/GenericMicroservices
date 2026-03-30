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
    │       ↓ 監聽
    │   Demo.GrpcService :5300（HTTP/2）
    │
    └─ GrpcServices="Client" → 生成 ItemService.ItemServiceClient
            ↓ 直接 new 出來用
        var client = new ItemService.ItemServiceClient(channel);
        await client.GetItemAsync(...)
```

真實微服務場景中，Client 端的程式碼會在**另一個服務的內部**：

```
OrderService（下訂單）
    ↓ gRPC 呼叫（HTTP/2，內部網路）
InventoryService（查庫存）:50051
```

## 本專案 gRPC 呼叫鏈

```
Client（Postman / Demo.GrpcClient）
    │
    │ gRPC GetAllItems（HTTP/2）
    ▼
Demo.GrpcService :5300（HTTP/2）
    │
    ├── 查 grpc_db（PostgreSQL :5432）
    │   ItemGrpcService.GetAllItems()
    │
    └──（可選）gRPC 呼叫 DataService
            │
            │ gRPC（HTTP/2，內部網路）
            ▼
       Demo.DataService :5129（HTTP/2，gRPC 專用）
            │
            └── 查 demo_db（PostgreSQL :5432）

       注意：DataService :5128 是 REST（HTTP/1.1）
            DataService :5129 是 gRPC（HTTP/2）
            兩個 port 共存，由 ConfigureKestrel 分別設定
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

---

## 實作步驟（從零開始重現）

### 涉及的文件

**Demo.GrpcService（新服務，有自己的 grpc_db）**：

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.GrpcService.csproj` | 新增 | 安裝 gRPC 套件、宣告 .proto 檔案和角色（Server/Client）|
| `Protos/items.proto` | 新增 | 定義 gRPC 服務合約（方法、請求/回應的資料格式）|
| `Data/Item.cs` | 新增 | GrpcService 自己的 Item 資料模型（對應 grpc_db）|
| `Data/AppDbContext.cs` | 新增 | grpc_db 的 EF Core 橋接器 |
| `Migrations/` | 新增（自動產生）| DB Schema 版本記錄 |
| `Services/GrpcItemService.cs` | 新增 | 實作 ItemService 的 gRPC 方法 |
| `Program.cs` | 新增 | 設定 HTTP/2、註冊服務、掛載 gRPC Handler |

**（可選）GrpcService 呼叫 DataService**：

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Protos/dataservice_items.proto` | 新增 | DataService 暴露給 GrpcService 的 gRPC 合約 |
| `Services/DataBridgeGrpcService.cs` | 新增 | 呼叫 DataService gRPC，橋接資料 |
| `Demo.DataService/Protos/dataservice_items.proto` | 新增 | DataService 側的 proto（Server 角色）|
| `Demo.DataService/Services/DataItemGrpcService.cs` | 新增 | DataService 的 gRPC 實作 |

---

### 步驟 1：建立專案並安裝套件

```bash
dotnet new web -n Demo.GrpcService
dotnet sln add Demo.GrpcService/Demo.GrpcService.csproj
cd Demo.GrpcService
dotnet add package Grpc.AspNetCore
dotnet add package Microsoft.EntityFrameworkCore --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.0
```

---

### 步驟 2：在 .csproj 宣告 .proto 檔案

**目的**：告訴 build 工具「這些 .proto 要自動生成 C# 程式碼」，並指定角色

**修改檔案**：`Demo.GrpcService/Demo.GrpcService.csproj`

```xml
<ItemGroup>
  <!-- Server：生成 ItemService.ItemServiceBase（你繼承並實作）-->
  <Protobuf Include="Protos\items.proto" GrpcServices="Server" />

  <!-- Client：生成 DataItemService.DataItemServiceClient（你直接呼叫）-->
  <Protobuf Include="Protos\dataservice_items.proto" GrpcServices="Client" />
</ItemGroup>
```

**兩個角色的差異**：
- `GrpcServices="Server"`：生成抽象 base class，你繼承後覆寫（override）方法填入邏輯
- `GrpcServices="Client"`：生成 Client class，你直接 new 出來呼叫遠端方法

---

### 步驟 3：撰寫 .proto 定義檔

**目的**：定義這個服務提供哪些 RPC 方法，以及資料的格式

**新增檔案**：`Demo.GrpcService/Protos/items.proto`

```protobuf
syntax = "proto3";                            // Protocol Buffers 版本

option csharp_namespace = "Demo.GrpcService"; // 生成的 C# 程式碼放在這個 namespace

package items;                                // protobuf 的 package（避免命名衝突）

// 服務定義：這個 service 提供的所有 RPC 方法
service ItemService {
  rpc GetItem     (GetItemRequest)     returns (ItemResponse);      // 取得單一 Item
  rpc GetAllItems (GetAllItemsRequest) returns (ItemListResponse);  // 取得所有 Item
}

// 請求/回應的資料格式（= 後面的數字是欄位編號，二進位傳輸用，不可修改）
message GetItemRequest {
  int32 id = 1;
}

message GetAllItemsRequest {
  // 空的訊息也需要定義，gRPC 不允許省略
}

message ItemResponse {
  int32  id          = 1;
  string name        = 2;
  string description = 3;
}

message ItemListResponse {
  repeated ItemResponse items = 1;  // repeated = 陣列/列表
}
```

---

### 步驟 4：實作 gRPC Service

**目的**：繼承 .proto 自動生成的 base class，填入實際的查詢邏輯

**新增檔案**：`Demo.GrpcService/Services/GrpcItemService.cs`

```csharp
using Demo.GrpcService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Demo.GrpcService.Services;

// 繼承自動生成的 ItemService.ItemServiceBase（不是你寫的，是 build 時從 .proto 生成的）
// 命名衝突注意：.proto 的 service 名稱是 ItemService，生成的 class 也叫 ItemService
// 所以你的實作類別要取不同名稱，例如 GrpcItemService
public class GrpcItemService : ItemService.ItemServiceBase
{
    private readonly AppDbContext _db;

    public GrpcItemService(AppDbContext db)
    {
        _db = db;
    }

    // override：覆寫 base class 的抽象方法，填入你的邏輯
    public override async Task<ItemResponse> GetItem(GetItemRequest request, ServerCallContext context)
    {
        var item = await _db.Items.FindAsync(request.Id);

        if (item is null)
            // gRPC 沒有 HTTP 404，用 RpcException + StatusCode.NotFound 代替
            throw new RpcException(new Status(StatusCode.NotFound, $"Item {request.Id} not found"));

        return new ItemResponse { Id = item.Id, Name = item.Name, Description = item.Description };
    }

    public override async Task<ItemListResponse> GetAllItems(GetAllItemsRequest request, ServerCallContext context)
    {
        var items = await _db.Items.ToListAsync();

        var response = new ItemListResponse();
        // response.Items 是 proto 的 repeated 欄位，對應到 C# 的 RepeatedField<T>
        // 不能直接賦值，需要用 AddRange
        response.Items.AddRange(items.Select(i => new ItemResponse
        {
            Id          = i.Id,
            Name        = i.Name,
            Description = i.Description,
        }));

        return response;
    }
}
```

---

### 步驟 5：設定 Program.cs

**目的**：讓服務在 HTTP/2（gRPC 必須）上啟動，掛載 gRPC Handler

**修改檔案**：`Demo.GrpcService/Program.cs`

```csharp
using Demo.GrpcService.Data;
using Demo.GrpcService.Services;
using Microsoft.EntityFrameworkCore;

// 允許對內部服務使用「明文 HTTP/2」（不加密，開發用）
// gRPC 必須走 HTTP/2，但開發環境不需要 TLS
// 不加這行，Client 端呼叫時會報錯：PROTOCOL_ERROR
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// DB 設定
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddGrpc();   // 啟用 gRPC 功能

var app = builder.Build();

// 執行 Migration，確保資料表存在
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapGrpcService<GrpcItemService>();  // 掛載 gRPC Handler（對應 proto 的 service ItemService）
app.MapGet("/", () => "gRPC service is running.");  // 提供一個 HTTP 端點確認服務活著

app.Run();
```

**Port 設定**：gRPC 需要 HTTP/2，在 `Properties/launchSettings.json` 設定：

```json
"applicationUrl": "http://localhost:5300"
```

開發環境用 HTTP（不是 HTTPS），因為 HTTP/2 明文（h2c）比較簡單，gRPC 正式環境才會用 HTTPS。

---

### 驗證方式

1. 啟動 Demo.GrpcService（port 5300）
2. 用 Postman 測試 gRPC：
   - New → gRPC
   - Server URL：`localhost:5300`
   - 匯入 `Protos/items.proto`
   - 選擇 `ItemService/GetAllItems` → Invoke
   - 預期回傳 GrpcItems 的資料
