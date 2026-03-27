# EF Core：Migration 與資料庫建立

## 背景：EF Core 如何建立資料表？

你在 C# 定義了 `DbContext` 和 Entity class，但資料庫裡的資料表不會自動出現。
EF Core 提供兩種方式把 C# 的定義同步到資料庫：

| 方式            | 指令 / 方法                    | 適合場景                   |
|-----------------|--------------------------------|----------------------------|
| `EnsureCreated` | `db.Database.EnsureCreated()`  | 開發初期、學習用、快速建立 |
| Migration       | `dotnet ef migrations add`     | 正式專案、需要版本控制     |

---

## 方式一：EnsureCreated（GenericMicroservices 用的）

```csharp
// Program.cs
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();  // ← 這行
}
```

**行為**：
- 啟動時檢查資料庫是否存在
- 如果不存在 → 自動建立資料庫和所有資料表
- 如果已存在 → **什麼都不做**（不會更新結構）

**缺點**：
- 無法追蹤「Schema 變更歷史」
- 如果你改了 Entity（例如新增欄位），`EnsureCreated` 不會更新資料表
- 不適合正式環境

**為什麼 GenericMicroservices 沒有 Migrations 資料夾？**
因為它用 `EnsureCreated`，不需要 Migration 檔案。

---

## 方式二：Migration（Demo.GrpcService 用的）

Migration 是 EF Core 的**版本控制系統**，記錄每一次 Schema 的變更。

### 怎麼產生 Migration？

```bash
dotnet ef migrations add InitialCreate
```

這個指令做了什麼：
1. 讀取目前的 `DbContext`（你的 C# 定義）
2. 比對上一次的 Schema 快照
3. 自動產生「這次變更」的程式碼

### 怎麼套用到資料庫？

```bash
dotnet ef database update
```

執行所有尚未套用的 Migration，把資料表建起來。

---

## Migration 產生的檔案

```
Migrations/
  20260327024250_InitialCreate.cs       ← Migration 內容
  20260327024250_InitialCreate.Designer.cs ← 工具用的 metadata
  AppDbContextModelSnapshot.cs          ← 目前 Schema 的快照
```

### `20260327024250_InitialCreate.cs`

檔名格式：`時間戳記_名稱.cs`

時間戳記確保多個 Migration 按照建立順序執行。

```csharp
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 建立資料表（套用這個 Migration）
        migrationBuilder.CreateTable(name: "GrpcItems", ...);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 回滾（撤銷這個 Migration）
        migrationBuilder.DropTable(name: "GrpcItems");
    }
}
```

- `Up()`：向前，套用這次變更
- `Down()`：回滾，撤銷這次變更

### `AppDbContextModelSnapshot.cs`

- 記錄目前 DB Schema 的**完整狀態**
- 不是給你看的，是給 EF Core 工具用的
- 下次執行 `migrations add` 時，工具用它來判斷「有哪些新變更需要記錄」

---

## 完整流程對比

### GenericMicroservices（EnsureCreated）

```
啟動
  │
  ↓
db.Database.EnsureCreated()
  │
  ├─ DB 不存在 → 建立資料庫和資料表
  └─ DB 已存在 → 不做任何事
```

### Demo.GrpcService（Migration）

```
開發階段（手動執行）：

  修改 Entity（C#）
    │
    ↓
  dotnet ef migrations add 描述名稱
    │
    ↓  自動產生 Migration 檔案
  dotnet ef database update
    │
    ↓
  資料庫更新完成

啟動時：
  不會自動執行任何 Migration
  需要開發者手動執行 dotnet ef database update
```

---

## 什麼時候用哪個？

| 情境                       | 建議方式                       |
|----------------------------|--------------------------------|
| 學習、快速 Demo             | `EnsureCreated`                |
| 正式專案                   | Migration                      |
| 需要追蹤 Schema 變更歷史   | Migration                      |
| 多人協作，DB 結構需要同步  | Migration                      |
| 需要回滾資料庫             | Migration（有 `Down()` 方法）  |

---

## 你需要做什麼 vs 不需要做什麼

```
你需要做的                          你不需要做的
────────────────────────────────    ────────────────────────────────
修改 Entity 後執行：                 不需要手動編輯 Migration 檔案
  dotnet ef migrations add          不需要整理或刪除 Migrations 資料夾
  dotnet ef database update         不需要理解 Designer.cs 的內容
新環境部署時執行：
  dotnet ef database update
```

**Migration 檔案應該 commit 進 git。**
團隊成員拿到程式碼後執行 `dotnet ef database update`，就能把資料庫同步到最新狀態。

---

## 什麼時候需要新增 Migration？

| 情況                               | 需要 Migration？ |
|------------------------------------|-----------------|
| 新增 Entity 欄位（例如加 `Price`）  | 是              |
| 刪除 Entity 欄位                   | 是              |
| 新增一個全新的 Entity class         | 是              |
| 修改欄位類型                        | 是              |
| 只改了商業邏輯（Service / Controller）| 否             |
| 只改了 `.proto` 定義               | 否              |

---

## 常用 Migration 指令

```bash
# 建立新的 Migration
dotnet ef migrations add 描述名稱

# 套用到資料庫
dotnet ef database update

# 回滾到上一個 Migration
dotnet ef database update 上一個Migration的名稱

# 移除最後一個 Migration（尚未套用到 DB 才能用）
dotnet ef migrations remove

# 查看所有 Migration 狀態
dotnet ef migrations list
```
