# 學習日誌

## 2026-03-30

### Phase 7-C 完成：指標監控（Prometheus + Grafana）
- 所有 HTTP 服務安裝 prometheus-net.AspNetCore，加入 UseMetricServer() + UseHttpMetrics()
- GrpcService Kestrel 從 Http2 改為 Http1AndHttp2，修復 Prometheus scrape 400 錯誤
- prometheus.yml 設定 5 個服務的 scrape targets（15 秒抓一次）
- docker-compose.yml 加入 Prometheus（9090）和 Grafana（3000）
- Grafana 連接 Prometheus 資料來源，可在 Drilldown → Metrics 瀏覽所有指標
- 待深入學習：PromQL 查詢語言、建立自訂儀表板、設定告警

---

### Phase 7-B 完成：分散式追蹤（OpenTelemetry + Jaeger）
- 為 Gateway、AuthService、DataService 安裝 OTel 套件（Extensions.Hosting、Instrumentation.AspNetCore、Instrumentation.Http、Exporter.OpenTelemetryProtocol）
- DataService 額外安裝 `OpenTelemetry.Instrumentation.EntityFrameworkCore`（prerelease，追蹤 SQL 查詢）
- 各服務 Program.cs 加入 `AddOpenTelemetry().WithTracing(...)` 自動埋點
- Jaeger 加入 docker-compose.yml（port 16686 UI + 4317 OTLP gRPC）
- OTLP endpoint 透過設定檔控制：本機用 `http://localhost:4317`，Docker 用 `http://jaeger:4317`
- 追蹤覆蓋範圍：HTTP 請求（AspNetCore）、對外呼叫（HttpClient）、SQL 查詢（EF Core）

---

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
- bcrypt：單向雜湊（不可逆），每次雜湊結果不同（內含 salt），只能用 Verify 比對，後端無法得知原始密碼
- 忘記密碼 → 重設密碼（寄 Email 一次性連結），網站永遠不會寄出你的密碼
- Seed Data：程式啟動時自動建立初始資料，確保開發環境有可用帳號
- AuthService 獨立原則：Gateway 只驗 Token，AuthService 才簽發 Token（SRP）
- YARP AuthorizationPolicy：在 appsettings.json 的 Route 設定授權，不用在程式碼寫
- Refresh Token：隨機字串（非 JWT），存 DB，可主動撤銷；Access Token 無狀態不存 DB
- Refresh Token Rotation：每次換 Token 同時撤銷舊的，防止 Token 被偷後長期濫用
- DB 正規化：外鍵只存 Id，不重複存其他欄位，查詢時用 JOIN 取得關聯資料
- EnsureCreated() vs Migrate()：前者無法更新已存在的 schema，一律改用 Migrate()

#### AuthService 端點完成狀態
- [x] `POST /auth/login`：登入取得 Access Token + Refresh Token
- [x] `POST /auth/refresh`：Refresh Token 換新 Access Token（含 Rotation）
- [x] `POST /auth/logout`：撤銷 Refresh Token
- [x] `POST /auth/users`：建立新使用者（需要 Token）
- [x] `GET  /auth/users`：列出所有使用者（需要 Token）
- [x] `DELETE /auth/users/{id}`：刪除使用者 + 撤銷所有 Refresh Token（需要 Token）
- [x] `PUT /auth/users/{id}/password`：修改密碼 + 撤銷所有 Refresh Token（需要 Token）
- [ ] `POST /auth/forgot-password`：寄重設密碼 Email（需要 SMTP / Email 服務）
- [ ] `POST /auth/reset-password`：用一次性 Token 設新密碼
- [ ] Seed 密碼改從 appsettings / 環境變數讀取（Phase 5-C Secrets 管理）

#### 學到的概念（AuthService 架構）
- AuthService 自己加 JWT 驗證（`RequireAuthorization()`），不完全依賴 Gateway
- 兩層防線：Gateway 擋外部、AuthService 自己保護管理端點
- Gateway 不需要知道 AuthService 內部哪些端點需要 Token，各服務自己管
- 密碼修改、刪除使用者時同時撤銷所有 Refresh Token，強制重新登入

