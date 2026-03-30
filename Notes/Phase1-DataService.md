# Phase 1：DataService 學習筆記

## 學習目標
建立一個完整的 REST API 後端服務，能夠讀寫 PostgreSQL 資料庫。

## 已完成章節

### 1-A：環境建立 + CRUD API（已完成）

**學到的概念**
- Solution vs 專案：`.sln` 是容器，可以放多個 `.csproj` 專案
- EF Core：ORM 橋接層，讓你用 C# 物件操作資料庫，不用自己寫 SQL
- REST API 動詞：GET（讀）、POST（新增）、PUT/PATCH（更新）、DELETE（刪除）
- `db.Database.EnsureCreated()`：啟動時自動建立資料表

---

### 1-B：Repository Pattern（已完成）

**學習目標**
- 理解為什麼要把資料庫操作從 Program.cs 抽出來 ✅
- 建立通用 `IRepository<T>` 介面 ✅
- 建立通用 `Repository<T>` EF Core 底層實作 ✅
- `ItemRepository` 繼承底層，只寫 Item 特有邏輯 ✅
- 使用 DI 註冊，endpoint 只依賴介面 ✅

**最終結構**
```
Core/Repositories/
├── IRepository.cs      ← 通用介面（未來 NuGet）
└── Repository.cs       ← 通用 EF Core 底層（未來 NuGet）

Features/Items/
├── Item.cs             ← 資料模型
└── ItemRepository.cs  ← 繼承 Repository<Item>
```

**疑惑與解答**

Q：為什麼不需要像 AMO_Dev 一樣有 `DatabaseResult<T>` wrapper？
A：.NET 後端有 Exception Middleware 統一攔截錯誤，不需要每個方法自己包 result。之後 Phase 1-C 會建統一回應格式。

Q：為什麼 AMO_Dev 比較複雜？
A：Unity 沒有內建的 HTTP pipeline、錯誤攔截機制、Model Validation。.NET 這些都內建，所以 Repository 可以更薄。

Q：`Repository<T>` 接受 `DbContext`（而非 `AppDbContext`）的原因？
A：讓底層不綁死任何專案的 DbContext，未來抽成 NuGet 後，任何 EF Core 專案都能用。

Q：`_db.Set<T>()` 是什麼？
A：EF Core 的泛型方法，T = Item 時等同於 `_db.Items`，T = Order 時等同於 `_db.Orders`，不需要寫死 DbSet 名稱。

Q：`AddScoped` 是什麼？
A：每個 HTTP request 建立一個新的 Repository instance，request 結束自動銷毀。

---

### 1-C：錯誤處理與統一回應格式（已完成）

**學習目標**
- 讓所有 API 回傳統一的 JSON 格式 ✅
- 建立全域錯誤攔截 ✅

**新增檔案**
```
Core/Common/
├── ApiResponse.cs                  ← 統一回應容器
└── Middleware/
    └── ExceptionMiddleware.cs      ← 全域例外攔截
```

**ApiResponse 格式**
```json
// 成功
{ "success": true, "data": {...}, "error": null }
// 失敗
{ "success": false, "data": null, "error": "Item with id 99 not found" }
```

**關鍵概念**
- `init` 屬性：只能在建立時設定，之後不可修改，比 `set` 更安全
- Middleware 洋蔥模型：Request 進入 → 穿越各層 → Response 返回，例外在最外層攔截
- `app.UseMiddleware<ExceptionMiddleware>()` 必須放最外層才能攔截所有例外


---

## 關鍵檔案對照

| 檔案 | 職責 |
|------|------|
| `Models/Item.cs` | 資料模型（對應資料表欄位） |
| `Data/AppDbContext.cs` | EF Core 的資料庫操作入口 |
| `appsettings.json` | PostgreSQL 連線字串 |
| `Program.cs` | API 路由與端點定義 |

---

## 實作步驟（從零開始重現）

### 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.DataService.csproj` | 修改 | 安裝 EF Core、Npgsql 套件 |
| `Features/Items/Item.cs` | 新增 | 資料模型，對應 DB 的 Items 資料表 |
| `Data/AppDbContext.cs` | 新增 | EF Core 的 DB 連線橋接器，定義哪些資料表 |
| `Core/Repositories/IRepository.cs` | 新增 | 通用 CRUD 介面，定義「資料庫操作的規格」 |
| `Core/Repositories/Repository.cs` | 新增 | 通用 CRUD 實作，底層用 EF Core |
| `Features/Items/ItemRepository.cs` | 新增 | Item 專屬的 Repository，繼承通用底層 |
| `Core/Common/ApiResponse.cs` | 新增 | 統一回應格式容器 |
| `Core/Common/Middleware/ExceptionMiddleware.cs` | 新增 | 全域錯誤攔截，統一回傳 500 JSON 格式 |
| `appsettings.json` | 修改 | 加入 PostgreSQL 連線字串 |
| `Program.cs` | 修改 | 註冊服務、掛載 Middleware、定義 API 端點 |

---

### 步驟 1：安裝套件

**目的**：讓 .NET 專案能使用 EF Core 和 PostgreSQL

