# 學習日誌

## 2026-03-26

### Phase 1 完成
- 環境建立：.NET 8.0 + PostgreSQL 16 + Rider
- 建立 Solution：GenericMicroservices
- 安裝 EF Core 套件（EntityFrameworkCore / Npgsql / Design）
- 建立通用底層：IRepository<T> + Repository<T>（Core/Repositories/）
- 建立 Features/Items/：Item.cs + ItemRepository.cs
- 統一回應格式：ApiResponse<T>（Core/Common/）
- 全域錯誤攔截：ExceptionMiddleware（Core/Common/Middleware/）
- Swagger 測試成功，資料寫入 demo_db 確認

### Phase 2 完成
- Demo.Gateway 建立（YARP，port 5000）
- /api/** → DataService（5128）
- CORS 集中在 Gateway（RequireCors，解決 OPTIONS preflight 問題）

### Phase 3-A 完成
- Demo.RealTime 建立（SignalR，port 5200）
- ChatHub：SendMessage、OnConnected、OnDisconnected
- Gateway 新增 /hub/** → RealTime 路由
- HTML 測試頁面，瀏覽器雙分頁即時通訊驗證成功

### 學到的概念
- Solution vs 專案的關係
- EF Core 作為 ORM 橋接層的角色
- Repository Pattern：IRepository<T> 通用介面 + Repository<T> 底層實作
- `_db.Set<T>()` 泛型存取 EF Core DbSet
- `AddScoped` DI 生命週期
- ApiResponse<T> 統一回應格式
- Middleware 洋蔥模型
- YARP Reverse Proxy 路由設定
- CORS：Same-Origin Policy、preflight、AllowCredentials 限制
- YARP + CORS 必須用 RequireCors（UseCors 單獨不夠）
- WebSocket vs HTTP：長連線 vs 短連線
- SignalR Hub：Clients.All、On、Invoke
- 通訊協議全景：Socket、TCP/UDP、HTTP、WebSocket、gRPC、MQTT、Message Broker

### 下一步
- Phase 3-B：RabbitMQ（Message Broker）
- Phase 4-A：gRPC
- Phase 5：JWT 身份驗證（安全性）