### Phase 5-B 完成（HTTPS / TLS）
- `dotnet dev-certs https --trust` 產生並信任開發用自簽憑證
- Gateway launchSettings.json 加入 `https://localhost:5001;http://localhost:5000`
- 測試：https://localhost:5001/auth/login 成功，Token 正常運作

#### 學到的概念（Phase 5-B）
- TLS 兩件事：加密傳輸（防竊聽） + 身份驗證（防冒牌伺服器）
- 憑證種類：自簽憑證（開發用，瀏覽器警告）vs CA 憑證（正式環境，Let's Encrypt）
- 只有 Gateway 對外 HTTPS，內部服務用 HTTP（同內網，Gateway 已保護最重要的一段）
- launchSettings.json `applicationUrl` 用分號分隔，同時監聽多個 Port
- 5001 是 .NET 社群慣例（不是強制），看到 5001 就知道是 HTTPS
- Port 設定優先順序：ConfigureKestrel > 環境變數 > appsettings.json > launchSettings.json
- 正式環境用環境變數控制 Port，不寫死在程式碼
- 服務啟動時 console 會印出正在監聽的所有 Port

### Phase 5-C 完成（Secrets 管理）
- Demo.AuthService 和 Demo.Gateway 各自執行 `dotnet user-secrets init`
- 敏感值（SecretKey、ConnectionStrings）移入 User Secrets（`~/.microsoft/usersecrets/`）
- appsettings.json 的敏感值清空，非敏感值保留
- 測試：重啟服務後登入正常，Configuration 系統自動從 User Secrets 讀值

#### 學到的概念（Phase 5-C）
- User Secrets 儲存在專案目錄之外（`~/.microsoft/usersecrets/<guid>/secrets.json`），git 追蹤不到
- `<UserSecretsId>` 存在 `.csproj`，是 Secrets 儲存位置的識別碼
- Configuration 優先順序：環境變數 > User Secrets > appsettings.json（後者被前者覆蓋）
- `dotnet user-secrets` 必須在有 `.csproj` 的目錄執行，或用 `--project` 指定
- 敏感值：SecretKey、ConnectionStrings（含帳密）→ 清空；非敏感值：Issuer、Logging → 保留
- Gateway 和 AuthService 的 SecretKey 必須相同，但 User Secrets 是各專案獨立的，需分別 set
- 正式環境改用環境變數注入（Docker Compose Phase 6 實作）

### Phase 5-D 完成（Rate Limiting）
- Demo.Gateway 加入 `AddRateLimiter`（.NET 8 內建，不需額外套件）
- `"login"` policy：每 IP 每 60 秒最多 5 次（/auth/login 專用，防暴力攻擊）
- `"global"` policy：每 IP 每 1 秒最多 20 次（所有路由，防 DDoS）
- `appsettings.json`：拆出 `auth-login-route`，各路由加上 `RateLimiterPolicy`
- 超過限制回傳 HTTP 429 Too Many Requests
- 測試：連續送 7 次登入請求，第 6、7 次收到 429 確認

#### 學到的概念（Phase 5-D）
- Rate Limiting 加在 Gateway：所有流量的入口，集中管理，後面服務不需重複設定
- Fixed Window：固定時間窗格（例如每 60 秒重置一次計數）
- `partitionKey`：以 IP 為單位計數，不同 IP 各自獨立
- `QueueLimit = 0`：超過上限直接拒絕，不排隊等待
- YARP `RateLimiterPolicy`：在 appsettings.json 的 Route 設定，對應 `AddPolicy` 的名稱
- 同一路由可同時有 `AuthorizationPolicy` + `RateLimiterPolicy`，各自獨立運作
- HTTP 429 Too Many Requests：Rate Limiting 的標準回應碼

### Phase 6-A 完成（Docker 容器化）
- 建立 `.dockerignore`（排除 bin/、obj/，避免覆蓋 restore 產生的 obj/）
- 7 個服務各自建立 Dockerfile（Multi-Stage Build）
- 所有 Image build 成功，可獨立啟動