```bash
dotnet add package Microsoft.EntityFrameworkCore --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.0
```

**為什麼需要三個套件**：
- `EntityFrameworkCore`：ORM 核心，提供 DbContext、LINQ 查詢等功能
- `Npgsql.EntityFrameworkCore.PostgreSQL`：PostgreSQL 的 EF Core Driver，讓 EF Core 能說 PostgreSQL 的語言
- `EntityFrameworkCore.Design`：提供 `dotnet ef` 指令（Migration 工具），只在開發時需要

---

### 步驟 2：建立資料模型

**目的**：定義一個 C# class 對應到 DB 的一張資料表

**新增檔案**：`Demo.DataService/Features/Items/Item.cs`

```csharp
namespace Demo.DataService.Features.Items;

public class Item
{
    public int Id { get; set; }                          // 主鍵，EF Core 自動識別 Id 為 PK
    public string Name { get; set; } = string.Empty;    // = string.Empty 避免 null warning
    public string Description { get; set; } = string.Empty;
}
```

**為什麼這樣寫**：EF Core 的慣例（Convention over Configuration）會自動把名為 `Id` 的屬性設為主鍵，不需要加 `[Key]` Attribute。每個 `public { get; set; }` 屬性都會對應到資料表的一個欄位。

---

### 步驟 3：建立 DbContext

**目的**：告訴 EF Core「這個 DB 有哪些資料表」

**新增檔案**：`Demo.DataService/Data/AppDbContext.cs`

```csharp
using Demo.DataService.Features.Items;
using Microsoft.EntityFrameworkCore;

namespace Demo.DataService.Data;

// DbContext 是 C# 程式和 DB 之間的橋接器
// 每個 DbSet<T> 對應到 DB 的一張資料表
public class AppDbContext : DbContext
{
    // options 由 DI 注入（在 Program.cs 設定連線字串）
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items { get; set; }  // → DB 裡的 "Items" 資料表
}
```

**為什麼繼承 DbContext**：DbContext 是 EF Core 提供的基底類別，包含了 LINQ 查詢翻譯成 SQL、追蹤物件變更、執行 SaveChanges 等功能。

---

### 步驟 4：設定連線字串

**目的**：告訴 EF Core 要連哪個 DB

**修改檔案**：`Demo.DataService/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=demo_db;Username=yuweilyutcit;Password="
  }
}
```

**連線字串各欄位說明**：
- `Host`：DB 伺服器位址（本機開發用 localhost）
- `Database`：資料庫名稱（EF Core 會自動建立）
- `Username` / `Password`：PostgreSQL 的登入帳號

---

### 步驟 5：建立通用 Repository 介面

**目的**：定義「所有資料表操作都應該支援的方法」，讓程式碼依賴介面而非具體實作（SOLID 的 DIP 原則）

**新增檔案**：`Demo.DataService/Core/Repositories/IRepository.cs`

```csharp
namespace Demo.DataService.Core.Repositories;

// where T : class → 限制 T 必須是 reference type（class），不能是 int 這種 value type
public interface IRepository<T> where T : class
{
    Task<IEnumerable<T>> GetAllAsync();
    Task<T?> GetByIdAsync(int id);
    Task AddAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveAsync();                    // 明確分開「準備操作」和「存檔」，符合 Unit of Work 概念
}
```

**為什麼用泛型 `T`**：同一份介面可以用於 Item、Order、User 等任何資料模型，不需要每種資料各寫一個介面。

---

### 步驟 6：建立通用 Repository 實作

**目的**：用 EF Core 實作上面的介面，這份實作可以服務任何資料模型

**新增檔案**：`Demo.DataService/Core/Repositories/Repository.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Demo.DataService.Core.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly DbContext _db;  // 用 DbContext（基底類別）而非 AppDbContext，讓這個底層不綁死特定專案

    public Repository(DbContext db)
    {
        _db = db;
    }

    // _db.Set<T>() 是 EF Core 的泛型方法
    // T = Item 時等同於 _db.Items
    // T = Order 時等同於 _db.Orders
    public async Task<IEnumerable<T>> GetAllAsync()
        => await _db.Set<T>().ToListAsync();

    public async Task<T?> GetByIdAsync(int id)
        => await _db.Set<T>().FindAsync(id);

    public async Task AddAsync(T entity)
        => await _db.Set<T>().AddAsync(entity);

    public Task DeleteAsync(T entity)
    {
        _db.Set<T>().Remove(entity);   // Remove 不是 async，直接標記為刪除
        return Task.CompletedTask;
    }

    public async Task SaveAsync()
        => await _db.SaveChangesAsync();  // 實際送出 INSERT/UPDATE/DELETE SQL 到 DB
}
```

---

### 步驟 7：建立 Item 專屬 Repository

**目的**：每種資料模型有自己的 Repository，未來可以加入 Item 專屬的查詢邏輯（例如：依名稱搜尋），而不污染通用底層

**新增檔案**：`Demo.DataService/Features/Items/ItemRepository.cs`

