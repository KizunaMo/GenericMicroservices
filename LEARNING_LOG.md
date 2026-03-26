# 學習日誌

## 2026-03-26

### 完成
- 環境建立：.NET 8.0 + PostgreSQL 16 + Rider
- 建立 Solution：GenericMicroservices
- 安裝 EF Core 套件（EntityFrameworkCore / Npgsql / Design）
- 建立 Item.cs 資料模型
- 建立 AppDbContext.cs
- 設定 appsettings.json 連線字串
- Program.cs 完成四個 CRUD endpoint
- Swagger 測試成功，資料寫入 demo_db 確認

### 學到的概念
- Solution vs 專案的關係
- EF Core 作為 ORM 橋接層的角色
- REST API 四個動詞（GET/POST/DELETE）
- db.Database.EnsureCreated() 自動建表

### 下一步
- Repository Pattern：IItemRepository + ItemRepository