| Image | 大小 |
|-------|------|
| demo-gateway | 88.8MB |
| demo-authservice | 90.8MB |
| demo-dataservice | 94.2MB |
| demo-realtime | 88.3MB |
| demo-grpcservice | 91MB |
| demo-worker | 80.7MB |
| demo-tcpservice | 77.5MB |

#### 學到的概念（Phase 6-A）
- Dockerfile：描述如何打包程式的指令腳本
- Multi-Stage Build：build stage 用 SDK（~900MB），runtime stage 用精簡 Image（~200MB）
- Layer 快取：先 COPY .csproj + restore，再 COPY 原始碼，讓套件不變時跳過 restore
- .NET 8 容器預設 Port：8080（不讀 launchSettings.json）
- Port Mapping：`-p 本機Port:容器Port`，容器內用 8080
- User Secrets 不存在於 Container，改用 `-e` 環境變數注入
- 單引號避免 zsh 展開特殊字元（`!!` 問題）
- `docker run` 佔用 Terminal，需要多個 Terminal 視窗操作
- `aspnet:8.0`（有 HTTP）vs `runtime:8.0`（無 HTTP，更精簡）
- 跨專案參考（Demo.Contracts）：需同時 COPY 兩個 .csproj 才能 restore
- 502 是預期行為：容器內 localhost ≠ 其他容器，Phase 6-B Docker Compose 解決

### Phase 6-B 完成（Docker Compose 多服務整合）
- 建立 `docker-compose.yml`（Solution 根目錄）
  - 9 個服務：gateway、auth-service、data-service、realtime-service、grpc-service、worker、tcp-service、postgres、rabbitmq
  - postgres、rabbitmq 用官方 Image，其餘服務用 `build:` 指令
  - Named Volume `postgres-data`：DB 資料在 Container 重啟後保留
- 建立 `Demo.Gateway/appsettings.Production.json`
  - 覆蓋 Clusters Address：localhost → Docker 服務名稱（auth-service:8080、data-service:5128、realtime-service:8080）
- 修正 DataService Kestrel：`ListenLocalhost` → `ListenAnyIP`
- 測試通過：POST /auth/login → GET /api/items（帶 Token）全部正常

#### 學到的概念（Phase 6-B）
- Docker Compose 內部網路：服務之間用服務名稱互連（不是 localhost）
- `appsettings.Production.json`：Docker 環境自動載入（ASPNETCORE_ENVIRONMENT=Production），覆蓋開發設定
- `ListenLocalhost` vs `ListenAnyIP`：前者只綁 127.0.0.1，其他容器連不到；後者綁 0.0.0.0，容器間可以連
- `depends_on`：控制服務啟動順序，但只等 Container 啟動，不等服務 ready（DB 連線失敗需靠重試機制）
- Named Volume：postgres-data 讓 DB 資料在 `docker compose down` 後仍保留；加 `-v` 才會刪除
- Docker Compose Image 命名：`專案資料夾名稱-服務名稱`（genericmicroservices-gateway），與手動 build 的名稱不同
- Port 佔用問題：macOS AirPlay Receiver 佔用 5000，開發時改用 5010 避開
- 排除舊 Image：`docker compose` 建立自己的 Image，Phase 6-A 的 demo-* Image 不再需要

### Phase 6-C 完成（環境設定）
- DataService：Swagger 改為只在 Development 開啟（`app.Environment.IsDevelopment()`）
- DataService / Worker：`RabbitMq:Host` 補進 appsettings.json（開發預設 localhost）
- 建立 Notes/Phase6C-EnvironmentConfig.md

#### 學到的概念（Phase 6-C）
- `ASPNETCORE_ENVIRONMENT`：決定載入哪個 appsettings.{Environment}.json
- 載入順序：appsettings.json → appsettings.{Env}.json → 環境變數（後者覆蓋前者）
- Docker 預設 Production，本機 dotnet run 預設 Development（來自 launchSettings.json）
- `app.Environment.IsDevelopment()`：判斷目前環境，用來做條件式功能開關
- 正式環境關閉 Swagger：避免暴露 API 結構給攻擊者
- 設定值應在 appsettings.json，不靠程式碼 `?? fallback` 來補

