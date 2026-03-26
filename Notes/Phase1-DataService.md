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

**git commit**：`a1f22f2` Initial commit

---

### 1-B：Repository Pattern（進行中）

**學習目標**
- 理解為什麼要把資料庫操作從 Program.cs 抽出來
- 建立 `IItemRepository` 介面
- 建立 `ItemRepository` 實作
- 使用 DI（依賴注入）讓 API endpoint 使用 Repository

**疑惑與解答**
（學習過程中記錄）

---

### 1-C：錯誤處理與統一回應格式（待進行）

---

## 關鍵檔案對照

| 檔案 | 職責 |
|------|------|
| `Models/Item.cs` | 資料模型（對應資料表欄位） |
| `Data/AppDbContext.cs` | EF Core 的資料庫操作入口 |
| `appsettings.json` | PostgreSQL 連線字串 |
| `Program.cs` | API 路由與端點定義 |