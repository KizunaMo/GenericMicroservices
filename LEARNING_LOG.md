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

### Phase 4-B 完成
- Demo.TcpService 建立（Console App，port 5400）
- 基礎封包格式：[ Length ][ Data ]，ReadExactAsync 解決黏包問題
- 擴充封包格式：[ Category ][ SubType ][ Length ][ Data ]
- Protocol 層：MessageCategory、ItemSubType、SystemSubType enum
- IMessageHandler interface + 各 Handler 實作（Strategy Pattern）
- Dictionary 分派器：根據 (Category, SubType) 找對應 Handler
- Demo.TcpTestConsole：測試各種 Category + SubType 組合
- 未知類型回傳錯誤訊息驗證成功

### 學到的概念（Phase 4-B）
- TCP 在協議層的位置（傳輸層，HTTP/gRPC 都建立在它之上）
- 黏包問題：TCP 是串流，ReadAsync 不保證一次讀完，需要 ReadExactAsync
- 封包格式設計：Header 固定，Data 依 SubType 變化
- 兩層分類（Category + SubType）：大分類管理，SubType 各自獨立
- Strategy Pattern 應用：IMessageHandler + Dictionary 分派
- CancellationToken：服務優雅關閉（Ctrl+C）
- 每個 Client 各自一個 Task，主迴圈不被阻塞

### 重構紀錄
- GenericMicroservices 專案改名為 Demo.DataService（命名一致性）
- 建立 Notes/Architecture-Overview.md（架構設計文件，含 ASCII 圖、設計理由、擴充方式）
- 建立 Notes/Packages.md（所有套件一覽，含用途與安裝指令）

---

## 2026-03-27

### Phase 5-A 進行中（JWT 身份驗證）

#### 已完成
- Demo.AuthService 建立（Port 5100，獨立服務）
  - auth_db + Users 資料表（EF Core + PostgreSQL）
  - bcrypt 密碼雜湊（BCrypt.Net-Next）
  - 啟動時 Seed 初始 admin 帳號
  - POST /auth/login：查 DB → bcrypt 驗證 → 簽發 JWT Token
- Demo.Gateway 更新
  - 移除 auth 邏輯，只保留 JWT 驗證（不簽發）
  - /auth/** 路由轉發到 AuthService（不需 Token）
  - /api/**、/hub/** 加上 AuthorizationPolicy，需要有效 Token
- 測試通過：登入取得 Token → 帶 Token 呼叫 API → 不帶 Token 回 401

#### 學到的概念（Phase 5-A）
- JWT 結構：Header.Payload.Signature，三段 Base64Url 編碼
- Claims：放在 Payload 的使用者資訊（Name、Role、NameIdentifier...）
- Signature：用密鑰簽名，Server 用同一把密鑰驗證，Client 無法偽造
- bcrypt：密碼雜湊算法，每次雜湊結果不同（內含 salt），只能用 Verify 比對
- Seed Data：程式啟動時自動建立初始資料，確保開發環境有可用帳號
- AuthService 獨立原則：Gateway 只驗 Token，AuthService 才簽發 Token（SRP）
- YARP AuthorizationPolicy：在 appsettings.json 的 Route 設定授權，不用在程式碼寫

#### AuthService 未來待完成的端點
- [ ] `POST /auth/refresh`：用 Refresh Token 換新的 Access Token（Phase 5-A 下一步）
- [ ] `POST /auth/register`：建立新使用者帳號
- [ ] `GET  /auth/users`：列出所有使用者（需要 admin role）
- [ ] `DELETE /auth/users/{id}`：刪除使用者（需要 admin role）
- [ ] `PUT /auth/users/{id}/password`：修改密碼
- [ ] `POST /auth/forgot-password`：寄重設密碼 Email（需要 SMTP / Email 服務）
- [ ] `POST /auth/reset-password`：用一次性 Token 設新密碼
- [ ] Seed 密碼改從 appsettings / 環境變數讀取（Phase 5-C Secrets 管理）

> 重設密碼流程需要寄 Email（SMTP / SendGrid），安排在 Refresh Token 之後實作。

### 下一步
- Phase 5-A 後段：Refresh Token 機制