### Phase 6-D 完成（Health Checks）
- 各服務加入 `AddHealthChecks()` + `MapHealthChecks("/health")`（Gateway、Auth、Data、RealTime、Grpc）
- docker-compose.yml：postgres 加入 healthcheck（`pg_isready`）
- depends_on 改用 `condition: service_healthy`，等 DB 真正 ready 才啟動服務
- 建立 Notes/Phase6D-HealthChecks.md

#### 學到的概念（Phase 6-D）
- `depends_on` 只等 Container 啟動，不等服務 ready → 需要 healthcheck + condition 配合
- `pg_isready`：PostgreSQL 內建工具，確認 DB 是否接受連線
- `condition: service_healthy`：等 healthcheck 通過才啟動，比 `service_started` 更安全
- `MapHealthChecks("/health")`：暴露 HTTP 探測端點，回傳 200 Healthy / 503 Unhealthy
- Health Check 是監控基礎設施（Prometheus / K8s liveness probe）的標準介面

### Phase 7-A 完成（結構化日誌 Serilog）
- 所有服務安裝 `Serilog.AspNetCore`
- Program.cs 加入 `UseSerilog`（WebApp）或 `AddSerilog`（Worker）
- appsettings.json 加入 Serilog 區塊（MinimumLevel、WriteTo Console、Enrich）
- 建立 Notes/Phase7A-Serilog.md

#### 學到的概念（Phase 7-A）
- 結構化日誌 vs 純文字：`{Username}` 是獨立欄位，不只是字串替換，可查詢
- Sink：日誌輸出目的地（Console、File、Elasticsearch...），可同時多個
- `MinimumLevel.Override`：針對特定命名空間降低等級，避免框架 log 淹沒業務 log
- `outputTemplate`：Console 輸出格式，`{Level:u3}` = 等級縮寫大寫 3 字
- Worker 用 `AddSerilog`，WebApp 用 `UseSerilog`（API 不同）
- `{}` 佔位符是結構化的關鍵，字串串接無法做到

### Phase 6-B 補充：Docker Compose 多環境（Override 機制）
- `docker-compose.yml` 精簡為基底（共用設定：build、volumes、healthcheck、environment）
- `docker-compose.override.yml`：開發用，對外開放 Port、ASPNETCORE_ENVIRONMENT=Development（自動載入）
- `docker-compose.prod.yml`：正式用，收起非必要 Port、加 restart policy（`-f` 手動指定）
- `docker-compose.staging.yml`：Staging 用，介於開發和正式之間，開放監控 Port 讓 QA 測試
- `Makefile`：Mac / Linux 執行捷徑（`make dev`、`make prod`、`make staging`...）
- `compose.ps1`：Windows 執行捷徑（`.\compose.ps1 dev`，對應 Makefile 所有指令）
- 建立 Notes/Phase6B-2-DockerCompose-MultiEnv.md（含 ASCII 流程圖）
- 建立 Notes/Docker-Localhost-Issue.md（localhost vs 127.0.0.1 完整說明）

#### 學到的概念（Compose Multi-Env）
- Override 機制：`docker compose up` 自動合併 override.yml，後者覆蓋前者同名設定
- ports 合併是追加（不覆蓋），environment 是追加＋同名覆蓋
- `docker compose config`：印出合併後的最終設定，debug 必備
- 正式環境不對外開放 DB port，開發才開放（安全原則）
- `-d` 背景執行是正式環境標配，開發不加方便看 log
- `override` 是保留名稱，自動載入；其他名稱（prod、staging）需手動 `-f`
- `--profile` 控制「哪些服務跑」，override 控制「怎麼跑」，兩者解決不同問題

#### 學到的概念（localhost vs 127.0.0.1）
- `localhost` 是主機名稱，DNS 解析可能優先給 IPv6（::1），導致連線失敗
- `127.0.0.1` 直接指定 IPv4，繞過 DNS，穩定可靠
- 容器內的 `localhost` 是容器自己，不是 Mac 宿主機
- 容器連其他容器用服務名稱（`auth-service:8080`）
- 容器連 Mac 宿主機用 `host.docker.internal`（Docker Desktop 專用）

### 下一步
- Phase 7-B：分散式追蹤（OpenTelemetry）