```csharp
using Demo.DataService.Core.Repositories;
using Demo.DataService.Data;

namespace Demo.DataService.Features.Items;

// 繼承通用底層（Repository<Item>），直接繼承所有 CRUD 操作
// 注入的是 AppDbContext（具體類型），傳給父類別的 DbContext 參數
public class ItemRepository : Repository<Item>
{
    public ItemRepository(AppDbContext db) : base(db)
    {
        // 目前沒有 Item 專屬邏輯，未來可以在這裡加 SearchByName() 等方法
    }
}
```

---

### 步驟 8：建立統一回應格式

**目的**：所有 API 端點都回傳相同結構的 JSON，前端只需要處理一種格式

**新增檔案**：`Demo.DataService/Core/Common/ApiResponse.cs`

```csharp
namespace Demo.DataService.Core.Common;

public class ApiResponse<T>
{
    public bool Success { get; init; }   // init：只能在物件建立時設定，之後不可修改（比 set 更安全）
    public T? Data { get; init; }
    public string? Error { get; init; }

    // 靜態工廠方法，讓呼叫端不用自己 new
    public static ApiResponse<T> Ok(T data)
        => new() { Success = true, Data = data };

    public static ApiResponse<T> Fail(string error)
        => new() { Success = false, Error = error };
}
```

**回應範例**：
```json
// 成功
{ "success": true, "data": { "id": 1, "name": "Item A" }, "error": null }

// 失敗
{ "success": false, "data": null, "error": "Item with id 99 not found" }
```

---

### 步驟 9：建立全域錯誤攔截 Middleware

**目的**：任何未預期的例外都在這裡統一處理，回傳統一的 JSON 格式，不讓 ASP.NET 預設的 HTML 錯誤頁面洩漏

**新增檔案**：`Demo.DataService/Core/Common/Middleware/ExceptionMiddleware.cs`

```csharp
using System.Net;
using System.Text.Json;

namespace Demo.DataService.Core.Common.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;  // 代表下一個 Middleware 或最終的 Handler

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);  // 繼續往下執行，等待回應
        }
        catch (Exception ex)
        {
            // 攔截到任何未處理的例外
            context.Response.StatusCode  = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new { success = false, data = (object?)null, error = ex.Message };
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
```

**Middleware 洋蔥模型**：ExceptionMiddleware 必須放在最外層，這樣內層任何地方拋出的例外都能被它攔截：
```
→ ExceptionMiddleware（最外層，攔截所有例外）
  → 其他 Middleware
    → API Handler（最內層）
```

---

### 步驟 10：在 Program.cs 組裝所有東西

**目的**：把所有服務和 Middleware 串接起來

**修改檔案**：`Demo.DataService/Program.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Demo.DataService.Core.Common;
using Demo.DataService.Core.Common.Middleware;
using Demo.DataService.Core.Repositories;
using Demo.DataService.Data;
using Demo.DataService.Features.Items;

var builder = WebApplication.CreateBuilder(args);

// ── 1. 註冊 DbContext，傳入連線字串 ──────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── 2. 用 DI 註冊 Repository ─────────────────────────────────
// AddScoped：每個 HTTP request 建立一個新 instance，request 結束自動銷毀
// IRepository<Item> → ItemRepository（介面對應到實作）
builder.Services.AddScoped<IRepository<Item>, ItemRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── 3. 掛載 Middleware（順序很重要，最外層最先執行）─────────
app.UseMiddleware<ExceptionMiddleware>();

// ── 4. 啟動時建立 DB 和資料表 ────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();  // 第一次啟動時建立資料表
}

app.UseSwagger();
app.UseSwaggerUI();

// ── 5. 定義 API 端點 ──────────────────────────────────────────
app.MapGet("/api/items", async (IRepository<Item> repo) =>
{
    var items = await repo.GetAllAsync();
    return Results.Ok(ApiResponse<IEnumerable<Item>>.Ok(items));
});

app.MapGet("/api/items/{id}", async (int id, IRepository<Item> repo) =>
{
    var item = await repo.GetByIdAsync(id);
    return item is not null
        ? Results.Ok(ApiResponse<Item>.Ok(item))
        : Results.NotFound(ApiResponse<Item>.Fail($"Item with id {id} not found"));
});

app.MapPost("/api/items", async (Item item, IRepository<Item> repo) =>
{
    await repo.AddAsync(item);
    await repo.SaveAsync();
    return Results.Created($"/api/items/{item.Id}", ApiResponse<Item>.Ok(item));
});

app.MapDelete("/api/items/{id}", async (int id, IRepository<Item> repo) =>
{
    var item = await repo.GetByIdAsync(id);
    if (item is null)
        return Results.NotFound(ApiResponse<Item>.Fail($"Item with id {id} not found"));

    await repo.DeleteAsync(item);
    await repo.SaveAsync();
    return Results.NoContent();
});

app.Run();
```

---

### 驗證方式

1. `dotnet run` 啟動服務
2. 開啟 `http://localhost:5128/swagger`，應該看到 Swagger UI
3. 用 Swagger 的 `POST /api/items` 新增一筆資料
4. 用 `GET /api/items` 確認資料存在
5. 用 psql 或 pgAdmin 確認 `demo_db` 的 `Items` 資料表有資料
