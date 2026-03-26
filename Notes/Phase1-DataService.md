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
