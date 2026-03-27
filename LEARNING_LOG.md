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

### Phase 4-A 完成（完整版）
- Demo.GrpcService 建立（gRPC，port 5300）
- items.proto：定義 ItemService、GetItem、GetAllItems、GetAllItemsFromDataService
- ItemGrpcService 實作，連接獨立 grpc_db（EF Core + Migration）
- Demo.GrpcClient Console App（程式碼參考用，非真實 Client）
- DataService 加入 gRPC Server（port 5129，HTTP/2 專用）
- dataservice_items.proto：DataItemService，讓 GrpcService 呼叫 DataService
- GrpcService → gRPC → DataService → demo_db 完整服務間通訊驗證成功
- Postman 測試 gRPC 成功

### 學到的概念（Phase 4-A）
- Protocol Buffers：二進位序列化，自動生成 C# 程式碼，不需手動處理
- .proto 語法：syntax、package、option csharp_namespace、message、service、rpc、repeated
- 欄位編號：給二進位編碼用的識別碼，不是預設值，不可更改
- GrpcServices="Server" vs "Client"：.csproj 中指定生成的程式碼類型
- 自動生成的程式碼放在 obj/Debug/net8.0/Protos/，不需手動維護
- 命名衝突：service 名稱會生成同名 class，實作類別需要不同名稱
- RpcException + StatusCode：gRPC 的錯誤處理方式
- 微服務 DB 原則：每個服務獨立 DB，不跨越服務邊界直接存取
- EnsureCreated vs Migration：前者快速，後者支援版本控制與回滾
- Migration 檔案由工具自動生成，應 commit 進 git
- HTTP/2 開發設定：兩個 Port 各自指定協議，避免 launchSettings.json 衝突
- AppContext.SetSwitch：Client 端開啟明文 HTTP/2 支援
- Port 設定優先順序：ConfigureKestrel > 環境變數 > appsettings > launchSettings
- 正式環境 Port 應由環境變數控制，不寫死在程式碼裡

### 下一步
- Phase 4-B：TCP Socket Raw（自訂協議）
- Phase 5：JWT 身份驗證（安全性）
